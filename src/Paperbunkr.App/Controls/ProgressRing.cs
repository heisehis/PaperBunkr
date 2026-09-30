using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Paperbunkr.App.Controls;

/// <summary>
/// A small determinate progress ring (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md §3): a full track circle and an
/// arc from 12 o'clock clockwise for <see cref="Value"/> (0..1). Drawn, not templated - the reading-list cover rail shows one per list.
/// Brushes come from the caller (skin resources), never literals.
/// </summary>
public sealed class ProgressRing : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(Value));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(StrokeThickness), 2.5);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(Fill));

    static ProgressRing()
    {
        AffectsRender<ProgressRing>(ValueProperty, StrokeThicknessProperty, ForegroundProperty, TrackBrushProperty, FillProperty);
    }

    /// <summary>0..1.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>Optional disc behind the ring (so it reads on top of a cover).</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double thickness = Math.Min(StrokeThickness, size / 2);
        double radius = (size - thickness) / 2;

        if (Fill is { } fill)
        {
            context.DrawEllipse(fill, null, center, size / 2, size / 2);
        }

        if (TrackBrush is { } track)
        {
            context.DrawEllipse(null, new Pen(track, thickness), center, radius, radius);
        }

        double value = Math.Clamp(Value, 0, 1);
        if (value <= 0 || Foreground is not { } brush)
        {
            return;
        }

        var pen = new Pen(brush, thickness, lineCap: PenLineCap.Round);
        if (value >= 0.999)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        double angle = value * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + (radius * Math.Sin(angle)), center.Y - (radius * Math.Cos(angle)));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, value > 0.5, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }
}
