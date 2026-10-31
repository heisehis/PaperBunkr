using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Services.Books;

/// <summary>Widths for the chapter ribbon (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §2).</summary>
public static class RibbonMath
{
    /// <summary>The smallest a segment may be, as a share of the average segment, so a one-page interlude stays visible and clickable.</summary>
    public const double MinShareOfAverage = 0.35;

    /// <summary>Weights proportional to <paramref name="lengths"/>, floored at <see cref="MinShareOfAverage"/> of the average. Unknown or zero
    /// lengths (all of them, or a PDF) give equal weights.</summary>
    public static IReadOnlyList<double> Weights(IReadOnlyList<double> lengths)
    {
        if (lengths.Count == 0)
        {
            return Array.Empty<double>();
        }

        double total = lengths.Where(l => l > 0 && double.IsFinite(l)).Sum();
        if (total <= 0)
        {
            return Enumerable.Repeat(1d, lengths.Count).ToArray();
        }

        double floor = total / lengths.Count * MinShareOfAverage;
        return lengths.Select(l => Math.Max(double.IsFinite(l) && l > 0 ? l : 0, floor)).ToArray();
    }

    /// <summary>Pixel widths for weights sharing <paramref name="width"/> with <paramref name="gap"/> between neighbours.</summary>
    public static IReadOnlyList<double> Widths(IReadOnlyList<double> weights, double width, double gap)
    {
        if (weights.Count == 0)
        {
            return Array.Empty<double>();
        }

        double usable = Math.Max(0, width - (gap * (weights.Count - 1)));
        double total = weights.Sum();
        return total <= 0
            ? Enumerable.Repeat(usable / weights.Count, weights.Count).ToArray()
            : weights.Select(w => usable * w / total).ToArray();
    }
}
