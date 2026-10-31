using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>One series rated by you and by the community, both on 0-100.</summary>
/// <param name="Delta">Your score minus the community's: positive means you rate it higher than they do.</param>
/// <param name="SourceCount">How many independent community sources fed <see cref="Community"/> (each provider, plus the comics' issue ratings as one).</param>
public sealed record CommunityComparisonRow(int SeriesId, string SeriesName, double User, double Community, double Delta, int SourceCount);

/// <summary>
/// The "You vs Community" Insights card's data (docs/superpowers/specs/2026-10-26-you-vs-community-and-chapter-alignment-design.md Part 2).
/// <see cref="RatedSeries"/> is every series you rated; <see cref="Rows"/> are the ones that also have community data.
/// <see cref="HigherThanCommunity"/> / <see cref="LowerThanCommunity"/> are the biggest disagreements each way.
/// </summary>
public sealed record CommunityComparison(
    int RatedSeries,
    IReadOnlyList<CommunityComparisonRow> Rows,
    IReadOnlyList<CommunityComparisonRow> HigherThanCommunity,
    IReadOnlyList<CommunityComparisonRow> LowerThanCommunity)
{
    public int ComparableSeries => Rows.Count;

    public static CommunityComparison Empty { get; } =
        new(0, Array.Empty<CommunityComparisonRow>(), Array.Empty<CommunityComparisonRow>(), Array.Empty<CommunityComparisonRow>());
}

/// <summary>
/// Compares your series ratings with community ratings, all normalized to 0-100.
/// Your rating (<see cref="Series.Rating"/>) is CE's 0-5 stars, so it scales by 20. The community side is the plain mean of:
/// every provider's <see cref="ExternalRating"/> (manga; each already carries its own scale), and the comic issues' Metron
/// <see cref="Issue.CommunityRating"/> (0-5) weighted by <see cref="Issue.CommunityRatingCount"/>, counted as one source. With a single comic
/// source and no counts this is CE's <c>ComicBookSeriesStatistics.AverageCommunityRating</c> (the mean over books rated above 0).
/// </summary>
public static class CommunityComparisonResolver
{
    /// <summary>How many series each "biggest disagreement" list holds.</summary>
    public const int TopCount = 5;

    private const double StarsToPercent = 20.0;

    public static CommunityComparison Build(PaperbunkrDbContext context)
    {
        var rated = context.Series
            .Where(s => s.Rating != null && s.Rating > 0)
            .Select(s => new { s.Id, s.Name, Rating = s.Rating!.Value })
            .ToList();
        if (rated.Count == 0)
        {
            return CommunityComparison.Empty;
        }

        var ids = rated.Select(r => r.Id).ToHashSet();
        var external = context.ExternalRatings
            .Where(r => r.Scale > 0 && r.Value > 0)
            .Select(r => new { r.SeriesId, r.Value, r.Scale })
            .ToList()
            .Where(r => ids.Contains(r.SeriesId))
            .GroupBy(r => r.SeriesId)
            .ToDictionary(g => g.Key, g => g.Select(r => (double)(r.Value / r.Scale) * 100.0).ToList());
        var comics = context.Issues
            .Where(i => i.CommunityRating != null && i.CommunityRating > 0)
            .Select(i => new { i.SeriesId, Rating = i.CommunityRating!.Value, i.CommunityRatingCount })
            .ToList()
            .Where(i => ids.Contains(i.SeriesId))
            .GroupBy(i => i.SeriesId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<CommunityComparisonRow>();
        foreach (var series in rated)
        {
            var sources = new List<double>();
            if (external.TryGetValue(series.Id, out var providerScores))
            {
                sources.AddRange(providerScores);
            }

            if (comics.TryGetValue(series.Id, out var issues))
            {
                double weight = issues.Sum(i => Math.Max(1, i.CommunityRatingCount ?? 1));
                double weighted = issues.Sum(i => i.Rating * Math.Max(1, i.CommunityRatingCount ?? 1)) / weight;
                sources.Add(weighted * StarsToPercent);
            }

            if (sources.Count == 0)
            {
                continue;
            }

            double user = series.Rating * StarsToPercent;
            double community = sources.Average();
            rows.Add(new CommunityComparisonRow(series.Id, series.Name, Math.Round(user, 1), Math.Round(community, 1), Math.Round(user - community, 1), sources.Count));
        }

        var higher = rows.Where(r => r.Delta > 0).OrderByDescending(r => r.Delta).ThenBy(r => r.SeriesName, StringComparer.OrdinalIgnoreCase).Take(TopCount).ToList();
        var lower = rows.Where(r => r.Delta < 0).OrderBy(r => r.Delta).ThenBy(r => r.SeriesName, StringComparer.OrdinalIgnoreCase).Take(TopCount).ToList();
        return new CommunityComparison(rated.Count, rows, higher, lower);
    }
}
