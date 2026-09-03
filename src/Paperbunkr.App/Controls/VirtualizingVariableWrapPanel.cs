using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Controls;

/// <summary>
/// A virtualizing wrap panel for <b>variable-width</b>, uniform-height tiles - the sibling of
/// <see cref="VirtualizingWrapPanel"/> (which stays deliberately uniform-cell, the fast path the
/// four fixed Library/Books density grids use). This one exists for the Panorama grid, whose whole
/// reason to exist is that each cover renders at its true aspect ratio: landscape covers wide,
/// portrait covers narrow, side by side. Each item supplies its own width via
/// <see cref="IVariableWidthTile.PreferredWidth"/>, so row packing is known from the data without
/// realizing every container to measure it - the exact eager-realize this whole virtualization
/// effort removed.
///
/// Same realize/recycle protocol as <see cref="VirtualizingWrapPanel"/> (container generation via
/// <see cref="ItemContainerGenerator"/>, <c>EffectiveViewportChanged</c>-driven virtualization,
/// <see cref="INavigableContainer"/> for arrow-key nav). The one structural difference: the packed
/// layout (<see cref="VariableWrapLayout"/>) is cached and only recomputed when the item set or
/// the available width actually changes - a vertical scroll just re-runs the O(rows) viewport-range
/// math against the cached layout, allocating nothing. Every current caller repopulates its source
/// collection via full Clear()+re-Add(), so a plain re-pack on any collection change is both
/// correct and no more expensive than real usage needs.
/// </summary>
public class VirtualizingVariableWrapPanel : VirtualizingPanel
{
    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<VirtualizingVariableWrapPanel, double>(nameof(ItemHeight));

    public static readonly StyledProperty<double> ItemSpacingProperty =
        AvaloniaProperty.Register<VirtualizingVariableWrapPanel, double>(nameof(ItemSpacing));

    public static readonly StyledProperty<double> LineSpacingProperty =
        AvaloniaProperty.Register<VirtualizingVariableWrapPanel, double>(nameof(LineSpacing));

    /// <summary>Extra rows kept realized beyond the viewport on each side - a small buffer hides
    /// pop-in and container churn during smooth scrolling.</summary>
    private const int BufferRows = 2;

    private static readonly AttachedProperty<object?> RecycleKeyProperty =
        AvaloniaProperty.RegisterAttached<VirtualizingVariableWrapPanel, Control, object?>("RecycleKey");

    private static readonly AttachedProperty<int> ItemIndexProperty =
        AvaloniaProperty.RegisterAttached<VirtualizingVariableWrapPanel, Control, int>("ItemIndex", -1);

    private readonly Dictionary<int, Control> _realizedByIndex = new();
    private readonly Dictionary<object, Stack<Control>> _recyclePool = new();
    private Rect _viewport;

    private VariableWrapLayout _layout = VariableWrapLayout.Empty;
    private double[] _itemWidths = Array.Empty<double>();
    private (int Count, double Width, double Height, double ItemSpacing, double LineSpacing) _layoutKey;
    private bool _layoutValid;

    static VirtualizingVariableWrapPanel()
    {
        AffectsMeasure<VirtualizingVariableWrapPanel>(ItemHeightProperty, ItemSpacingProperty, LineSpacingProperty);
    }

    public VirtualizingVariableWrapPanel()
    {
        EffectiveViewportChanged += OnEffectiveViewportChanged;
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
        double itemHeight = ItemHeight;
        double itemSpacing = ItemSpacing;
        double lineSpacing = LineSpacing;
        int count = Items.Count;

        EnsureLayout(count, availableSize.Width, itemHeight, itemSpacing, lineSpacing);

        if (_layout.TotalRows == 0)
        {
            DerealizeAll();
            return default;
        }

        RealizeViewportRange(itemHeight, lineSpacing);

        foreach (var (index, element) in _realizedByIndex)
        {
            element.Measure(new Size(WidthAt(index), itemHeight));
        }

        double width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        return new Size(width, _layout.TotalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double itemHeight = ItemHeight;
        double rowStride = itemHeight + LineSpacing;

        foreach (var (index, element) in _realizedByIndex)
        {
            if (index >= _layout.RowOfItem.Count)
            {
                continue;
            }

            double x = _layout.XOfItem[index];
            double y = _layout.RowOfItem[index] * rowStride;
            element.Arrange(new Rect(x, y, WidthAt(index), itemHeight));
        }

        return finalSize;
    }

    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        // Every caller repopulates via full Clear()+re-Add() (see the class doc comment), including
        // Panorama's aspect-ratio reflow, which rebuilds the card instances so their PreferredWidth
        // changes. A full re-pack on any change is correct for that and far simpler than porting
        // index-shifting logic nothing exercises.
        _layoutValid = false;
        DerealizeAll();
        InvalidateMeasure();
    }

    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        int count = Items.Count;
        if (count == 0 || _layout.TotalRows == 0)
        {
            return null;
        }

        var fromControl = from as Control;
        int fromIndex = fromControl is not null ? IndexFromContainer(fromControl) : -1;
        int toIndex;

        switch (direction)
        {
            case NavigationDirection.First:
                toIndex = 0;
                break;
            case NavigationDirection.Last:
                toIndex = count - 1;
                break;
            case NavigationDirection.Next:
            case NavigationDirection.Right:
                toIndex = fromIndex + 1;
                break;
            case NavigationDirection.Previous:
            case NavigationDirection.Left:
                toIndex = fromIndex - 1;
                break;
            case NavigationDirection.Up:
            case NavigationDirection.Down:
                if (fromIndex < 0 || fromIndex >= _layout.RowOfItem.Count)
                {
                    return null;
                }

                int targetRow = _layout.RowOfItem[fromIndex] + (direction == NavigationDirection.Down ? 1 : -1);
                toIndex = VirtualizingVariableWrapMath.NearestInRow(_layout, targetRow, _layout.XOfItem[fromIndex]);
                if (toIndex < 0)
                {
                    return from;
                }

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

    /// <summary>Walks up from whatever control has focus to the realized container that actually
    /// carries <see cref="ItemIndexProperty"/> - same fix (and reason) as
    /// <see cref="VirtualizingWrapPanel.IndexFromContainer"/>.</summary>
    protected override int IndexFromContainer(Control container)
    {
        for (Control? current = container; current is not null; current = current.GetVisualParent() as Control)
        {
            if (current.IsSet(ItemIndexProperty))
            {
                return current.GetValue(ItemIndexProperty);
            }

            if (ReferenceEquals(current, this))
            {
                break;
            }
        }

        return -1;
    }

    protected override Control? ScrollIntoView(int index)
    {
        var items = Items;
        if (index < 0 || index >= items.Count)
        {
            return null;
        }

        EnsureLayout(items.Count, Bounds.Width, ItemHeight, ItemSpacing, LineSpacing);
        if (index >= _layout.RowOfItem.Count)
        {
            return null;
        }

        if (ContainerFromIndex(index) is { } existing)
        {
            existing.BringIntoView();
            return existing;
        }

        var element = GetOrCreateElement(items, index);
        double rowStride = ItemHeight + LineSpacing;
        element.Measure(new Size(WidthAt(index), ItemHeight));
        element.Arrange(new Rect(_layout.XOfItem[index], _layout.RowOfItem[index] * rowStride, WidthAt(index), ItemHeight));
        element.BringIntoView();
        return element;
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        var oldViewport = _viewport;
        _viewport = e.EffectiveViewport.Intersect(new Rect(Bounds.Size));

        if (Math.Abs(oldViewport.Top - _viewport.Top) > 0.5 || Math.Abs(oldViewport.Bottom - _viewport.Bottom) > 0.5)
        {
            InvalidateMeasure();
        }
    }

    private void EnsureLayout(int count, double availableWidth, double itemHeight, double itemSpacing, double lineSpacing)
    {
        var key = (count, availableWidth, itemHeight, itemSpacing, lineSpacing);
        if (_layoutValid && _layoutKey == key)
        {
            return;
        }

        _itemWidths = new double[count];
        for (int i = 0; i < count; i++)
        {
            _itemWidths[i] = Items[i] is IVariableWidthTile tile && tile.PreferredWidth > 0
                ? tile.PreferredWidth
                : itemHeight * (2.0 / 3.0);
        }

        _layout = VirtualizingVariableWrapMath.ComputeLayout(_itemWidths, availableWidth, itemHeight, itemSpacing, lineSpacing);
        _layoutKey = key;
        _layoutValid = true;
    }

    private double WidthAt(int index) =>
        index >= 0 && index < _itemWidths.Length ? _itemWidths[index] : ItemHeight * (2.0 / 3.0);

    private void RealizeViewportRange(double itemHeight, double lineSpacing)
    {
        var range = VirtualizingVariableWrapMath.ComputeRealizedRange(
            _layout, _viewport.Top, _viewport.Bottom, itemHeight, lineSpacing, BufferRows);

        if (range.IsEmpty)
        {
            DerealizeAll();
            return;
        }

        var items = Items;

        foreach (int staleIndex in _realizedByIndex.Keys.Where(i => i < range.FirstIndex || i > range.LastIndex).ToList())
        {
            RecycleElement(_realizedByIndex[staleIndex], staleIndex);
            _realizedByIndex.Remove(staleIndex);
        }

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
