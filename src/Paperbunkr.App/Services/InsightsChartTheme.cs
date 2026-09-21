using System.Collections.Generic;
using Avalonia;
using Avalonia.Styling;
using ScottPlot;

namespace Paperbunkr.App.Services;

/// <summary>
/// Bridges the active skin's resource brushes into ScottPlot's imperative styling for the Insights
/// dashboard's two bar charts (docs/superpowers/specs/2026-09-05-insights-dashboard-design.md §9).
/// ScottPlot plots are drawn imperatively and don't restyle live on a skin change - the Insights
/// screen re-applies this every time it's navigated to, so a skin switch elsewhere is picked up on
/// the next visit. Falls back to a dark neutral palette when <see cref="Application.Current"/> has
/// no resources yet (headless tests, design-time).
/// </summary>
public static class InsightsChartTheme
{
    private static ScottPlot.Color Resolve(string colorKey, string fallbackHex)
    {
        if (Application.Current?.TryGetResource(colorKey, null, out object? value) == true
            && value is Avalonia.Media.Color c)
        {
            return new ScottPlot.Color(c.R, c.G, c.B, c.A);
        }

        return ScottPlot.Color.FromHex(fallbackHex);
    }

    public static ScottPlot.Color Text => Resolve("PbTextColor", "#c9ccd3");

    public static ScottPlot.Color Muted => Resolve("PbTextMutedColor", "#8b8f9a");

    public static ScottPlot.Color Accent => Visible(Resolve("PbAccentColor", "#5b8def"));

    public static ScottPlot.Color Badge => Visible(Resolve("PbBadgeColor", "#d7ac4c"));

    public static ScottPlot.Color Success => Visible(Resolve("PbSuccessColor", "#5fa889"));

    public static ScottPlot.Color Danger => Visible(Resolve("PbDangerColor", "#d96c6c"));

    public static ScottPlot.Color Blue => Visible(Resolve("PbChartBlueColor", "#5b8dbe"));

    public static ScottPlot.Color Violet => Visible(Resolve("PbChartVioletColor", "#9b7ebd"));

    /// <summary>WCAG 1.4.11's minimum contrast for graphical objects against their background (docs/superpowers/specs/2026-09-21-cosmetics-
    /// pitch-2-design.md #16).</summary>
    public const double MinGraphicContrast = 3.0;

    /// <summary>The chart colour, lightened or darkened only if it falls below <see cref="MinGraphicContrast"/> against the skin's background -
    /// so every categorical colour stays legible in both light and dark skins. A skin whose colours already pass is untouched.</summary>
    public static ScottPlot.Color Visible(ScottPlot.Color color)
    {
        var bg = Application.Current?.TryGetResource("PbBgColor", null, out object? value) == true && value is Avalonia.Media.Color c
            ? c
            : Avalonia.Media.Color.FromRgb(0x14, 0x16, 0x1B);
        return EnsureContrast(color, bg);
    }

    /// <summary>Pure form of <see cref="Visible"/>, exposed for tests: <paramref name="color"/> nudged until it reaches <see cref="MinGraphicContrast"/> against <paramref name="background"/>.</summary>
    public static ScottPlot.Color EnsureContrast(ScottPlot.Color color, Avalonia.Media.Color background)
    {
        var fg = Avalonia.Media.Color.FromRgb(color.R, color.G, color.B);
        var adjusted = ThemeService.AdjustForContrast(fg, background, MinGraphicContrast);
        return adjusted == fg ? color : new ScottPlot.Color(adjusted.R, adjusted.G, adjusted.B, color.A);
    }

    public static ScottPlot.Color Grid => Resolve("PbBorderColor", "#33353d");

    /// <summary>
    /// Fixed-order categorical palette for any Stats chart needing more than one series/segment
    /// color (docs/superpowers/specs/2026-09-08-stats-v2-mangabaka-design.md §8) - a donut/bar
    /// assigns colors by list index, so the same category always gets the same slot across renders
    /// and app sessions rather than a color being randomly reassigned each time.
    /// </summary>
    public static IReadOnlyList<ScottPlot.Color> CategoricalPalette => new[] { Accent, Blue, Badge, Success, Violet, Danger };

    /// <summary>Applies figure/axis/grid colours and a transparent background to a plot. Call before adding data.</summary>
    public static void Apply(Plot plot)
    {
        plot.FigureBackground.Color = ScottPlot.Colors.Transparent;
        plot.DataBackground.Color = ScottPlot.Colors.Transparent;
        plot.Axes.Color(Muted);
        plot.Grid.MajorLineColor = Grid.WithAlpha(0.35);
        plot.Grid.IsBeneathPlottables = true;
    }
}
