using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Paperbunkr.App.Views.Insights;

/// <summary>
/// Hand-rolled completion donut for the Insights dashboard (docs/superpowers/specs/
/// 2026-09-05-insights-dashboard-design.md §9.2) - three arcs (read / in-progress / unread) drawn
/// straight from the active skin's brushes, so it re-themes for free on a skin change (unlike the
/// ScottPlot bar charts). A dependency's pie output wouldn't match the app's visual language and
/// would need the same theming bridge anyway.
/// </summary>
public sealed class CompletionDonut : Control
{
    public static readonly StyledProperty<int> ReadCountProperty =
        AvaloniaProperty.Register<CompletionDonut, int>(nameof(ReadCount));

    public static readonly StyledProperty<int> InProgressCountProperty =
        AvaloniaProperty.Register<CompletionDonut, int>(nameof(InProgressCount));

    public static readonly StyledProperty<int> UnreadCountProperty =
        AvaloniaProperty.Register<CompletionDonut, int>(nameof(UnreadCount));

    static CompletionDonut()
    {
        AffectsRender<CompletionDonut>(ReadCountProperty, InProgressCountProperty, UnreadCountProperty);
        AffectsMeasure<CompletionDonut>(ReadCountProperty, InProgressCountProperty, UnreadCountProperty);
    }

    public int ReadCount { get => GetValue(ReadCountProperty); set => SetValue(ReadCountProperty, value); }

    public int InProgressCount { get => GetValue(InProgressCountProperty); set => SetValue(InProgressCountProperty, value); }

    public int UnreadCount { get => GetValue(UnreadCountProperty); set => SetValue(UnreadCountProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        double side = Math.Min(
            double.IsInfinity(availableSize.Width) ? 140 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 140 : availableSize.Height);
        return new Size(side, side);
    }

    public override void Render(DrawingContext context)
    {
        double total = ReadCount + InProgressCount + UnreadCount;
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double outer = size / 2;
        double thickness = Math.Max(8, outer * 0.28);
        double radius = outer - thickness / 2;

        var track = ResolveBrush("PbSurface2Brush", Color.FromRgb(0x33, 0x35, 0x3d));
        context.DrawEllipse(null, new Pen(track, thickness), center, radius, radius);

        if (total <= 0)
        {
            return;
        }

        var segments = new (double Value, IBrush Brush)[]
        {
            (ReadCount, ResolveBrush("PbAccentBrush", Color.FromRgb(0x5b, 0x8d, 0xef))),
            (InProgressCount, ResolveBrush("PbSuccessBrush", Color.FromRgb(0x7e, 0xe7, 0x87))),
            (UnreadCount, ResolveBrush("PbTextFaintBrush", Color.FromRgb(0x6b, 0x6f, 0x7a))),
        };

        // Give every non-zero segment a visible minimum sweep (~3° each) so a handful-of-thousands
        // "read" slice doesn't vanish, then take the padding back off the largest segment.
        const double minSweep = Math.PI / 60;
        int nonZero = segments.Count(s => s.Value > 0);
        double reservedForMinimums = nonZero * minSweep;
        double scale = (Math.PI * 2 - reservedForMinimums) / total;
        int largestIndex = 0;
        for (int i = 1; i < segments.Length; i++)
        {
            if (segments[i].Value > segments[largestIndex].Value)
            {
                largestIndex = i;
            }
        }

        double startAngle = -Math.PI / 2;
        for (int i = 0; i < segments.Length; i++)
        {
            var (value, brush) = segments[i];
            if (value <= 0)
            {
                continue;
            }

            double sweep = (value * scale) + minSweep;
            if (i == largestIndex)
            {
                sweep -= reservedForMinimums; // absorb the total padding here so the ring still closes
            }

            double endAngle = startAngle + sweep;

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                var p0 = PointOnCircle(center, radius, startAngle);
                var p1 = PointOnCircle(center, radius, endAngle);
                ctx.BeginFigure(p0, false);
                ctx.ArcTo(p1, new Size(radius, radius), 0, sweep > Math.PI, SweepDirection.Clockwise);
                ctx.EndFigure(false);
            }

            context.DrawGeometry(null, new Pen(brush, thickness) { LineCap = PenLineCap.Round }, geometry);
            startAngle = endAngle;
        }

        if (total > 0)
        {
            // Lead with the count, not a percentage - "0%" for 1-of-2406 reads as broken.
            double pct = ReadCount / total * 100.0;
            string caption = pct >= 1 ? $"{pct:0}% read" : "read";
            var big = new FormattedText(
                ReadCount.ToString("N0"),
                System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default,
                Math.Max(13, outer * 0.34), ResolveBrush("PbTextBrush", Colors.White));
            var small = new FormattedText(
                caption,
                System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default,
                Math.Max(9, outer * 0.15), ResolveBrush("PbTextMutedBrush", Color.FromRgb(0x8b, 0x8f, 0x9a)));
            double blockHeight = big.Height + small.Height;
            context.DrawText(big, new Point(center.X - big.Width / 2, center.Y - blockHeight / 2));
            context.DrawText(small, new Point(center.X - small.Width / 2, center.Y - blockHeight / 2 + big.Height));
        }
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
        => new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));

    private IBrush ResolveBrush(string key, Color fallback)
        => this.TryFindResource(key, out object? value) && value is IBrush brush ? brush : new SolidColorBrush(fallback);
}
