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

    /// <summary>0..1 fraction of the ring drawn - animated from 0 to 1 whenever <see cref="Slices"/> changes (docs/superpowers/specs/2026-09-21-
    /// cosmetics-pitch-2-design.md #16). 1 (fully drawn) when motion is off, so a static frame is always the complete donut.</summary>
    public static readonly StyledProperty<double> SweepProgressProperty =
        AvaloniaProperty.Register<CategoryDonut, double>(nameof(SweepProgress), 1d);

    private int _sweepGeneration;

    static CategoryDonut()
    {
        AffectsRender<CategoryDonut>(SlicesProperty, SweepProgressProperty);
        AffectsMeasure<CategoryDonut>(SlicesProperty);
    }

    public double SweepProgress { get => GetValue(SweepProgressProperty); set => SetValue(SweepProgressProperty, value); }

    /// <summary>The duration of the entry sweep: the app's large-motion token, which is <see cref="TimeSpan.Zero"/> under Reduced Motion (so the
    /// donut simply appears whole). Falls back to 0 when no resource is found (headless/design-time): no animation rather than a guessed one.</summary>
    internal TimeSpan EntranceDuration()
        => this.TryFindResource("PbMotionLarge", out object? value) && value is TimeSpan duration ? duration : TimeSpan.Zero;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SlicesProperty)
        {
            StartEntrance();
        }
    }

    private void StartEntrance()
    {
        int generation = ++_sweepGeneration;
        var duration = EntranceDuration();
        if (duration <= TimeSpan.Zero || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            SweepProgress = 1;
            return;
        }

        SweepProgress = 0;
        double startSeconds = double.NaN;

        void Frame(TimeSpan timestamp)
        {
            if (generation != _sweepGeneration)
            {
                return; // superseded by a newer data change (or detached)
            }

            if (double.IsNaN(startSeconds))
            {
                startSeconds = timestamp.TotalSeconds;
            }

            double t = Math.Clamp((timestamp.TotalSeconds - startSeconds) / duration.TotalSeconds, 0, 1);
            SweepProgress = 1 - Math.Pow(1 - t, 3); // ease-out cubic
            if (t < 1)
            {
                topLevel.RequestAnimationFrame(Frame);
            }
        }

        topLevel.RequestAnimationFrame(Frame);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _sweepGeneration++;
        SweepProgress = 1;
        base.OnDetachedFromVisualTree(e);
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

        double startAngle = ArcGeometry.TopAngle;
        for (int i = 0; i < slices.Count; i++)
        {
            var brush = ResolveBrush(CategoricalBrushKeys[i % CategoricalBrushKeys.Length], Colors.Gray);
            double sweep = (slices[i].Count * scale) + minSweep;
            if (i == largestIndex)
            {
                sweep -= reservedForMinimums;
            }

            sweep *= SweepProgress;
            if (sweep <= 0.0001)
            {
                startAngle += sweep;
                continue;
            }

            double endAngle = startAngle + sweep;

            var geometry = ArcGeometry.CreateArc(center, radius, startAngle, sweep);

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

    private IBrush ResolveBrush(string key, Color fallback)
        => this.TryFindResource(key, out object? value) && value is IBrush brush ? brush : new SolidColorBrush(fallback);
}
