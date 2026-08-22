using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Read-side resolver for external-provider data (docs/superpowers/specs/2026-08-17-metadata-
/// model-phase5a-external-metadata-schema-design.md) - read-only this pass, since no adapter
/// exists yet to drive writes. A future adapter phase owns its own write path (likely writing
/// directly via <c>context.ExternalMediaIds.Add(...)</c>, the same shape
/// <see cref="MediaRelationResolver.TryCreate"/> already uses), not through this resolver.
/// </summary>
public static class ExternalMetadataResolver
{
    public static IReadOnlyList<ExternalMediaId> GetExternalIds(PaperbunkrDbContext context, int seriesId) =>
        context.ExternalMediaIds.Where(e => e.SeriesId == seriesId).ToList();

    public static IReadOnlyList<ExternalRating> GetRatings(PaperbunkrDbContext context, int seriesId) =>
        context.ExternalRatings.Where(r => r.SeriesId == seriesId).ToList();

    public static ExternalMetadataSnapshot? GetLatestSnapshot(PaperbunkrDbContext context, int seriesId, ExternalMetadataProvider provider) =>
        context.ExternalMetadataSnapshots
            .Where(s => s.SeriesId == seriesId && s.Provider == provider)
            .OrderByDescending(s => s.RetrievedAt)
            .FirstOrDefault();
}
