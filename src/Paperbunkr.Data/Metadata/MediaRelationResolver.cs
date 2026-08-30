using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>Which kind of node sits on one side of a <see cref="MediaRelation"/> (docs/superpowers/specs/2026-08-30-media-relation-collection-nodes-design.md).</summary>
public enum MediaRelationEndpointKind
{
    Series,
    Collection,
}

/// <summary>
/// One related node, resolved: the <see cref="RelationType"/> to display for it (already resolved
/// for whichever side the query was rooted at), and the underlying <see cref="MediaRelation.Id"/>
/// (for removal). Exactly one of <see cref="Series"/>/<see cref="Collection"/> is non-null,
/// matching <see cref="Kind"/> - mirrors <c>CollectionResolver.CollectionMember</c>'s discriminated-
/// result shape.
/// </summary>
public sealed record MediaRelationEndpoint(
    MediaRelationEndpointKind Kind,
    Series? Series,
    Collection? Collection,
    RelationType DisplayType,
    int MediaRelationId);

/// <summary>
/// Read-side resolver for <see cref="MediaRelation"/> (docs/superpowers/specs/2026-08-17-metadata-
/// model-phase3-media-relations-design.md; Collection nodes added docs/superpowers/specs/2026-08-30-
/// media-relation-collection-nodes-design.md) - a relation is stored as exactly one row regardless
/// of which side's Detail/editor page it's viewed from; this is what makes a directional relation
/// (e.g. Prequel) read correctly from either side without a duplicate row.
///
/// <see cref="MediaRelation.RelationType"/> always describes the source side's own role relative to
/// the target side ("Source is the Prequel of Target"). So: viewed from the <em>target</em>'s page,
/// the source's card shows the stored type as-is (it already describes the source correctly).
/// Viewed from the <em>source</em>'s page, the target's card needs <see cref="RelationTypeCatalog"/>'s
/// inverse (the target is the *opposite* role - the Sequel, not the Prequel). Getting this backwards
/// was a real bug caught before it shipped: an early version inverted on the target side instead,
/// which would have shown "Sequel" on the earlier work's own page instead of the later one's.
/// </summary>
// internal (docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-design.md §7) - reachable
// from Paperbunkr.App (IMetadataGraph adapter) and the test assemblies via InternalsVisibleTo, but
// not from a plugin .csx script referencing Paperbunkr.Data.dll. Plugins read this graph through
// IMetadataGraph.
internal static class MediaRelationResolver
{
    /// <summary>Every relation touching the given series, resolved to the *other* side (Series or Collection) with directional inversion already applied.</summary>
    public static IReadOnlyList<MediaRelationEndpoint> GetRelatedFromSeries(PaperbunkrDbContext context, int seriesId)
    {
        var relations = context.MediaRelations
            .Include(m => m.SourceSeries)
            .Include(m => m.TargetSeries)
            .Include(m => m.SourceCollection)
            .Include(m => m.TargetCollection)
            .Where(m => m.SourceSeriesId == seriesId || m.TargetSeriesId == seriesId)
            .OrderBy(m => m.CreatedAt)
            .ToList();

        var result = new List<MediaRelationEndpoint>();
        foreach (var relation in relations)
        {
            if (relation.SourceSeriesId == seriesId)
            {
                // Viewing from the source's page - the target's own role is the inverse of the
                // stored type.
                var displayType = RelationTypeCatalog.All[relation.RelationType].InverseType;
                AddOtherSide(result, relation, isSource: false, displayType);
            }
            else
            {
                // Viewing from the target's page - the source's role is the stored type, as-is.
                AddOtherSide(result, relation, isSource: true, relation.RelationType);
            }
        }

        return result;
    }

    /// <summary>Every relation touching the given collection, resolved to the *other* side with directional inversion already applied. A collection's own relations can only have a Series on the other side (Collection↔Collection is rejected in <see cref="TryCreate"/>), but the directionality logic is shared with <see cref="GetRelatedFromSeries"/> regardless.</summary>
    public static IReadOnlyList<MediaRelationEndpoint> GetRelatedFromCollection(PaperbunkrDbContext context, int collectionId)
    {
        var relations = context.MediaRelations
            .Include(m => m.SourceSeries)
            .Include(m => m.TargetSeries)
            .Include(m => m.SourceCollection)
            .Include(m => m.TargetCollection)
            .Where(m => m.SourceCollectionId == collectionId || m.TargetCollectionId == collectionId)
            .OrderBy(m => m.CreatedAt)
            .ToList();

        var result = new List<MediaRelationEndpoint>();
        foreach (var relation in relations)
        {
            if (relation.SourceCollectionId == collectionId)
            {
                var displayType = RelationTypeCatalog.All[relation.RelationType].InverseType;
                AddOtherSide(result, relation, isSource: false, displayType);
            }
            else
            {
                AddOtherSide(result, relation, isSource: true, relation.RelationType);
            }
        }

        return result;
    }

    private static void AddOtherSide(List<MediaRelationEndpoint> result, MediaRelation relation, bool isSource, RelationType displayType)
    {
        var series = isSource ? relation.SourceSeries : relation.TargetSeries;
        var collection = isSource ? relation.SourceCollection : relation.TargetCollection;

        if (series is not null)
        {
            result.Add(new MediaRelationEndpoint(MediaRelationEndpointKind.Series, series, null, displayType, relation.Id));
        }
        else if (collection is not null)
        {
            result.Add(new MediaRelationEndpoint(MediaRelationEndpointKind.Collection, null, collection, displayType, relation.Id));
        }
    }

    /// <summary>
    /// Creates a <see cref="MediaRelation"/> plus its single user-asserted <see cref="RelationEvidence"/>
    /// row (docs/superpowers/specs/2026-08-17-metadata-model-phase3-media-relations-design.md).
    /// Returns <see langword="false"/> without writing anything for: a self-relation, an exact
    /// duplicate (same source/target/type triple, in either direction), or a Collection↔Collection
    /// pair (that combination is <see cref="CollectionRelation"/>'s job, not this entity's - see
    /// docs/superpowers/specs/2026-08-30-media-relation-collection-nodes-design.md).
    /// </summary>
    public static bool TryCreate(
        PaperbunkrDbContext context,
        MediaRelationEndpointKind sourceKind,
        int sourceId,
        MediaRelationEndpointKind targetKind,
        int targetId,
        RelationType relationType)
    {
        if (sourceKind == MediaRelationEndpointKind.Collection && targetKind == MediaRelationEndpointKind.Collection)
        {
            return false;
        }

        if (sourceKind == targetKind && sourceId == targetId)
        {
            return false;
        }

        // MatchesSide can't be translated to SQL, so filter by RelationType server-side (the
        // selective part) and evaluate the source/target match in memory - this table is a small
        // per-series/collection graph, not a library-scale one.
        bool isDuplicate = context.MediaRelations
            .Where(m => m.RelationType == relationType)
            .AsEnumerable()
            .Any(m =>
                (MatchesSide(m.SourceSeriesId, m.SourceCollectionId, sourceKind, sourceId) && MatchesSide(m.TargetSeriesId, m.TargetCollectionId, targetKind, targetId)) ||
                (MatchesSide(m.SourceSeriesId, m.SourceCollectionId, targetKind, targetId) && MatchesSide(m.TargetSeriesId, m.TargetCollectionId, sourceKind, sourceId)));
        if (isDuplicate)
        {
            return false;
        }

        var relation = new MediaRelation
        {
            SourceSeriesId = sourceKind == MediaRelationEndpointKind.Series ? sourceId : null,
            SourceCollectionId = sourceKind == MediaRelationEndpointKind.Collection ? sourceId : null,
            TargetSeriesId = targetKind == MediaRelationEndpointKind.Series ? targetId : null,
            TargetCollectionId = targetKind == MediaRelationEndpointKind.Collection ? targetId : null,
            RelationType = relationType,
        };
        relation.Evidence.Add(new RelationEvidence { MediaRelation = relation, Provider = RelationEvidenceProvider.User, Confidence = 1.0m });
        context.MediaRelations.Add(relation);
        context.SaveChanges();
        return true;
    }

    /// <summary>Series↔Series convenience overload - the shape every pre-existing caller (and test) already uses.</summary>
    public static bool TryCreate(PaperbunkrDbContext context, int sourceSeriesId, int targetSeriesId, RelationType relationType) =>
        TryCreate(context, MediaRelationEndpointKind.Series, sourceSeriesId, MediaRelationEndpointKind.Series, targetSeriesId, relationType);

    private static bool MatchesSide(int? seriesId, int? collectionId, MediaRelationEndpointKind kind, int id) =>
        kind == MediaRelationEndpointKind.Series ? seriesId == id : collectionId == id;

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
