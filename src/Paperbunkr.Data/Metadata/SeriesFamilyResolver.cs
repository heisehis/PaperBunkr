using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Graph-driven "series family" scoping for the age-progression timeline (docs/superpowers/specs/
/// 2026-08-27-metadata-model-phase4g-age-progression-design.md). A family is the connected
/// component reachable from the given series by following <see cref="MediaRelation"/> edges (via
/// <see cref="MediaRelationResolver.GetRelatedSeries"/>) plus every series sharing any of its
/// <c>Continuity</c> rows (via <see cref="ContinuityResolver.GetOtherSeriesSharingContinuity"/>),
/// unioned and deduplicated. Pure query - no new entity or join table.
///
/// Explicitly not character-aware this phase: an unrelated one-shot that shares no
/// <see cref="MediaRelation"/> and no <c>Continuity</c> with the chosen series won't appear even if
/// the same character features in both. That's the known, accepted gap the deferred first-class
/// <c>Character</c> entity leaves open.
/// </summary>
// internal - see MediaRelationResolver (Plugin API v3 §7). Plugins read through IMetadataGraph.
internal static class SeriesFamilyResolver
{
    /// <param name="characterAware">
    /// When <see langword="true"/>, the connected component is expanded once more by
    /// <see cref="CharacterResolver.GetSeriesIdsSharingCharacterWith"/> - picking up an unrelated
    /// one-shot that shares a character but no <see cref="MediaRelation"/> / <c>Continuity</c>
    /// (docs/superpowers/specs/2026-08-27-metadata-model-phase4g-age-progression-design.md's
    /// documented gap). Deliberately a single expansion, not a transitive one - see
    /// <see cref="CharacterResolver.GetSeriesIdsSharingCharacterWith"/>.
    /// </param>
    public static IReadOnlyList<Series> GetFamily(PaperbunkrDbContext context, int seriesId, bool characterAware = false)
    {
        var visited = new HashSet<int> { seriesId };
        var queue = new Queue<int>();
        queue.Enqueue(seriesId);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();

            var neighbours = MediaRelationResolver.GetRelatedFromSeries(context, current)
                .Where(r => r.Kind == MediaRelationEndpointKind.Series)
                .Select(r => r.Series!.Id)
                .Concat(ContinuityResolver.GetOtherSeriesSharingContinuity(context, current).Select(s => s.Id));

            foreach (int neighbour in neighbours)
            {
                if (visited.Add(neighbour))
                {
                    queue.Enqueue(neighbour);
                }
            }
        }

        if (characterAware)
        {
            foreach (int shared in CharacterResolver.GetSeriesIdsSharingCharacterWith(context, visited))
            {
                visited.Add(shared);
            }
        }

        return context.Series
            .Include(s => s.Issues)
            .Where(s => visited.Contains(s.Id))
            .OrderBy(s => s.Name)
            .ToList();
    }
}
