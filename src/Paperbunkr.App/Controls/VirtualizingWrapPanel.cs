using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Paperbunkr.App.Controls;

/// <summary>
/// A real virtualizing wrap-grid panel (docs/superpowers/specs/2026-08-22-cover-memory-
/// virtualization-design.md) - only realizes containers (and therefore only decodes/holds the
/// <c>Bitmap</c> covers bound into them) for rows near the current viewport, releasing the rest.
/// Root cause this exists for: every <c>SeriesCardSample</c>/<c>BookCardSample</c>/etc. binds its
/// decoded cover permanently into a view-model property, and the plain <c>WrapPanel</c> +
/// <c>ItemsControl</c> combination previously used realizes every container up front (no
/// virtualization support in Avalonia for a wrapping layout) - so a library with N series held N
/// live decoded covers in memory for the entire session, regardless of scroll position.
///
/// Deliberately restricted to <b>uniform</b> item sizes (<see cref="ItemWidth"/>/
/// <see cref="ItemHeight"/> required, both fixed) rather than attempting the general variable-size
/// case <see cref="WrapPanel"/> supports: with a uniform grid, which row/column any index falls in
/// is pure arithmetic (<c>row = index / itemsPerRow</c>) - no need to measure/realize every
/// preceding item just to know where item N sits, unlike a flow layout with per-item variable
/// widths (which is why this is scoped to the four fixed-width Library/Books grid density modes,
/// not the variable-width Panorama mode - that one keeps using the plain, non-virtualizing
/// <see cref="WrapPanel"/> it always has).
///
/// Modeled on Avalonia's real <see cref="VirtualizingStackPanel"/> (container realize/recycle
/// protocol via <see cref="ItemContainerGenerator"/>, <c>EffectiveViewportChanged</c>-driven
/// virtualization) - ported down to what the uniform-grid case actually needs. Left out
/// deliberately: <c>IScrollAnchorProvider</c> integration and estimated-size refinement (both exist
/// in the real control purely to smooth over *unknown* element sizes causing scroll-position
/// jitter as they're measured for the first time - moot here since every element's size and
/// position is known exactly up front) and per-item incremental insert/remove tracking (this app's
/// screens always repopulate their card collections via full Clear()+re-Add(), never fine-grained
/// single-item inserts, so treating every collection-changed notification as a full re-realize is
/// both correct and exactly as expensive as what real usage needs).
/// </summary>
public class VirtualizingWrapPanel : VirtualizingPanel
{
    public static readonly StyledProperty<double> ItemWidthProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(ItemWidth));

    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(ItemHeight));

    public static readonly StyledProperty<double> ItemSpacingProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(ItemSpacing));

    public static readonly StyledProperty<double> LineSpacingProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(LineSpacing));

    /// <summary>How many extra rows beyond the visible viewport stay realized on each side - a small buffer avoids a visible pop-in flash and container churn during smooth scrolling.</summary>
    private const int BufferRows = 2;

    private static readonly AttachedProperty<object?> RecycleKeyProperty =
        AvaloniaProperty.RegisterAttached<VirtualizingWrapPanel, Control, object?>("RecycleKey");

    private static readonly AttachedProperty<int> ItemIndexProperty =
        AvaloniaProperty.RegisterAttached<VirtualizingWrapPanel, Control, int>("ItemIndex", -1);

    private readonly Dictionary<int, Control> _realizedByIndex = new();
    private readonly Dictionary<object, Stack<Control>> _recyclePool = new();
    private Rect _viewport;
    private int _itemsPerRow = 1;
    private int _totalRows;

    static VirtualizingWrapPanel()
    {
        AffectsMeasure<VirtualizingWrapPanel>(ItemWidthProperty, ItemHeightProperty, ItemSpacingProperty, LineSpacingProperty);
    }

    public VirtualizingWrapPanel()
    {
        EffectiveViewportChanged += OnEffectiveViewportChanged;
    }

    public double ItemWidth
    {
        get => GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public double ItemSpacing
    {
        get => GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    public double LineSpacing
    {
        get => GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double itemWidth = ItemWidth;
        double itemHeight = ItemHeight;
        double spacing = ItemSpacing;
        double lineSpacing = LineSpacing;
        int count = Items.Count;

        var layout = VirtualizingWrapGridMath.ComputeLayout(count, availableSize.Width, itemWidth, itemHeight, spacing, lineSpacing);
        _itemsPerRow = layout.ItemsPerRow;
        _totalRows = layout.TotalRows;

        if (layout.TotalRows == 0)
        {
            DerealizeAll();
            return default;
        }

        RealizeViewportRange(count, layout, itemHeight, lineSpacing);

        foreach (var element in _realizedByIndex.Values)
        {
            element.Measure(new Size(itemWidth, itemHeight));
        }

        return new Size(availableSize.Width, layout.TotalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double itemWidth = ItemWidth;
        double itemHeight = ItemHeight;
        double spacing = ItemSpacing;
        double lineSpacing = LineSpacing;

        foreach (var (index, element) in _realizedByIndex)
        {
            int row = index / _itemsPerRow;
            int col = index % _itemsPerRow;
            double x = col * (itemWidth + spacing);
            double y = row * (itemHeight + lineSpacing);
            element.Arrange(new Rect(x, y, itemWidth, itemHeight));
        }

        return finalSize;
    }

    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        // Every current caller repopulates via full Clear()+re-Add() rather than fine-grained
        // single-item inserts/removes (see this class's own doc comment) - a full re-realize on
        // any change is correct for that usage and far simpler than porting index-shifting logic
        // nothing here would ever exercise.
        DerealizeAll();
        InvalidateMeasure();
    }

    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        int count = Items.Count;
        if (count == 0 || _itemsPerRow <= 0)
        {
            return null;
        }

        var fromControl = from as Control;
        int fromIndex = fromControl is not null ? IndexFromContainer(fromControl) : -1;
        int toIndex = fromIndex;

        switch (direction)
        {
            case NavigationDirection.First:
                toIndex = 0;
                break;
            case NavigationDirection.Last:
                toIndex = count - 1;
                break;
            case NavigationDirection.Next:
                toIndex = fromIndex + 1;
                break;
            case NavigationDirection.Previous:
                toIndex = fromIndex - 1;
                break;
            case NavigationDirection.Left:
                toIndex = fromIndex - 1;
                break;
            case NavigationDirection.Right:
                toIndex = fromIndex + 1;
                break;
            case NavigationDirection.Up:
                toIndex = fromIndex - _itemsPerRow;
                break;
            case NavigationDirection.Down:
                toIndex = fromIndex + _itemsPerRow;
                break;
            default:
                return null;
        }

        if (fromIndex == toIndex)
        {
            return from;
        }

        if (wrap)
        {
            if (toIndex < 0) toIndex = count - 1;
            else if (toIndex >= count) toIndex = 0;
        }
        else if (toIndex < 0 || toIndex >= count)
        {
            return null;
        }

        return ScrollIntoView(toIndex);
    }

    protected override IEnumerable<Control>? GetRealizedContainers() => _realizedByIndex.Values;

    protected override Control? ContainerFromIndex(int index) =>
        _realizedByIndex.TryGetValue(index, out var element) ? element : null;

    protected override int IndexFromContainer(Control container) =>
        container.GetValue(ItemIndexProperty);

    protected override Control? ScrollIntoView(int index)
    {
        var items = Items;
        if (index < 0 || index >= items.Count || _itemsPerRow <= 0)
        {
            return null;
        }

        if (ContainerFromIndex(index) is { } existing)
        {
            existing.BringIntoView();
            return existing;
        }

        // Not currently realized - force-realize it so BringIntoView has a real element to target,
        // matching the real VirtualizingStackPanel's own ScrollIntoView approach for an
        // off-viewport index.
        var element = GetOrCreateElement(items, index);
        double rowStride = ItemHeight + LineSpacing;
        int row = index / _itemsPerRow;
        int col = index % _itemsPerRow;
        element.Measure(new Size(ItemWidth, ItemHeight));
        element.Arrange(new Rect(col * (ItemWidth + ItemSpacing), row * rowStride, ItemWidth, ItemHeight));
        element.BringIntoView();
        return element;
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        var oldViewport = _viewport;
        _viewport = e.EffectiveViewport.Intersect(new Rect(Bounds.Size));

        if (!MathUtilitiesAreClose(oldViewport.Top, _viewport.Top) || !MathUtilitiesAreClose(oldViewport.Bottom, _viewport.Bottom))
        {
            InvalidateMeasure();
        }
    }

    private static bool MathUtilitiesAreClose(double a, double b) => Math.Abs(a - b) < 0.5;

    private void RealizeViewportRange(int count, WrapGridLayout layout, double itemHeight, double lineSpacing)
    {
        var range = VirtualizingWrapGridMath.ComputeRealizedRange(
            count, layout, _viewport.Top, _viewport.Bottom, itemHeight, lineSpacing, BufferRows);

        if (range.IsEmpty)
        {
            DerealizeAll();
            return;
        }

        var items = Items;

        // Derealize anything now outside the wanted range.
        foreach (int staleIndex in _realizedByIndex.Keys.Where(i => i < range.FirstIndex || i > range.LastIndex).ToList())
        {
            RecycleElement(_realizedByIndex[staleIndex], staleIndex);
            _realizedByIndex.Remove(staleIndex);
        }

        // Realize anything newly in range.
        for (int i = range.FirstIndex; i <= range.LastIndex; i++)
        {
            if (!_realizedByIndex.ContainsKey(i))
            {
                _realizedByIndex[i] = GetOrCreateElement(items, i);
            }
        }
    }

    private void DerealizeAll()
    {
        foreach (var (index, element) in _realizedByIndex.ToList())
        {
            RecycleElement(element, index);
        }

        _realizedByIndex.Clear();
    }

    private Control GetOrCreateElement(IReadOnlyList<object?> items, int index)
    {
        if (_realizedByIndex.TryGetValue(index, out var existing))
        {
            return existing;
        }

        var generator = ItemContainerGenerator!;
        object? item = items[index];

        if (!generator.NeedsContainer(item, index, out object? recycleKey))
        {
            var ownContainer = (Control)item!;
            generator.PrepareItemContainer(ownContainer, item, index);
            AddInternalChild(ownContainer);
            ownContainer.SetValue(ItemIndexProperty, index);
            generator.ItemContainerPrepared(ownContainer, item, index);
            return ownContainer;
        }

        if (recycleKey is not null && _recyclePool.TryGetValue(recycleKey, out var pool) && pool.Count > 0)
        {
            var recycled = pool.Pop();
            recycled.SetCurrentValue(Visual.IsVisibleProperty, true);
            recycled.SetValue(ItemIndexProperty, index);
            generator.PrepareItemContainer(recycled, item, index);
            generator.ItemContainerPrepared(recycled, item, index);
            return recycled;
        }

        var container = generator.CreateContainer(item, index, recycleKey);
        container.SetValue(RecycleKeyProperty, recycleKey);
        container.SetValue(ItemIndexProperty, index);
        generator.PrepareItemContainer(container, item, index);
        AddInternalChild(container);
        generator.ItemContainerPrepared(container, item, index);
        return container;
    }

    private void RecycleElement(Control element, int index)
    {
        var generator = ItemContainerGenerator!;
        object? recycleKey = element.GetValue(RecycleKeyProperty);

        if (recycleKey is null)
        {
            // NeedsContainer returned false for this item - the item is its own container and
            // can't be pooled/reused for a different item; just hide it (matches the real
            // VirtualizingStackPanel's "own container" handling).
            element.SetCurrentValue(Visual.IsVisibleProperty, false);
            return;
        }

        generator.ClearItemContainer(element);

        if (!_recyclePool.TryGetValue(recycleKey, out var pool))
        {
            pool = new Stack<Control>();
            _recyclePool[recycleKey] = pool;
        }

        pool.Push(element);
        element.SetCurrentValue(Visual.IsVisibleProperty, false);
    }
}
