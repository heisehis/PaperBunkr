using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Read-side resolver for <see cref="MediaRelation"/> (docs/superpowers/specs/2026-08-17-metadata-
/// model-phase3-media-relations-design.md) - a relation is stored as exactly one row regardless of
/// which series' Detail page it's viewed from; this is what makes a directional relation (e.g.
/// Prequel) read correctly from either side without a duplicate row.
///
/// <see cref="MediaRelation.RelationType"/> always describes <see cref="MediaRelation.SourceSeries"/>'s
/// role relative to <see cref="MediaRelation.TargetSeries"/> ("Source is the Prequel of Target").
/// So: viewed from the <em>target</em>'s page, the source's card shows the stored type as-is (it
/// already describes the source correctly). Viewed from the <em>source</em>'s page, the target's
/// card needs <see cref="RelationTypeCatalog"/>'s inverse (the target is the *opposite* role - the
/// Sequel, not the Prequel). Getting this backwards was a real bug caught before it shipped: an
/// early version inverted on the target side instead, which would have shown "Sequel" on the
/// earlier work's own page instead of the later one's.
/// </summary>
public static class MediaRelationResolver
{
    /// <summary>One related series, the <see cref="RelationType"/> to display for it (already resolved for whichever side <paramref name="seriesId"/> is on), and the underlying <see cref="MediaRelation.Id"/> (for removal).</summary>
    public static IReadOnlyList<(Series OtherSeries, RelationType DisplayType, int MediaRelationId)> GetRelatedSeries(PaperbunkrDbContext context, int seriesId)
    {
        var relations = context.MediaRelations
            .Include(m => m.SourceSeries)
            .Include(m => m.TargetSeries)
            .Where(m => m.SourceSeriesId == seriesId || m.TargetSeriesId == seriesId)
            .OrderBy(m => m.CreatedAt)
            .ToList();

        var result = new List<(Series, RelationType, int)>();
        foreach (var relation in relations)
        {
            if (relation.SourceSeriesId == seriesId)
            {
                // Viewing from the source's page - the target's own role is the inverse of the
                // stored type.
                if (relation.TargetSeries is not null)
                {
                    var displayType = RelationTypeCatalog.All[relation.RelationType].InverseType;
                    result.Add((relation.TargetSeries, displayType, relation.Id));
                }
            }
            else if (relation.SourceSeries is not null)
            {
                // Viewing from the target's page - the source's role is the stored type, as-is.
                result.Add((relation.SourceSeries, relation.RelationType, relation.Id));
            }
        }

        return result;
    }

    /// <summary>
    /// Creates a <see cref="MediaRelation"/> plus its single user-asserted <see cref="RelationEvidence"/>
    /// row (docs/superpowers/specs/2026-08-17-metadata-model-phase3-media-relations-design.md).
    /// Returns <see langword="false"/> without writing anything for a self-relation or an exact
    /// duplicate (same source/target/type triple, in either direction, since a relation is a single
    /// row regardless of which side it's created from).
    /// </summary>
    public static bool TryCreate(PaperbunkrDbContext context, int sourceSeriesId, int targetSeriesId, RelationType relationType)
    {
        if (sourceSeriesId == targetSeriesId)
        {
            return false;
        }

        bool isDuplicate = context.MediaRelations.Any(m =>
            m.RelationType == relationType &&
            ((m.SourceSeriesId == sourceSeriesId && m.TargetSeriesId == targetSeriesId) ||
             (m.SourceSeriesId == targetSeriesId && m.TargetSeriesId == sourceSeriesId)));
        if (isDuplicate)
        {
            return false;
        }

        var relation = new MediaRelation { SourceSeriesId = sourceSeriesId, TargetSeriesId = targetSeriesId, RelationType = relationType };
        relation.Evidence.Add(new RelationEvidence { MediaRelation = relation, Provider = RelationEvidenceProvider.User, Confidence = 1.0m });
        context.MediaRelations.Add(relation);
        context.SaveChanges();
        return true;
    }

    /// <summary>Removes a <see cref="MediaRelation"/> (and cascades to its <see cref="RelationEvidence"/>) - a no-op if it no longer exists.</summary>
    public static void Remove(PaperbunkrDbContext context, int mediaRelationId)
    {
        var relation = context.MediaRelations.Find(mediaRelationId);
        if (relation is not null)
        {
            context.MediaRelations.Remove(relation);
            context.SaveChanges();
        }
    }
}
