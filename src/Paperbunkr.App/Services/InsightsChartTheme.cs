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

    public static ScottPlot.Color Accent => Resolve("PbAccentColor", "#5b8def");

    public static ScottPlot.Color Grid => Resolve("PbBorderColor", "#33353d");

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
