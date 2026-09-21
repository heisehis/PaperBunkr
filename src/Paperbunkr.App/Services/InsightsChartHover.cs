using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Services;

/// <summary>
/// Pure hover descriptions for the Insights charts (docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #16): given the
/// data-space coordinates under the pointer, say what tooltip (if any) to show. Kept free of ScottPlot/Avalonia types so the
/// hit-testing is unit-tested; <c>InsightsScreen</c> converts pixels to coordinates and shows the returned text.
/// </summary>
public static class InsightsChartHover
{
    /// <summary>Half the drawn bar width (ScottPlot's default bar is 0.6 wide, so hovering the bar or its immediate gutter counts).</summary>
    private const double BarHalfWidth = 0.4;

    /// <summary>Tooltip for the bar under (<paramref name="x"/>, <paramref name="y"/>), or null when the pointer is between bars, left of the
    /// baseline, or above the bar. Bar <c>i</c> is centred on x = i.</summary>
    public static string? DescribeBar(double x, double y, IReadOnlyList<(string Label, int Value)> bars, string unit)
    {
        if (bars.Count == 0)
        {
            return null;
        }

        int index = (int)Math.Round(x);
        if (index < 0 || index >= bars.Count || Math.Abs(x - index) > BarHalfWidth)
        {
            return null;
        }

        var (label, value) = bars[index];
        if (y < 0 || y > Math.Max(value, 1))
        {
            return null;
        }

        return $"{label}: {value:N0} {unit}";
    }

    /// <summary>Tooltip for the month column nearest <paramref name="x"/> on the cumulative growth chart: the month plus every series' running
    /// total there (largest first), or null outside the plotted range.</summary>
    public static string? DescribeGrowth(double x, IReadOnlyList<string> monthLabels, IReadOnlyList<(string Name, double[] Values)> series)
    {
        if (monthLabels.Count == 0 || series.Count == 0)
        {
            return null;
        }

        int index = (int)Math.Round(x);
        if (index < 0 || index >= monthLabels.Count || Math.Abs(x - index) > 0.5)
        {
            return null;
        }

        var parts = series
            .Where(s => index < s.Values.Length)
            .OrderByDescending(s => s.Values[index])
            .Select(s => series.Count == 1 ? $"{s.Values[index]:N0}" : $"{s.Name} {s.Values[index]:N0}");
        return $"{monthLabels[index]} · {string.Join(" · ", parts)}";
    }
}
