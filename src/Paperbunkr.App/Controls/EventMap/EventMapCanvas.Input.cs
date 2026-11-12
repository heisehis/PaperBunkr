using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Paperbunkr.App.Services.EventMap;

namespace Paperbunkr.App.Controls.EventMap;

public sealed partial class EventMapCanvas
{
    private bool _panning;
    private Point _panStart;
    private Vector _panOffset;
    private double _lastPinchScale = 1;
    private int? _hoveredIndex;
    private int? _hoveredLaneIndex;
    private int _hoverGeneration;

    private void OnPinch(object? sender, PinchEventArgs e)
    {
        _panning = false;
        var origin = e.ScaleOrigin;
        if (e.Scale > 0 && _lastPinchScale > 0)
            ZoomAbout(new Point(origin.X - RailWidth, origin.Y - RulerHeight), e.Scale / _lastPinchScale);
        _lastPinchScale = e.Scale;
        e.Handled = true;
    }

    private void OnPinchEnded(object? sender, PinchEndedEventArgs e) => _lastPinchScale = 1;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Layout is null) return;
        var point = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        var hit = EventMapHitTest.At(point, Layout, Metrics, Offset, RailWidth, RulerHeight, SelectedIndex);
        if (properties.IsLeftButtonPressed && hit.Kind == EventMapHitKind.Empty && HandleBracketPointer(point, pressed: true))
        {
            e.Handled = true;
            return;
        }
        if (properties.IsMiddleButtonPressed || (properties.IsLeftButtonPressed && hit.Kind == EventMapHitKind.Empty))
        {
            ClearPeek();
            _glideGeneration++;
            _panning = true;
            _panStart = point;
            _panOffset = Offset;
            e.Pointer.Capture(this);
            Focus();
            e.Handled = true;
        }
        else if (properties.IsLeftButtonPressed && hit.Kind == EventMapHitKind.Card)
        {
            Focus();
            CardPressed?.Invoke(hit.Index, e.ClickCount);
            e.Handled = true;
        }
        else if (properties.IsLeftButtonPressed && hit.Kind == EventMapHitKind.Band)
        {
            BandPressed?.Invoke(hit.Index, e.ClickCount);
            e.Handled = true;
        }
        else if (properties.IsLeftButtonPressed && hit.Kind == EventMapHitKind.Lane && hit.Index < Layout.Tracks.Count && Layout.Tracks[hit.Index].IsFolded)
        {
            LanePressed?.Invoke(hit.Index);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_panning)
        {
            if (Layout is null) { ClearPeek(); return; }
            var point = e.GetPosition(this);
            var hit = EventMapHitTest.At(point, Layout, Metrics, Offset, RailWidth, RulerHeight, SelectedIndex);
            if (hit.Kind == EventMapHitKind.Empty) HandleBracketPointer(point, pressed: false);
            int? lane = hit.Kind == EventMapHitKind.Lane ? hit.Index : null;
            if (_hoveredLaneIndex != lane)
            {
                _hoveredLaneIndex = lane;
                if (lane is not null) AnimateHover(); else InvalidateVisual();
            }
            string? tip = hit.Kind == EventMapHitKind.Lane && LaneHeaders is { } headers && hit.Index < headers.Count
                ? headers[hit.Index].FullName : null;
            if (Layout.IsBlockMode && point.Y < RulerHeight && point.X >= RailWidth)
            {
                if (Connectors is { } connectors && point.Y >= 17 && point.Y <= 32)
                    for (int i = 0; i < connectors.Count; i++)
                    {
                        var connector = connectors[i];
                        if (connector.FromBlock < 0 || connector.ToBlock < 0 || connector.FromBlock >= Layout.Blocks.Count || connector.ToBlock >= Layout.Blocks.Count) continue;
                        var from = Layout.BlockColumns(Layout.Blocks[connector.FromBlock]);
                        var to = Layout.BlockColumns(Layout.Blocks[connector.ToBlock]);
                        double x1 = RailWidth + ((from.First + from.Last + 1) * Metrics.SlotWidth / 2) - Offset.X;
                        double x2 = RailWidth + ((to.First + to.Last + 1) * Metrics.SlotWidth / 2) - Offset.X;
                        double y = 25 + (i % 2 == 0 ? -2 : 3);
                        if (Math.Abs(point.Y - y) <= 4 && point.X >= Math.Min(x1, x2) - 4 && point.X <= Math.Max(x1, x2) + 4) { tip = connector.Tooltip; break; }
                    }
                if (tip is null && hit.Kind == EventMapHitKind.Band && hit.Index < Layout.Blocks.Count)
                {
                    var block = Layout.Blocks[hit.Index];
                    var members = Cards is null ? Array.Empty<EventMapCell>() : Layout.Cells.Where(c => c.Row.BlockIndex == block.Index && c.Index < Cards.Count).ToArray();
                    int read = Cards is null ? 0 : members.Count(c => Cards[c.Index].IsRead);
                    tip = block.Label + (members.Length == 0 ? "" : $" · {100 * read / members.Length} % read");
                }
            }
            ToolTip.SetTip(this, tip);
            int? index = hit.Kind == EventMapHitKind.Card ? hit.Index : null;
            if (index == _hoveredIndex) return;
            ClearPeek();
            _hoveredIndex = index;
            if (index is not null) AnimateHover();
            int generation = _hoverGeneration;
            if (index is not null && EventMapCardLooks.For(Metrics.CardWidth) < EventMapCardLook.Standard) DispatcherTimer.RunOnce(() =>
            {
                if (generation == _hoverGeneration && _hoveredIndex == index) PeekChanged?.Invoke(index);
            }, TimeSpan.FromMilliseconds(400));
            return;
        }
        var delta = e.GetPosition(this) - _panStart;
        SetCurrentValue(OffsetProperty, _panOffset - delta);
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoveredLaneIndex = null;
        ClearPeek();
    }

    private void ClearPeek()
    {
        _hoverGeneration++;
        _hoveredIndex = null;
        InvalidateVisual();
        PeekChanged?.Invoke(null);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_panning) return;
        _panning = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _panning = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Layout is null || Layout.IsEmpty) return;
        ClearPeek();
        _glideGeneration++;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            SetCurrentValue(OffsetProperty, new Vector(Offset.X - (e.Delta.Y * 80), Offset.Y));
        else
            SetCurrentValue(OffsetProperty, new Vector(Offset.X - (e.Delta.X * 80), Offset.Y - (e.Delta.Y * 80)));
        e.Handled = true;
    }

    public void ZoomAbout(Point anchor, double factor)
    {
        if (Layout is null) return;
        double min = EventMapZoom.MinimumScaleForFit(EventMapZoom.FitScale(Layout.ColumnCount, Layout.TrackCount, Bounds.Size, RailWidth, RulerHeight));
        var result = EventMapZoom.ZoomAbout(Scale, Offset, anchor, factor, min);
        _zoomAnchoring = true;
        SetCurrentValue(ScaleProperty, result.Scale);
        SetCurrentValue(OffsetProperty, result.Offset);
        _zoomAnchoring = false;
    }
}
