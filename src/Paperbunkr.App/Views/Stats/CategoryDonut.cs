using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Paperbunkr.App.Services;
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
    public static readonly StyledProperty<IReadOnlyList<CompositionSlice>> SlicesProperty =
        AvaloniaProperty.Register<CategoryDonut, IReadOnlyList<CompositionSlice>>(nameof(Slices), Array.Empty<CompositionSlice>());

    /// <summary>0..1 fraction of the ring drawn - animated from 0 to 1 whenever <see cref="Slices"/> changes (docs/superpowers/specs/2026-09-21-
    /// cosmetics-pitch-2-design.md #16). 1 (fully drawn) when motion is off, so a static frame is always the complete donut.</summary>
    public static readonly StyledProperty<double> SweepProgressProperty =
        AvaloniaProperty.Register<CategoryDonut, double>(nameof(SweepProgress), 1d);

    /// <summary>What the categories mean, which decides their colours (<see cref="InsightsChartPalette"/>).</summary>
    public static readonly StyledProperty<ChartCategoryKind> KindProperty =
        AvaloniaProperty.Register<CategoryDonut, ChartCategoryKind>(nameof(Kind));

    /// <summary>How many slices are drawn before the smallest fold into a neutral "Other".</summary>
    public static readonly StyledProperty<int> MaxSlicesProperty =
        AvaloniaProperty.Register<CategoryDonut, int>(nameof(MaxSlices), 6);

    /// <summary>The slice under the pointer (or being pointed at in the paired <see cref="DonutLegend"/>), -1 for none. Two-way, so
    /// binding it to the legend's property links hover between the two.</summary>
    public static readonly StyledProperty<int> HoveredIndexProperty =
        AvaloniaProperty.Register<CategoryDonut, int>(nameof(HoveredIndex), -1, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private int _sweepGeneration;

    static CategoryDonut()
    {
        AffectsRender<CategoryDonut>(SlicesProperty, SweepProgressProperty, KindProperty, MaxSlicesProperty, HoveredIndexProperty);
        AffectsMeasure<CategoryDonut>(SlicesProperty);
    }

    public ChartCategoryKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    public int MaxSlices { get => GetValue(MaxSlicesProperty); set => SetValue(MaxSlicesProperty, value); }

    public int HoveredIndex { get => GetValue(HoveredIndexProperty); set => SetValue(HoveredIndexProperty, value); }

    /// <summary>The ordered, capped, coloured slices exactly as drawn - the legend builds from the same call.</summary>
    internal IReadOnlyList<PaletteSlice> Palette => InsightsChartPalette.Layout(Slices, Kind, MaxSlices);

    private (Point Center, double Outer, double Thickness) Geometry()
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        double outer = size / 2;
        return (new Point(Bounds.Width / 2, Bounds.Height / 2), outer, Math.Max(8, outer * 0.28));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var palette = Palette;
        var (center, outer, thickness) = Geometry();
        int hit = DonutMath.HitTest(DonutMath.Sweeps(palette.Select(s => s.Count).ToList()), center, outer, thickness, e.GetPosition(this));
        HoveredIndex = hit;
        if (hit < 0)
        {
            ToolTip.SetIsOpen(this, false);
            return;
        }

        double total = palette.Sum(s => (double)s.Count);
        ToolTip.SetTip(this, $"{palette[hit].Label} · {palette[hit].Count:N0} · {palette[hit].Count / total:P1}");
        ToolTip.SetPlacement(this, PlacementMode.Pointer);
        ToolTip.SetIsOpen(this, true);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        HoveredIndex = -1;
        ToolTip.SetIsOpen(this, false);
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
        // Ordered, capped (the smallest tail folds into a neutral "Other" rather than wrapping a colour onto a second
        // category - found building the Content rating donut, 2026-09-24) and coloured by meaning where a label has
        // one: docs/superpowers/specs/2026-10-04-insights-goal-outcomes-and-chart-colour-design.md.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size)); // a hit surface for the slice hover
        var slices = Palette;
        double total = slices.Sum(s => (double)s.Count);
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
        {
            return;
        }

        var (center, outer, thickness) = Geometry();
        double radius = outer - thickness / 2;

        var track = ResolveBrush("PbSurface2Brush", Color.FromRgb(0x33, 0x35, 0x3d));
        context.DrawEllipse(null, new Pen(track, thickness), center, radius, radius);

        if (total <= 0 || slices.Count == 0)
        {
            return;
        }

        // Every non-zero segment gets a visible minimum sweep so a tiny slice among a huge one
        // doesn't vanish (DonutMath, shared with the hover hit-test). Segments are drawn with flat caps and a hairline
        // gap between them rather than round caps that overlap their neighbours: overlapping translucent caps (a dimmed
        // or shaded slice) would show as dark seams, and the gap keeps two slices of one colour tellable apart.
        var sweeps = DonutMath.Sweeps(slices.Select(s => s.Count).ToList());
        int hovered = HoveredIndex >= 0 && HoveredIndex < slices.Count ? HoveredIndex : -1;

        double startAngle = ArcGeometry.TopAngle;
        for (int i = 0; i < slices.Count; i++)
        {
            // Hovering one slice (or its legend row) dims the others so it reads as the subject.
            var brush = PaletteBrushes.Resolve(this, slices[i], hovered >= 0 && hovered != i ? 0.35 : 1);
            double sweep = sweeps[i] * SweepProgress;
            if (sweep <= 0.0001)
            {
                startAngle += sweep;
                continue;
            }

            double endAngle = startAngle + sweep;

            // Trim half the gap off each end (never more than a fifth of the slice, and always leaving a one-slice ring open
            // so its start and end points differ - an arc that ends where it began draws nothing).
            double gap = slices.Count == 1 ? 0.02 : Math.Min(0.02, sweep * 0.2);
            var geometry = ArcGeometry.CreateArc(center, radius, startAngle + (gap / 2), Math.Max(0.0001, sweep - gap));

            context.DrawGeometry(null, new Pen(brush, thickness) { LineCap = PenLineCap.Flat }, geometry);
            startAngle = endAngle;
        }

        // The centre reads the hovered slice's count and label, otherwise the total.
        var big = new FormattedText(
            (hovered >= 0 ? slices[hovered].Count : (int)total).ToString("N0"),
            System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default,
            Math.Max(13, outer * 0.34), ResolveBrush("PbTextBrush", Colors.White));
        var small = new FormattedText(
            hovered >= 0 ? slices[hovered].Label : slices.Count == 1 ? slices[0].Label : $"{slices.Count} categories",
            System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default,
            Math.Max(9, outer * 0.15), ResolveBrush("PbTextMutedBrush", Color.FromRgb(0x8b, 0x8f, 0x9a)));
        double blockHeight = big.Height + small.Height;
        context.DrawText(big, new Point(center.X - big.Width / 2, center.Y - blockHeight / 2));
        context.DrawText(small, new Point(center.X - small.Width / 2, center.Y - blockHeight / 2 + big.Height));
    }

    private IBrush ResolveBrush(string key, Color fallback)
        => this.TryFindResource(key, out object? value) && value is IBrush brush ? brush : new SolidColorBrush(fallback);
}
