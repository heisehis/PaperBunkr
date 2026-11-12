using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Controls.EventMap;

/// <summary>Compact map overview; pointer input moves the canvas viewport.</summary>
public sealed class EventMapOverviewStrip : Control
{
    public static readonly StyledProperty<EventMapCanvas?> CanvasProperty = AvaloniaProperty.Register<EventMapOverviewStrip, EventMapCanvas?>(nameof(Canvas));
    private bool _dragging;
    private Vector _grabOffset;

    public EventMapOverviewStrip()
    {
        Height = 32;
        Focusable = false;
        ClipToBounds = true;
        Reveal.SetIsOpen(this, false);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        ResourcesChanged += (_, _) => InvalidateVisual();
    }

    public EventMapCanvas? Canvas { get => GetValue(CanvasProperty); set => SetValue(CanvasProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CanvasProperty)
        {
            if (change.OldValue is EventMapCanvas old) old.ViewportChanged -= Update;
            if (Canvas is { } canvas) canvas.ViewportChanged += Update;
            Update();
        }
    }

    private void Update()
    {
        var canvas = Canvas;
        var layout = canvas?.Layout;
        if (canvas is null || layout is null || layout.IsEmpty || canvas.Bounds.Width <= 0 || canvas.Bounds.Height <= 0)
        { SetOpen(false); return; }
        var total = layout.TotalSizeFor(canvas.Metrics);
        double availableHeight = canvas.ContentViewport.Height + (IsVisible ? Bounds.Height : 0);
        SetOpen(total.Width > canvas.ContentViewport.Width + 0.5 || total.Height > availableHeight + 0.5);
        InvalidateVisual();
    }

    private void SetOpen(bool value)
    {
        if (Reveal.GetIsOpen(this) != value) Reveal.SetIsOpen(this, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Canvas is not { Layout: { } layout } canvas || layout.IsEmpty) return;
        var bg = new ImmutableSolidColorBrush(EventMapPalette.ResolveColor(this, "PbChromeColor"));
        var accent = new ImmutableSolidColorBrush(EventMapPalette.ResolveColor(this, "PbAccentColor"));
        context.FillRectangle(bg, new Rect(Bounds.Size));
        var wash = new ImmutableSolidColorBrush(EventMapPalette.ResolveColor(this, "PbAccentColor"), 0.08);
        int ordinal = 0;
        foreach (var block in layout.Blocks)
        {
            if (block.Kind is not (EventMapBlockKind.Event or EventMapBlockKind.Unplaced)) continue;
            var span = layout.BlockColumns(block);
            if (ordinal++ % 2 == 1 && layout.ColumnCount > 0)
                context.FillRectangle(wash, new Rect(span.First * Bounds.Width / layout.ColumnCount, 4,
                    (span.Last - span.First + 1) * Bounds.Width / layout.ColumnCount, Math.Max(0, Bounds.Height - 8)));
        }
        foreach (var cell in layout.Cells)
        {
            var tick = EventMapOverviewMapping.TickRect(cell, layout, Bounds.Size);
            var color = new ImmutableSolidColorBrush(EventMapPalette.ResolveColor(this, cell.ColorIndexOverride ?? layout.Tracks[cell.Track].ColorIndex));
            bool read = canvas.Cards is { } cards && cell.Index < cards.Count && cards[cell.Index].IsRead;
            context.DrawRectangle(read ? color : null, new Pen(color, 1), tick);
            if (cell.Index == canvas.SelectedIndex)
                context.DrawRectangle(null, new Pen(accent, 1), new Rect(tick.X - 1.5, tick.Y, tick.Width + 3, tick.Height));
        }
        var box = EventMapOverviewMapping.ViewportBox(canvas.Offset, canvas.ContentViewport, layout, canvas.Metrics, Bounds.Size);
        context.DrawRectangle(null, new Pen(accent, 1.5), box);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        e.Pointer.Capture(this);
        var point = e.GetPosition(this);
        var canvas = Canvas;
        var box = canvas?.Layout is { } layout
            ? EventMapOverviewMapping.ViewportBox(canvas.Offset, canvas.ContentViewport, layout, canvas.Metrics, Bounds.Size) : default;
        _grabOffset = box.Contains(point) ? point - box.Center : default;
        MoveTo(point);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
        {
            if (Canvas?.Layout is { IsBlockMode: true } layout && Bounds.Width > 0)
            {
                int column = (int)(e.GetPosition(this).X / Bounds.Width * layout.ColumnCount);
                string? label = null;
                foreach (var block in layout.Blocks)
                {
                    var span = layout.BlockColumns(block);
                    if (column >= span.First && column <= span.Last) { label = block.Label; break; }
                }
                ToolTip.SetTip(this, label);
            }
            return;
        }
        MoveTo(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); _dragging = false; }

    private void MoveTo(Point point)
    {
        if (Canvas is not { Layout: { } layout } canvas) return;
        canvas.SetCurrentValue(EventMapCanvas.OffsetProperty,
            EventMapOverviewMapping.CenterOffsetFor(point - _grabOffset, canvas.ContentViewport, layout, canvas.Metrics, Bounds.Size));
    }
}
