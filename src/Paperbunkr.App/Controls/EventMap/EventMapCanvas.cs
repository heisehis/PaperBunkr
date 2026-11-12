using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Input;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Controls.EventMap;

/// <summary>A single, viewport-culled drawing surface for the event and continuity maps.</summary>
public sealed partial class EventMapCanvas : Control
{
    public static readonly StyledProperty<EventMapLayoutResult?> LayoutProperty = AvaloniaProperty.Register<EventMapCanvas, EventMapLayoutResult?>(nameof(Layout));
    public static readonly StyledProperty<IReadOnlyList<EventMapCardViewModel>?> CardsProperty = AvaloniaProperty.Register<EventMapCanvas, IReadOnlyList<EventMapCardViewModel>?>(nameof(Cards));
    public static readonly StyledProperty<IReadOnlyList<EventMapLaneHeader>?> LaneHeadersProperty = AvaloniaProperty.Register<EventMapCanvas, IReadOnlyList<EventMapLaneHeader>?>(nameof(LaneHeaders));
    public static readonly StyledProperty<IReadOnlyList<EventMapConnector>?> ConnectorsProperty = AvaloniaProperty.Register<EventMapCanvas, IReadOnlyList<EventMapConnector>?>(nameof(Connectors));
    public static readonly StyledProperty<IReadOnlySet<int>?> RelatedSetProperty = AvaloniaProperty.Register<EventMapCanvas, IReadOnlySet<int>?>(nameof(RelatedSet));
    public static readonly StyledProperty<int?> SelectedIndexProperty = AvaloniaProperty.Register<EventMapCanvas, int?>(nameof(SelectedIndex));
    public static readonly StyledProperty<double> ScaleProperty = AvaloniaProperty.Register<EventMapCanvas, double>(nameof(Scale), 1);
    public static readonly StyledProperty<Vector> OffsetProperty = AvaloniaProperty.Register<EventMapCanvas, Vector>(nameof(Offset));
    public static readonly StyledProperty<bool> IsLaneRailWideProperty = AvaloniaProperty.Register<EventMapCanvas, bool>(nameof(IsLaneRailWide));
    public static readonly StyledProperty<double> RulerHeightProperty = AvaloniaProperty.Register<EventMapCanvas, double>(nameof(RulerHeight), 28);
    public static readonly StyledProperty<double> ObscuredRightProperty = AvaloniaProperty.Register<EventMapCanvas, double>(nameof(ObscuredRight));

    private IReadOnlyList<EventMapCardViewModel>? _subscribedCards;
    private bool _clamping;
    private bool _zoomAnchoring;
    private EventMapCoverSource? _coverSource;
    private readonly EventMapTextCache _textCache = new();

    static EventMapCanvas() => AffectsRender<EventMapCanvas>(LayoutProperty, CardsProperty, LaneHeadersProperty, ConnectorsProperty,
        RelatedSetProperty, SelectedIndexProperty, ScaleProperty, OffsetProperty, IsLaneRailWideProperty, RulerHeightProperty, ShowProgressLineProperty,
        FeatureSetProperty, BarColorsProperty, BracketsProperty);

    public EventMapCanvas()
    {
        _coverSource = new EventMapCoverSource(InvalidateVisual);
        Focusable = true;
        FocusAdorner = null;
        ClipToBounds = true;
        GestureRecognizers.Add(new PinchGestureRecognizer());
        AddHandler(PinchEvent, OnPinch);
        AddHandler(PinchEndedEvent, OnPinchEnded);
        AddHandler(PointerTouchPadGestureMagnifyEvent, (_, e) =>
        {
            var p = e.GetPosition(this);
            double factor = 1 + e.Delta.X;
            if (double.IsFinite(factor) && factor > 0) ZoomAbout(new Point(p.X - RailWidth, p.Y - RulerHeight), factor);
            e.Handled = true;
        });
        ActualThemeVariantChanged += (_, _) => ClearRenderCaches();
        ResourcesChanged += (_, _) => ClearRenderCaches();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _coverSource ??= new EventMapCoverSource(InvalidateVisual);
        HookPulseHost();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _coverSource?.Dispose();
        _coverSource = null;
        _coverMode = -1;
        _textCache.Clear();
        ClearRenderCaches();
        ClearBandArt();
        ClearFaces();
        UnhookPulseHost();
        _traceHops = null;
    }

    public EventMapLayoutResult? Layout { get => GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }
    public IReadOnlyList<EventMapCardViewModel>? Cards { get => GetValue(CardsProperty); set => SetValue(CardsProperty, value); }
    public IReadOnlyList<EventMapLaneHeader>? LaneHeaders { get => GetValue(LaneHeadersProperty); set => SetValue(LaneHeadersProperty, value); }
    public IReadOnlyList<EventMapConnector>? Connectors { get => GetValue(ConnectorsProperty); set => SetValue(ConnectorsProperty, value); }
    public IReadOnlySet<int>? RelatedSet { get => GetValue(RelatedSetProperty); set => SetValue(RelatedSetProperty, value); }
    public int? SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public double Scale { get => GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
    public Vector Offset { get => GetValue(OffsetProperty); set => SetValue(OffsetProperty, value); }
    public bool IsLaneRailWide { get => GetValue(IsLaneRailWideProperty); set => SetValue(IsLaneRailWideProperty, value); }
    public double RulerHeight { get => GetValue(RulerHeightProperty); set => SetValue(RulerHeightProperty, value); }
    public double ObscuredRight { get => GetValue(ObscuredRightProperty); set => SetValue(ObscuredRightProperty, value); }
    public double RailWidth => IsLaneRailWide ? 180 : 52;
    public Size ContentViewport => new(Math.Max(0, Bounds.Width - RailWidth), Math.Max(0, Bounds.Height - RulerHeight));
    public EventMapMetrics Metrics => EventMapMetrics.ForScale(Scale);
    public event Action<int, int>? CardPressed;
    public event Action? SelectionCleared;
    public event Action<int, int>? BandPressed;
    public event Action<int>? LanePressed;
    public event Action<int?>? PeekChanged;
    public event Action? ViewportChanged;

    protected override AutomationPeer OnCreateAutomationPeer() => new EventMapCanvasAutomationPeer(this);

    internal void SelectFromAutomation(int index) => CardPressed?.Invoke(index, 1);
    internal void ClearSelectionFromAutomation() => SelectionCleared?.Invoke();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ScaleProperty && !_zoomAnchoring && change.OldValue is double oldScale && oldScale > 0 && change.NewValue is double nextScale)
        {
            var viewport = ContentViewport;
            var anchor = new Point(viewport.Width / 2, viewport.Height / 2);
            double ratio = nextScale / oldScale;
            SetCurrentValue(OffsetProperty, new Vector(((Offset.X + anchor.X) * ratio) - anchor.X,
                ((Offset.Y + anchor.Y) * ratio) - anchor.Y));
        }
        if (change.Property == LayoutProperty) { _textCache.Clear(); _edgeGeometries.Clear(); _coverMode = -1; InvalidateDots(); }
        if (change.Property == ScaleProperty) { _edgeGeometries.Clear(); InvalidateDots(); }
        if (change.Property == SelectedIndexProperty && ControlAutomationPeer.FromElement(this) is EventMapCanvasAutomationPeer peer)
            peer.SelectionChanged(change.OldValue as int?, change.NewValue as int?);
        if (change.Property == SelectedIndexProperty) ViewportChanged?.Invoke();
        if (change.Property == RelatedSetProperty) { InvalidateDots(); if (!StartTrace()) AnimateDimming(change.OldValue as IReadOnlySet<int>); }
        if (change.Property == LayoutProperty) _traceHops = null;
        if (change.Property == FeatureSetProperty || change.Property == BarColorsProperty) InvalidateDots();
        if (change.Property == ScaleProperty && EventMapCardLooks.For(Metrics.CardWidth) >= EventMapCardLook.Standard) ClearPeek();
        if (change.Property == CardsProperty)
        {
            InvalidateDots();
            _coverMode = -1;
            if (_subscribedCards is not null) foreach (var card in _subscribedCards) card.PropertyChanged -= CardChanged;
            _subscribedCards = Cards;
            if (_subscribedCards is not null) foreach (var card in _subscribedCards) card.PropertyChanged += CardChanged;
            ViewportChanged?.Invoke();
        }
        if (change.Property == LayoutProperty || change.Property == ScaleProperty || change.Property == OffsetProperty ||
            change.Property == IsLaneRailWideProperty || change.Property == RulerHeightProperty || change.Property == ObscuredRightProperty || change.Property == BoundsProperty)
        {
            ClampOffset();
            ViewportChanged?.Invoke();
        }
    }

    private void CardChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvalidateVisual();
        if (e.PropertyName == nameof(EventMapCardViewModel.FacePath)) return;
        if (e.PropertyName == nameof(EventMapCardViewModel.ReadState)) { InvalidateDots(); ViewportChanged?.Invoke(); }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        ClampOffset();
        ViewportChanged?.Invoke();
        return size;
    }

    private void ClampOffset()
    {
        if (_clamping || Layout is null) return;
        var size = Layout.TotalSizeFor(Metrics);
        var viewport = ContentViewport;
        var clamped = new Vector(Math.Clamp(Offset.X, 0, Math.Max(0, size.Width - viewport.Width + Math.Min(ObscuredRight, viewport.Width))),
            Math.Clamp(Offset.Y, 0, Math.Max(0, size.Height - viewport.Height)));
        if (clamped == Offset) return;
        _clamping = true;
        SetCurrentValue(OffsetProperty, clamped);
        _clamping = false;
    }

    public void Reveal(int index, double obscuredRight = 0)
    {
        if (Layout is null || index < 0 || index >= Layout.Cells.Count) return;
        var rect = Layout.CardRect(index, Metrics).Inflate(24);
        var viewport = ContentViewport;
        double width = Math.Max(1, viewport.Width - obscuredRight);
        double x = Offset.X, y = Offset.Y;
        if (rect.Right > x + width) x = rect.Right - width; else if (rect.Left < x) x = rect.Left;
        if (rect.Top < y) y = rect.Top; else if (rect.Bottom > y + viewport.Height) y = rect.Bottom - viewport.Height;
        GlideTo(new Vector(x, y));
    }
}
