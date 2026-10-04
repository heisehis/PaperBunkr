using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Controls.EventMap;

/// <summary>
/// The Event Map's scrollable content (docs/superpowers/specs/2026-09-25-event-map-design.md §3, approach B). Measures
/// to the layout's total size; realizes <see cref="EventMapCard"/>s only for the visible cells (plus one column of
/// overscan), recycling them from a pool as the viewport moves - a card scrolling into a new cell is rebound, never
/// recreated. Follows <see cref="VirtualizingWrapPanel"/>'s <c>EffectiveViewportChanged</c> pattern, but as a plain
/// <see cref="Panel"/>: cells sit at grid coordinates the pure layout already computed, nothing flows. The
/// <see cref="EventMapEdgeLayer"/> is child 0 and fills the surface. Pooled cards stay children (hidden), so recycling
/// never touches the visual tree.
/// </summary>
public sealed class EventMapSurface : Panel
{
    public static readonly StyledProperty<EventMapLayoutResult?> LayoutProperty =
        AvaloniaProperty.Register<EventMapSurface, EventMapLayoutResult?>(nameof(Layout));

    public static readonly StyledProperty<IReadOnlyList<EventMapCardViewModel>?> CardsProperty =
        AvaloniaProperty.Register<EventMapSurface, IReadOnlyList<EventMapCardViewModel>?>(nameof(Cards));

    public static readonly StyledProperty<IReadOnlySet<int>?> RelatedSetProperty =
        AvaloniaProperty.Register<EventMapSurface, IReadOnlySet<int>?>(nameof(RelatedSet));

    private readonly EventMapEdgeLayer _edges = new();
    private readonly Dictionary<int, EventMapCard> _realized = new();
    private readonly Stack<EventMapCard> _pool = new();
    private readonly List<int> _scratch = new();
    private Rect _viewport;
    private EventMapVisibleRange _realizedRange = EventMapVisibleRange.Empty;

    static EventMapSurface()
    {
        AffectsMeasure<EventMapSurface>(LayoutProperty, CardsProperty);
    }

    public EventMapSurface()
    {
        Focusable = true;
        FocusAdorner = null;
        Children.Add(_edges);
        EffectiveViewportChanged += OnEffectiveViewportChanged;
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    public EventMapLayoutResult? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public IReadOnlyList<EventMapCardViewModel>? Cards
    {
        get => GetValue(CardsProperty);
        set => SetValue(CardsProperty, value);
    }

    public IReadOnlySet<int>? RelatedSet
    {
        get => GetValue(RelatedSetProperty);
        set => SetValue(RelatedSetProperty, value);
    }

    /// <summary>A card was pressed: (cell index, click count). The view turns 1 into select+inspect and 2 into open-reader.</summary>
    public event Action<int, int>? CardPressed;

    // Test seams (virtualization bounds).
    internal int RealizedCount => _realized.Count;

    internal int PooledCount => _pool.Count;

    internal int CreatedCount => Children.Count - 1;

    internal IEnumerable<int> RealizedIndices => _realized.Keys;

    internal EventMapEdgeLayer EdgeLayer => _edges;

    /// <summary>The realized card for a cell, if it is on screen.</summary>
    public EventMapCard? CardAt(int index) => _realized.GetValueOrDefault(index);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LayoutProperty)
        {
            _edges.Layout = Layout;
            _realizedRange = EventMapVisibleRange.Empty;    // force a full rebind on the next measure
        }
        else if (change.Property == CardsProperty)
        {
            _realizedRange = EventMapVisibleRange.Empty;
        }
        else if (change.Property == RelatedSetProperty)
        {
            _edges.RelatedSet = RelatedSet;
        }
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        _viewport = e.EffectiveViewport;
        _edges.Viewport = _viewport;
        if (Layout is { } layout && layout.VisibleRange(_viewport) != _realizedRange)
        {
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var layout = Layout;
        if (layout is null || layout.IsEmpty)
        {
            RecycleAll();
            _edges.Measure(default);
            return default;
        }

        Realize(layout);
        var cardSize = new Size(layout.Metrics.CardWidth, layout.Metrics.CardHeight);
        foreach (var card in _realized.Values)
        {
            card.Measure(cardSize);
        }

        _edges.Measure(layout.TotalSize);
        return layout.TotalSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var layout = Layout;
        _edges.Arrange(new Rect(finalSize));
        if (layout is null)
        {
            return finalSize;
        }

        foreach (var (index, card) in _realized)
        {
            card.Arrange(layout.CardRect(index));
        }

        return finalSize;
    }

    /// <summary>Realizes exactly the cells in the current visible range, recycling the rest. Rebinding is a DataContext swap.</summary>
    internal void Realize(EventMapLayoutResult layout)
    {
        var cards = Cards;
        var range = layout.VisibleRange(_viewport);
        var needed = new HashSet<int>(layout.CellsIn(range).Where(i => cards is not null && i < cards.Count));

        _scratch.Clear();
        _scratch.AddRange(_realized.Keys.Where(i => !needed.Contains(i)));
        foreach (int stale in _scratch)
        {
            Recycle(stale);
        }

        var theme = ThemeFor(layout.Density);
        foreach (int index in needed)
        {
            if (!_realized.TryGetValue(index, out var card))
            {
                card = _pool.Count > 0 ? _pool.Pop() : CreateCard();
                card.IsVisible = true;
                _realized[index] = card;
            }

            card.DataContext = cards![index];
            card.Density = layout.Density;
            if (theme is not null && !ReferenceEquals(card.Theme, theme))
            {
                card.Theme = theme;
            }
        }

        _realizedRange = range;
    }

    private EventMapCard CreateCard()
    {
        var card = new EventMapCard { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        Children.Add(card);
        return card;
    }

    private void Recycle(int index)
    {
        if (_realized.Remove(index, out var card))
        {
            if (card.IsKeyboardFocusWithin)
            {
                // The card that had focus is scrolling out of range. Hiding it would leave nothing focused, and the next arrow press (which can arrive before the view re-focuses the selected card) would
                // go nowhere; the surface holds focus in the meantime.
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is null)
                    {
                        Focus(NavigationMethod.Directional);
                    }
                }, Avalonia.Threading.DispatcherPriority.Send);
            }

            card.DataContext = null;
            card.IsVisible = false;
            _pool.Push(card);
        }
    }

    private void RecycleAll()
    {
        foreach (int index in _realized.Keys.ToList())
        {
            Recycle(index);
        }

        _realizedRange = EventMapVisibleRange.Empty;
    }

    /// <summary>One card theme per density stop, defined by <c>EventMapView</c> as <c>EventMapCard{Density}Theme</c>.</summary>
    private ControlTheme? ThemeFor(EventMapDensity density) =>
        this.TryFindResource($"EventMapCard{density}Theme", ActualThemeVariant, out object? value) ? value as ControlTheme : null;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var card = (e.Source as Visual)?.FindAncestorOfType<EventMapCard>(includeSelf: true);
        if (card is { CellIndex: >= 0 })
        {
            card.Focus(NavigationMethod.Pointer);
            CardPressed?.Invoke(card.CellIndex, e.ClickCount);
            e.Handled = true;
        }
    }
}
