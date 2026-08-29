using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Collections;

/// <summary>
/// Read/write resolver for <see cref="CollectionRelation"/> - the Collections analogue of
/// <see cref="MediaRelationResolver"/>, same directional-relation shape (a relation is stored as
/// exactly one row regardless of which collection's page it's viewed from; see that resolver's own
/// doc comment for the inversion rule this mirrors exactly). No evidence sub-table to manage - see
/// <see cref="CollectionRelation"/>'s own doc comment for why.
/// </summary>
public static class CollectionRelationResolver
{
    /// <summary>One related collection, the <see cref="RelationType"/> to display for it (already resolved for whichever side <paramref name="collectionId"/> is on), and the underlying <see cref="CollectionRelation.Id"/> (for removal).</summary>
    public static IReadOnlyList<(Collection OtherCollection, RelationType DisplayType, int CollectionRelationId)> GetRelatedCollections(PaperbunkrDbContext context, int collectionId)
    {
        var relations = context.CollectionRelations
            .Include(r => r.SourceCollection)
            .Include(r => r.TargetCollection)
            .Where(r => r.SourceCollectionId == collectionId || r.TargetCollectionId == collectionId)
            .OrderBy(r => r.CreatedAt)
            .ToList();

        var result = new List<(Collection, RelationType, int)>();
        foreach (var relation in relations)
        {
            if (relation.SourceCollectionId == collectionId)
            {
                // Viewing from the source's page - the target's own role is the inverse of the
                // stored type.
                if (relation.TargetCollection is not null)
                {
                    var displayType = RelationTypeCatalog.All[relation.RelationType].InverseType;
                    result.Add((relation.TargetCollection, displayType, relation.Id));
                }
            }
            else if (relation.SourceCollection is not null)
            {
                // Viewing from the target's page - the source's role is the stored type, as-is.
                result.Add((relation.SourceCollection, relation.RelationType, relation.Id));
            }
        }

        return result;
    }

    /// <summary>
    /// Creates a <see cref="CollectionRelation"/> row. Returns <see langword="false"/> without
    /// writing anything for a self-relation or an exact duplicate (same source/target/type triple,
    /// in either direction, since a relation is a single row regardless of which side it's created
    /// from) - same guard <see cref="MediaRelationResolver.TryCreate"/> has.
    /// </summary>
    public static bool TryCreate(PaperbunkrDbContext context, int sourceCollectionId, int targetCollectionId, RelationType relationType)
    {
        if (sourceCollectionId == targetCollectionId)
        {
            return false;
        }

        bool isDuplicate = context.CollectionRelations.Any(r =>
            r.RelationType == relationType &&
            ((r.SourceCollectionId == sourceCollectionId && r.TargetCollectionId == targetCollectionId) ||
             (r.SourceCollectionId == targetCollectionId && r.TargetCollectionId == sourceCollectionId)));
        if (isDuplicate)
        {
            return false;
        }

        context.CollectionRelations.Add(new CollectionRelation
        {
            SourceCollectionId = sourceCollectionId,
            TargetCollectionId = targetCollectionId,
            RelationType = relationType,
        });
        context.SaveChanges();
        return true;
    }

    /// <summary>Removes a <see cref="CollectionRelation"/> - a no-op if it no longer exists.</summary>
    public static void Remove(PaperbunkrDbContext context, int collectionRelationId)
    {
        var relation = context.CollectionRelations.Find(collectionRelationId);
        if (relation is not null)
        {
            context.CollectionRelations.Remove(relation);
            context.SaveChanges();
        }
    }
}
