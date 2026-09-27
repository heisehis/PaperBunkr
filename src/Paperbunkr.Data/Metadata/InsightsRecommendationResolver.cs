using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// The Insights screen's "Because you finished X" recommendations section (docs/superpowers/specs/
/// 2026-09-23-insights-recommendations-surface-design.md) - reuses the existing
/// <see cref="RecommendationResolver"/> as-is, seeded by the most recently **finished** series (not
/// opened, unlike the Home screen's own "Because You Read" module, which seeds from
/// <see cref="HomeFeedResolver.GetRecentlyOpenedSeriesIds"/>). Deliberately excludes anything already
/// appearing in Home's current picks by recomputing that same exclusion set from the identical public,
/// stateless functions Home itself calls - no shared state, no cross-screen coupling beyond calling the
/// same pure functions twice.
/// </summary>
public static class InsightsRecommendationResolver
{
    public static InsightsRecommendationSeed? GetSeedWithRecommendations(PaperbunkrDbContext context)
    {
        var candidateSeeds = context.ReadingEvents.AsNoTracking()
            .Where(e => e.Kind == ReadingEventKind.Finished && e.SeriesId != null)
            .ToList()
            .GroupBy(e => e.SeriesId!.Value)
            .Select(g => (SeriesId: g.Key, LatestFinish: g.Max(e => e.TimestampUtc)))
            .OrderByDescending(x => x.LatestFinish)
            .Take(5)
            .Select(x => x.SeriesId)
            .ToList();

        if (candidateSeeds.Count == 0)
        {
            return null;
        }

        var homeExclusionSet = BuildHomeExclusionSet(context);

        foreach (int seedId in candidateSeeds)
        {
            var recommendations = RecommendationResolver.GetRecommendations(context, seedId, limit: 20)
                .Where(r => !homeExclusionSet.Contains(r.TargetSeriesId))
                .Take(6)
                .ToList();

            if (recommendations.Count == 0)
            {
                continue;
            }

            string? seedName = context.Series.AsNoTracking().Where(s => s.Id == seedId).Select(s => s.Name).FirstOrDefault();
            if (seedName is null)
            {
                continue; // seed series gone - shouldn't happen (ReadingEvent survives deletion, but the seed itself must still resolve to show a header)
            }

            return new InsightsRecommendationSeed(seedId, seedName, recommendations);
        }

        return null;
    }

    /// <summary>Every <see cref="RecommendationCandidate.TargetSeriesId"/> currently shown in Home's own
    /// "Because You Read" rows - recomputed here from the same public functions
    /// <c>HomeScreenViewModel.BuildSnapshot</c> already calls, not read from any shared state.</summary>
    private static HashSet<int> BuildHomeExclusionSet(PaperbunkrDbContext context)
    {
        var exclusion = new HashSet<int>();
        foreach (int homeSeedId in HomeFeedResolver.GetRecentlyOpenedSeriesIds(context))
        {
            foreach (var candidate in RecommendationResolver.GetRecommendations(context, homeSeedId))
            {
                exclusion.Add(candidate.TargetSeriesId);
            }
        }

        return exclusion;
    }
}

public sealed record InsightsRecommendationSeed(int SeedSeriesId, string SeedSeriesName, IReadOnlyList<RecommendationCandidate> Recommendations);
