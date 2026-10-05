using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Paperbunkr.Data.Metadata;

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

    /// <summary>Active (accent, or amber when <see cref="IsBehind"/>), Completed (success ring + check) or Missed (danger arc at the
    /// reached percentage + cross) - docs/superpowers/specs/2026-10-04-insights-goal-outcomes-and-chart-colour-design.md.</summary>
    public static readonly StyledProperty<GoalOutcome> OutcomeProperty =
        AvaloniaProperty.Register<GoalRing, GoalOutcome>(nameof(Outcome));

    public static readonly StyledProperty<bool> IsBehindProperty =
        AvaloniaProperty.Register<GoalRing, bool>(nameof(IsBehind));

    static GoalRing()
    {
        AffectsRender<GoalRing>(PercentProperty, OutcomeProperty, IsBehindProperty);
    }

    public double Percent { get => GetValue(PercentProperty); set => SetValue(PercentProperty, value); }

    public GoalOutcome Outcome { get => GetValue(OutcomeProperty); set => SetValue(OutcomeProperty, value); }

    public bool IsBehind { get => GetValue(IsBehindProperty); set => SetValue(IsBehindProperty, value); }

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

        if (Outcome == GoalOutcome.Completed)
        {
            var successBrush = ResolveBrush("PbSuccessBrush", Color.FromRgb(0x4a, 0xde, 0x80));
            context.DrawEllipse(null, new Pen(successBrush, strokeWidth), center, radius, radius);
            DrawMark(context, center, radius, successBrush, check: true);
            return;
        }

        bool missed = Outcome == GoalOutcome.Missed;
        var arcBrush = missed
            ? ResolveBrush("PbDangerBrush", Color.FromRgb(0xd9, 0x6c, 0x6c))
            : IsBehind
                ? ResolveBrush("PbBadgeBrush", Color.FromRgb(0xd7, 0xac, 0x4c))
                : ResolveBrush("PbAccentBrush", Color.FromRgb(0x5b, 0x8d, 0xef));

        if (missed)
        {
            DrawMark(context, center, radius, arcBrush, check: false);
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

        context.DrawGeometry(null, new Pen(arcBrush, strokeWidth, lineCap: PenLineCap.Round), geometry);
    }

    /// <summary>A check (completed) or cross (missed) drawn inside the ring, so the outcome never depends on colour alone.</summary>
    private static void DrawMark(DrawingContext context, Point center, double radius, IBrush brush, bool check)
    {
        var pen = new Pen(brush, 2.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        double r = radius * 0.42;
        if (check)
        {
            context.DrawLine(pen, new Point(center.X - r, center.Y + (r * 0.05)), new Point(center.X - (r * 0.25), center.Y + (r * 0.7)));
            context.DrawLine(pen, new Point(center.X - (r * 0.25), center.Y + (r * 0.7)), new Point(center.X + r, center.Y - (r * 0.6)));
        }
        else
        {
            context.DrawLine(pen, new Point(center.X - (r * 0.7), center.Y - (r * 0.7)), new Point(center.X + (r * 0.7), center.Y + (r * 0.7)));
            context.DrawLine(pen, new Point(center.X + (r * 0.7), center.Y - (r * 0.7)), new Point(center.X - (r * 0.7), center.Y + (r * 0.7)));
        }
    }

    private IBrush ResolveBrush(string key, Color fallback)
        => this.TryFindResource(key, out object? value) && value is IBrush brush ? brush : new SolidColorBrush(fallback);
}
