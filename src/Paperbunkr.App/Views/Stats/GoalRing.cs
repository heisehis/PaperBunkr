using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Paperbunkr.App.Views.Stats;

/// <summary>
/// Small progress ring for the Insights Overview tab's reading-goal cards (docs/superpowers/specs/2026-09-23-
/// insights-reading-goals-design.md). Same hand-rolled-control shape as <see cref="TrendSparkline"/>/
/// <see cref="ActivityHeatmap"/> in this folder: styled properties drawn via <see cref="DrawingContext"/>,
/// brush resolved from the app's own skin resources.
/// </summary>
public sealed class GoalRing : Control
{
    public static readonly StyledProperty<double> PercentProperty =
        AvaloniaProperty.Register<GoalRing, double>(nameof(Percent));

    public static readonly StyledProperty<bool> IsCompleteProperty =
        AvaloniaProperty.Register<GoalRing, bool>(nameof(IsComplete));

    static GoalRing()
    {
        AffectsRender<GoalRing>(PercentProperty, IsCompleteProperty);
    }

    public double Percent { get => GetValue(PercentProperty); set => SetValue(PercentProperty, value); }

    public bool IsComplete { get => GetValue(IsCompleteProperty); set => SetValue(IsCompleteProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(40, 40);

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        const double strokeWidth = 4;
        double radius = (Math.Min(Bounds.Width, Bounds.Height) - strokeWidth) / 2;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);

        var trackBrush = ResolveBrush("PbBorderBrush", Color.FromArgb(0x40, 0xff, 0xff, 0xff));
        context.DrawEllipse(null, new Pen(trackBrush, strokeWidth), center, radius, radius);

        if (IsComplete)
        {
            var successBrush = ResolveBrush("PbSuccessBrush", Color.FromRgb(0x4a, 0xde, 0x80));
            context.DrawEllipse(null, new Pen(successBrush, strokeWidth), center, radius, radius);
            return;
        }

        double clamped = Math.Clamp(Percent, 0, 100);
        if (clamped <= 0)
        {
            return; // nothing to sweep yet - the track alone is the whole picture
        }

        double sweepAngle = clamped / 100.0 * 360.0;
        Point PointOnCircle(double degrees)
        {
            double radians = (degrees - 90) * Math.PI / 180.0; // start at 12 o'clock
            return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        }

        var start = PointOnCircle(0);
        var end = PointOnCircle(sweepAngle);
        bool isLargeArc = clamped > 50;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, isFilled: false);
            ctx.ArcTo(end, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise);
            ctx.EndFigure(isClosed: false);
        }

        var accentBrush = ResolveBrush("PbAccentBrush", Color.FromRgb(0x5b, 0x8d, 0xef));
        context.DrawGeometry(null, new Pen(accentBrush, strokeWidth, lineCap: PenLineCap.Round), geometry);
    }

    private IBrush ResolveBrush(string key, Color fallback)
        => this.TryFindResource(key, out object? value) && value is IBrush brush ? brush : new SolidColorBrush(fallback);
}
