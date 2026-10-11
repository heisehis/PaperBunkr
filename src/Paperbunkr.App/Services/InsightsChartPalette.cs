using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services;

/// <summary>What kind of category a chart is drawing, which decides whether a label carries a fixed meaning colour
/// (docs/superpowers/specs/2026-10-04-insights-goal-outcomes-and-chart-colour-design.md, "Shared colour map").</summary>
public enum ChartCategoryKind
{
    /// <summary>No inherent meaning - slices take the categorical palette by position.</summary>
    Positional,

    /// <summary>Reading status: Completed is green, Dropped red, and so on.</summary>
    ReadingState,

    /// <summary>Content rating, ordered from all-ages (green) to adults-only (red).</summary>
    ContentRating,
}

/// <summary>One drawn category: its label, count and the skin resources that colour it. <see cref="Base"/> is the
/// token stem ("Success", "ChartBlue", "TextMuted"); <see cref="BrushKey"/>/<see cref="ColorKey"/> are the matching
/// <c>Pb{Base}Brush</c>/<c>Pb{Base}Color</c> resources.</summary>
public sealed record PaletteSlice(string Label, int Count, string Base, bool IsNeutral, int Shade = 0)
{
    public string BrushKey => $"Pb{Base}Brush";

    public string ColorKey => $"Pb{Base}Color";

    /// <summary>1 for the first slice of a colour; lighter for each further slice that shares it (several content-rating tiers
    /// share a hue by design), so two neighbours of one tier stay tellable apart.</summary>
    public double Opacity => Math.Max(0.55, 1 - (0.15 * Shade));
}

/// <summary>
/// The one place that answers "what colour is this category?" for the Insights charts, so a donut slice, its legend
/// row and a matching bar can never disagree. Meaning colours come first (Completed = success), the positional
/// categorical palette fills in for everything without one, and "Unknown"/"Other"/"None" are always a neutral grey so
/// "no information" never reads as the headline.
/// </summary>
public static class InsightsChartPalette
{
    /// <summary>The same six stems <see cref="InsightsChartTheme.CategoricalPalette"/> uses, in the same order.</summary>
    public static readonly IReadOnlyList<string> CategoricalBases = new[] { "Accent", "ChartBlue", "Badge", "Success", "ChartViolet", "Danger" };

    public const string NeutralBase = "TextMuted";

    private static readonly HashSet<string> NeutralLabels = new(StringComparer.OrdinalIgnoreCase) { "unknown", "other", "none", "unrated", "not rated" };

    /// <summary>Orders, caps and colours <paramref name="slices"/>. Zero-count slices are dropped. When more than
    /// <paramref name="max"/> remain, the largest <c>max - 1</c> are kept and the rest fold into a neutral "Other"
    /// (never wrapping a colour onto two different categories).</summary>
    public static IReadOnlyList<PaletteSlice> Layout(IEnumerable<CompositionSlice> slices, ChartCategoryKind kind, int max = 6)
    {
        var live = slices.Where(s => s.Count > 0).ToList();
        if (live.Count > max)
        {
            var ordered = live.OrderByDescending(s => s.Count).ToList();
            var kept = ordered.Take(max - 1).ToList();
            kept.Add(new CompositionSlice("Other", ordered.Skip(max - 1).Sum(s => s.Count)));
            live = kept;
        }

        // First pass: semantic and neutral colours; second pass: positional fallback skipping stems already in use.
        var bases = new string?[live.Count];
        var used = new HashSet<string>();
        for (int i = 0; i < live.Count; i++)
        {
            if (IsNeutralLabel(live[i].Label))
            {
                bases[i] = NeutralBase;
            }
            else if (Semantic(kind, live[i].Label) is { } stem)
            {
                bases[i] = stem;
                used.Add(stem);
            }
        }

        int cursor = 0;
        for (int i = 0; i < live.Count; i++)
        {
            if (bases[i] is not null)
            {
                continue;
            }

            string? free = CategoricalBases.FirstOrDefault(b => !used.Contains(b));
            if (free is null)
            {
                free = CategoricalBases[cursor++ % CategoricalBases.Count]; // every stem taken: only now may one repeat
            }

            bases[i] = free;
            used.Add(free);
        }

        var seen = new Dictionary<string, int>();
        var result = new List<PaletteSlice>(live.Count);
        for (int i = 0; i < live.Count; i++)
        {
            string stem = bases[i]!;
            int shade = seen.GetValueOrDefault(stem);
            seen[stem] = shade + 1;
            result.Add(new PaletteSlice(live[i].Label, live[i].Count, stem, stem == NeutralBase, shade));
        }

        return result;
    }

    public static bool IsNeutralLabel(string label) => NeutralLabels.Contains(label.Trim());

    /// <summary>The meaning colour for a label of this kind, or null when it has none (positional fallback).</summary>
    public static string? Semantic(ChartCategoryKind kind, string label)
    {
        string key = new string(label.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return kind switch
        {
            ChartCategoryKind.ReadingState => key switch
            {
                "completed" or "finished" => "Success",
                "reading" => "Accent",
                "rereading" => "ChartViolet",
                "planned" => "ChartBlue",
                "paused" or "onhold" => "Badge",
                "dropped" => "Danger",
                _ => null,
            },
            ChartCategoryKind.ContentRating => ContentRatingStem(key),
            _ => null,
        };
    }

    // All-ages (green) -> teen (blue) -> mature (amber) -> adults only (red). Matching is on the letters/digits of the
    // label so "Mature 17+", "mature17" and "MATURE 17+" all land on the same stem.
    private static string? ContentRatingStem(string key) => key switch
    {
        "everyone" or "g" or "pg" or "allages" or "kidstoadults" or "kids" or "cca" => "Success",
        "everyone10" or "teen" or "teenplus" or "t" or "pg13" or "10" or "12" or "13" => "ChartBlue",
        "mature17" or "mature" or "m" or "ma15" or "r" or "15" or "16" or "17" => "Badge",
        "adultsonly18" or "adultsonly" or "adult" or "x18" or "r18" or "ao" or "18" => "Danger",
        _ => null,
    };

    /// <summary>A colour along a danger -> amber -> success ramp: <paramref name="index"/> 0 of <paramref name="count"/> is the
    /// danger end, the last index the success end. Used for the 1-5 score distribution.</summary>
    public static ScottPlot.Color RampColor(int index, int count)
    {
        if (count <= 1)
        {
            return InsightsChartTheme.Success;
        }

        double t = Math.Clamp(index / (double)(count - 1), 0, 1);
        var stops = new[] { InsightsChartTheme.Danger, InsightsChartTheme.Badge, InsightsChartTheme.Success };
        double scaled = t * (stops.Length - 1);
        int lower = Math.Min((int)Math.Floor(scaled), stops.Length - 2);
        return Mix(stops[lower], stops[lower + 1], scaled - lower);
    }

    private static ScottPlot.Color Mix(ScottPlot.Color a, ScottPlot.Color b, double fraction)
        => new((byte)Math.Round(a.R + ((b.R - a.R) * fraction)), (byte)Math.Round(a.G + ((b.G - a.G) * fraction)),
            (byte)Math.Round(a.B + ((b.B - a.B) * fraction)), a.A);
}
