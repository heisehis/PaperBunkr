using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Views.Stats;

/// <summary>Resolves a <see cref="PaletteSlice"/> to the skin's brush for its colour token, so a donut slice, its legend row and a bar all use one resolution path.</summary>
internal static class PaletteBrushes
{
    public static IBrush Resolve(Control control, PaletteSlice slice, double dim = 1)
    {
        Color color;
        if (control.TryFindResource(slice.ColorKey, out object? c) && c is Color resolved)
        {
            color = resolved;
        }
        else if (control.TryFindResource(slice.BrushKey, out object? b) && b is ISolidColorBrush solid)
        {
            color = solid.Color;
        }
        else
        {
            color = Colors.Gray;
        }

        return new SolidColorBrush(color, slice.Opacity * dim);
    }
}

/// <summary>
/// The legend beside a <see cref="CategoryDonut"/> (docs/superpowers/specs/2026-10-04-insights-goal-outcomes-and-chart-colour-design.md,
/// "Legends"): one row per drawn slice with a colour swatch, label, count and percent - optionally a proportional bar - built from the
/// <em>same</em> <see cref="InsightsChartPalette.Layout"/> call as the donut, so a slice and its row cannot disagree on colour or on the
/// "Other" fold. <see cref="HoveredIndex"/> binds two-way to the donut's, so pointing at either highlights both. Rows are not focusable:
/// the legend is a hover aid, not something the keyboard needs.
/// </summary>
public sealed class DonutLegend : Control
{
    private const double RowHeight = 22;
    private const double FontSize = 12;

    public static readonly StyledProperty<IReadOnlyList<CompositionSlice>> SlicesProperty =
        AvaloniaProperty.Register<DonutLegend, IReadOnlyList<CompositionSlice>>(nameof(Slices), Array.Empty<CompositionSlice>());

    public static readonly StyledProperty<ChartCategoryKind> KindProperty =
        AvaloniaProperty.Register<DonutLegend, ChartCategoryKind>(nameof(Kind));

    public static readonly StyledProperty<int> MaxSlicesProperty =
        AvaloniaProperty.Register<DonutLegend, int>(nameof(MaxSlices), 6);

    public static readonly StyledProperty<int> HoveredIndexProperty =
        AvaloniaProperty.Register<DonutLegend, int>(nameof(HoveredIndex), -1, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>Draws a proportional bar in each row's colour between the label and the count.</summary>
    public static readonly StyledProperty<bool> ShowBarsProperty =
        AvaloniaProperty.Register<DonutLegend, bool>(nameof(ShowBars));

    static DonutLegend()
    {
        AffectsRender<DonutLegend>(SlicesProperty, KindProperty, MaxSlicesProperty, HoveredIndexProperty, ShowBarsProperty);
        AffectsMeasure<DonutLegend>(SlicesProperty, KindProperty, MaxSlicesProperty);
    }

    public IReadOnlyList<CompositionSlice> Slices { get => GetValue(SlicesProperty); set => SetValue(SlicesProperty, value); }

    public ChartCategoryKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    public int MaxSlices { get => GetValue(MaxSlicesProperty); set => SetValue(MaxSlicesProperty, value); }

    public int HoveredIndex { get => GetValue(HoveredIndexProperty); set => SetValue(HoveredIndexProperty, value); }

    public bool ShowBars { get => GetValue(ShowBarsProperty); set => SetValue(ShowBarsProperty, value); }

    internal IReadOnlyList<PaletteSlice> Palette => InsightsChartPalette.Layout(Slices, Kind, MaxSlices);

    /// <summary>The row's text for the right-hand column: "2,177 · 54%".</summary>
    internal static string Describe(int count, double total)
    {
        if (total <= 0)
        {
            return count.ToString("N0");
        }

        double share = count / total;
        return share > 0 && share < 0.005 ? $"{count:N0} · <1%" : $"{count:N0} · {share:P0}";
    }

    protected override Size MeasureOverride(Size availableSize)
        => new(double.IsInfinity(availableSize.Width) ? 220 : availableSize.Width, Palette.Count * RowHeight);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int row = (int)(e.GetPosition(this).Y / RowHeight);
        HoveredIndex = row >= 0 && row < Palette.Count ? row : -1;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        HoveredIndex = -1;
    }

    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size)); // a hit surface for the row hover
        var palette = Palette;
        double total = palette.Sum(s => (double)s.Count);
        // Bars scale to the largest *known* category: an "Unknown" row of thousands would otherwise squash every real
        // rating to a sliver. Its own bar simply fills the track (the count beside it still says how many).
        int max = palette.Where(s => !s.IsNeutral).Select(s => s.Count).DefaultIfEmpty(palette.Count == 0 ? 0 : palette.Max(s => s.Count)).Max();
        int hovered = HoveredIndex >= 0 && HoveredIndex < palette.Count ? HoveredIndex : -1;

        var text = ResolveBrush("PbTextBrush", Colors.White);
        var muted = ResolveBrush("PbTextMutedBrush", Color.FromRgb(0x8b, 0x8f, 0x9a));
        var hoverFill = ResolveBrush("PbSurface2Brush", Color.FromRgb(0x33, 0x35, 0x3d));
        double width = Bounds.Width;
        double labelWidth = ShowBars ? 96 : 0;

        for (int i = 0; i < palette.Count; i++)
        {
            var slice = palette[i];
            double top = i * RowHeight;
            double dim = hovered >= 0 && hovered != i ? 0.45 : 1;
            var rowRect = new Rect(0, top, width, RowHeight);

            if (hovered == i)
            {
                context.DrawRectangle(hoverFill, null, rowRect, 4, 4);
            }

            // Swatch: squircle chip, never a pill (PbRadiusChip).
            var swatch = new Rect(6, top + ((RowHeight - 10) / 2), 10, 10);
            context.DrawRectangle(PaletteBrushes.Resolve(this, slice, dim), null, swatch, 3, 3);

            var right = Format(Describe(slice.Count, total), muted, dim);
            double rightX = Math.Max(0, width - right.Width - 6);
            double labelX = 24;
            double labelMax = ShowBars ? labelWidth : Math.Max(0, rightX - labelX - 8);
            var label = Format(slice.Label, text, dim, labelMax);
            context.DrawText(label, new Point(labelX, top + ((RowHeight - label.Height) / 2)));

            if (ShowBars)
            {
                double barX = labelX + labelWidth + 6;
                double barMax = Math.Max(0, rightX - barX - 8);
                double barWidth = max <= 0 ? 0 : Math.Min(barMax, Math.Max(3, barMax * slice.Count / max));
                var bar = new Rect(barX, top + ((RowHeight - 8) / 2), barWidth, 8);
                context.DrawRectangle(PaletteBrushes.Resolve(this, slice, dim), null, bar, 3, 3);
            }

            context.DrawText(right, new Point(rightX, top + ((RowHeight - right.Height) / 2)));
        }
    }

    private static FormattedText Format(string value, IBrush brush, double dim, double maxWidth = double.PositiveInfinity)
    {
        var shown = dim < 1 && brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, solid.Opacity * dim) : brush;
        var formatted = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, FontSize, shown);
        if (!double.IsInfinity(maxWidth))
        {
            formatted.MaxTextWidth = maxWidth;
            formatted.MaxLineCount = 1;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        return formatted;
    }

    private IBrush ResolveBrush(string key, Color fallback)
        => this.TryFindResource(key, out object? value) && value is IBrush brush ? brush : new SolidColorBrush(fallback);
}
