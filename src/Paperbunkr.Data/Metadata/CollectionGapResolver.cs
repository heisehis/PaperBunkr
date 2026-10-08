using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>How strict a gap scan is. Insights' "Gaps" tile and Library Health's "Collection gaps" share one algorithm and differ only here.</summary>
/// <param name="MinNumericIssues">Fewest numbered issues a series needs before its run is looked at.</param>
/// <param name="OwnershipFloor">Smallest owned share of the first-to-last span (0 = any).</param>
/// <param name="MissingCap">Most missing numbers a series may have and still be listed (null = any).</param>
/// <param name="Limit">Most series returned (null = all).</param>
/// <param name="WholeNumbersOnly">Ignore fractional numbers (#3.5) instead of flooring them, and ignore placeholders (wanted, not owned).</param>
public sealed record CollectionGapOptions(int MinNumericIssues, double OwnershipFloor, int? MissingCap, int? Limit, bool WholeNumbersOnly)
{
    /// <summary>Insights → Today's tile: only a run you own most of, with a handful of holes.</summary>
    public static readonly CollectionGapOptions Insights = new(
        MinNumericIssues: 3, InsightsResolver.GapOwnershipFloor, InsightsResolver.GapMissingCap, InsightsResolver.AttentionListLimit, WholeNumbersOnly: false);

    /// <summary>Library Health's collection-wide view (docs/superpowers/specs/2026-10-06-smart-features-design.md §3.3): every hole in every run.</summary>
    public static readonly CollectionGapOptions Library = new(MinNumericIssues: 2, OwnershipFloor: 0, MissingCap: null, Limit: null, WholeNumbersOnly: true);
}

/// <summary>One series with holes in its run, for Library Health's table.</summary>
/// <param name="Owned">The owned numbers as ranges ("1–6, 9, 11–14").</param>
/// <param name="Missing">The missing numbers as ranges ("#7–8, #10").</param>
/// <param name="DismissalKey">Series id + the missing numbers, so a dismissal stops matching (the row comes back) when a new hole appears.</param>
public sealed record CollectionGapRow(int SeriesId, string SeriesName, string? Publisher, string Owned, string Missing, int MissingCount, string DismissalKey);

/// <summary>
/// The holes in each series' run of issue numbers, from the numbers you own alone - no provider catalog (that is Series Detail's
/// "Missing Issues", which needs the series tracked). CE has only a per-series gap count (<c>ComicBookSeriesStatistics.GapCount</c>);
/// naming the missing numbers library-wide is a deliberate deviation.
/// </summary>
public static class CollectionGapResolver
{
    /// <summary>
    /// The shared core. <paramref name="issues"/> must have <see cref="Issue.Series"/> loaded and share Series instances per series.
    /// Ordered closest-to-complete first.
    /// </summary>
    public static IReadOnlyList<CollectionGap> Compute(IEnumerable<Issue> issues, CollectionGapOptions options)
    {
        var result = new List<CollectionGap>();
        foreach (var group in issues.Where(i => i.Series != null).GroupBy(i => i.Series!))
        {
            var numeric = group
                .Where(i => !options.WholeNumbersOnly || !i.IsPlaceholder)
                .Where(i => i.NumberType() == IssueNumberType.Numeric && i.NumberSortKey() is { } k && k >= 0)
                .Select(i => i.NumberSortKey()!.Value)
                .Where(k => !options.WholeNumbersOnly || k == Math.Floor(k))
                .Select(k => (int)Math.Floor(k))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            // Need a real run to talk about a "gap" in.
            if (numeric.Count < options.MinNumericIssues)
            {
                continue;
            }

            int span = numeric[^1] - numeric[0] + 1;
            if ((double)numeric.Count / span < options.OwnershipFloor)
            {
                continue; // you own a scattering across a wide range - not a fill-the-holes situation
            }

            var owned = numeric.ToHashSet();
            var missing = new List<int>();
            for (int n = numeric[0]; n <= numeric[^1]; n++)
            {
                if (!owned.Contains(n))
                {
                    missing.Add(n);
                }
            }

            if (missing.Count > 0 && (options.MissingCap is not int cap || missing.Count <= cap))
            {
                result.Add(new CollectionGap(group.Key.Id, group.Key.Name, missing) { OwnedNumbers = numeric });
            }
        }

        IEnumerable<CollectionGap> ordered = result.OrderBy(g => g.MissingNumbers.Count);
        return (options.Limit is int limit ? ordered.Take(limit) : ordered).ToList();
    }

    /// <summary>
    /// Library Health's rows: local issues only (a remote library may be shared incompletely by design), placeholders and fractional
    /// numbers ignored, an issue whose file is missing still counted as owned (the Files tab already reports it).
    /// </summary>
    public static IReadOnlyList<CollectionGapRow> ForLibrary(PaperbunkrDbContext context)
    {
        var issues = context.Issues.AsNoTrackingWithIdentityResolution().Include(i => i.Series).ToList();
        var publisherBySeries = issues
            .Where(i => !string.IsNullOrWhiteSpace(i.Publisher))
            .GroupBy(i => i.SeriesId)
            .ToDictionary(g => g.Key, g => g.GroupBy(i => i.Publisher!, StringComparer.OrdinalIgnoreCase).OrderByDescending(p => p.Count()).First().Key);

        return Compute(issues, CollectionGapOptions.Library)
            .OrderBy(g => g.MissingNumbers.Count)
            .ThenBy(g => g.SeriesName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CollectionGapRow(
                g.SeriesId,
                g.SeriesName,
                publisherBySeries.TryGetValue(g.SeriesId, out var publisher) ? publisher : null,
                FormatRanges(g.OwnedNumbers, prefix: string.Empty),
                FormatRanges(g.MissingNumbers, prefix: "#"),
                g.MissingNumbers.Count,
                DismissalKey(g.SeriesId, g.MissingNumbers)))
            .ToList();
    }

    public static string DismissalKey(int seriesId, IReadOnlyList<int> missing) => $"{seriesId}:{FormatRanges(missing, string.Empty, "-", ",")}";

    /// <summary>Sorted numbers as collapsed ranges: 7, 8, 10 → "#7–8, #10".</summary>
    public static string FormatRanges(IReadOnlyList<int> sorted, string prefix, string dash = "–", string separator = ", ")
    {
        var parts = new List<string>();
        int index = 0;
        while (index < sorted.Count)
        {
            int start = sorted[index];
            int end = start;
            while (index + 1 < sorted.Count && sorted[index + 1] == end + 1)
            {
                end = sorted[++index];
            }

            parts.Add(start == end ? $"{prefix}{start}" : $"{prefix}{start}{dash}{end}");
            index++;
        }

        return string.Join(separator, parts);
    }
}
