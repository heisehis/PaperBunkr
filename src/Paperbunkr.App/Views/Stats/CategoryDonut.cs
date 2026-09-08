using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Views.Stats;

/// <summary>
/// Generalized N-category donut for the Stats screen (docs/superpowers/specs/2026-09-08-stats-v2-
/// mangabaka-design.md §6.5/§9) - replaces the old 3-segment <c>CompletionDonut</c>, used for both
/// the Reading State (7 slices) and Media Type (5 slices) breakdowns. Same arc-draw approach
/// (StreamGeometry/ArcTo, minimum-sweep-per-segment) as the control it generalizes; colors come from
/// the same 6 resource keys <see cref="Services.InsightsChartTheme.CategoricalPalette"/> uses for
/// ScottPlot charts, resolved here as Avalonia brushes directly since this control draws with
/// <see cref="DrawingContext"/>, not ScottPlot.
/// </summary>
public sealed class CategoryDonut : Control
{
    private static readonly string[] CategoricalBrushKeys =
    {
        "PbAccentBrush", "PbChartBlueBrush", "PbBadgeBrush", "PbSuccessBrush", "PbChartVioletBrush", "PbDangerBrush",
    };

    public static readonly StyledProperty<IReadOnlyList<CompositionSlice>> SlicesProperty =
        AvaloniaProperty.Register<CategoryDonut, IReadOnlyList<CompositionSlice>>(nameof(Slices), Array.Empty<CompositionSlice>());

    static CategoryDonut()
    {
        AffectsRender<CategoryDonut>(SlicesProperty);
        AffectsMeasure<CategoryDonut>(SlicesProperty);
    }

    public IReadOnlyList<CompositionSlice> Slices { get => GetValue(SlicesProperty); set => SetValue(SlicesProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        double side = Math.Min(
            double.IsInfinity(availableSize.Width) ? 140 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 140 : availableSize.Height);
        return new Size(side, side);
    }

    public override void Render(DrawingContext context)
    {
        var slices = Slices.Where(s => s.Count > 0).ToList();
        double total = slices.Sum(s => (double)s.Count);
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

        if (total <= 0 || slices.Count == 0)
        {
            return;
        }

        // Every non-zero segment gets a visible minimum sweep so a tiny slice among a huge one
        // doesn't vanish, then the padding is taken back off the largest segment so the ring closes.
        const double minSweep = Math.PI / 60;
        double reservedForMinimums = slices.Count * minSweep;
        double scale = Math.Max(0, (Math.PI * 2 - reservedForMinimums)) / total;
        int largestIndex = 0;
        for (int i = 1; i < slices.Count; i++)
        {
            if (slices[i].Count > slices[largestIndex].Count)
            {
                largestIndex = i;
            }
        }

        double startAngle = -Math.PI / 2;
        for (int i = 0; i < slices.Count; i++)
        {
            var brush = ResolveBrush(CategoricalBrushKeys[i % CategoricalBrushKeys.Length], Colors.Gray);
            double sweep = (slices[i].Count * scale) + minSweep;
            if (i == largestIndex)
            {
                sweep -= reservedForMinimums;
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

        var big = new FormattedText(
            ((int)total).ToString("N0"),
            System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default,
            Math.Max(13, outer * 0.34), ResolveBrush("PbTextBrush", Colors.White));
        var small = new FormattedText(
            slices.Count == 1 ? slices[0].Label : $"{slices.Count} categories",
            System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default,
            Math.Max(9, outer * 0.15), ResolveBrush("PbTextMutedBrush", Color.FromRgb(0x8b, 0x8f, 0x9a)));
        double blockHeight = big.Height + small.Height;
        context.DrawText(big, new Point(center.X - big.Width / 2, center.Y - blockHeight / 2));
        context.DrawText(small, new Point(center.X - small.Width / 2, center.Y - blockHeight / 2 + big.Height));
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
        => new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));

    private IBrush ResolveBrush(string key, Color fallback)
        => this.TryFindResource(key, out object? value) && value is IBrush brush ? brush : new SolidColorBrush(fallback);
}
