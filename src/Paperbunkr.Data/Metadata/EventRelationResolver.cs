using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Read/write resolver for <see cref="EventRelation"/> (docs/superpowers/specs/2026-08-27-metadata-
/// model-phase4d-event-relations-design.md), mirroring <see cref="MediaRelationResolver"/>'s shape.
/// A relation is stored as exactly one row regardless of which event's detail pane it's viewed from.
///
/// <see cref="EventRelation.RelationType"/> is read as-stored when the queried event is the
/// <see cref="EventRelation.SourceEvent"/>, and inverted (via
/// <see cref="RelationTypeCatalog"/>'s <see cref="RelationTypeInfo.InverseType"/>) when the queried
/// event is the <see cref="EventRelation.TargetEvent"/> - so a stored "Prequel" reads as "Prequel"
/// from the earlier event and "Sequel" from the later one, and a symmetric type (Crossover) reads
/// the same from either side.
/// </summary>
// internal - see MediaRelationResolver (Plugin API v3 §7). Plugins read through IMetadataGraph.
internal static class EventRelationResolver
{
    /// <summary>One related event, the <see cref="RelationType"/> to display for it (already
    /// resolved for whichever side <paramref name="storyEventId"/> is on), and the underlying
    /// <see cref="EventRelation.Id"/> (for removal).</summary>
    public static IReadOnlyList<(StoryEvent OtherEvent, RelationType DisplayType, int EventRelationId)> GetRelatedEvents(PaperbunkrDbContext context, int storyEventId)
    {
        var relations = context.EventRelations
            .Include(r => r.SourceEvent)
            .Include(r => r.TargetEvent)
            .Where(r => r.SourceEventId == storyEventId || r.TargetEventId == storyEventId)
            .OrderBy(r => r.CreatedAt)
            .ToList();

        var result = new List<(StoryEvent, RelationType, int)>();
        foreach (var relation in relations)
        {
            if (relation.SourceEventId == storyEventId)
            {
                if (relation.TargetEvent is not null)
                {
                    result.Add((relation.TargetEvent, relation.RelationType, relation.Id));
                }
            }
            else if (relation.SourceEvent is not null)
            {
                var displayType = RelationTypeCatalog.All[relation.RelationType].InverseType;
                result.Add((relation.SourceEvent, displayType, relation.Id));
            }
        }

        return result;
    }

    /// <summary>
    /// Creates an <see cref="EventRelation"/> plus its single user-asserted
    /// <see cref="EventRelationEvidence"/> row. Returns <see langword="false"/> without writing
    /// anything for a self-relation or an exact duplicate (same source/target/type triple, in
    /// either direction, since a relation is a single row regardless of which side it's created from).
    /// </summary>
    public static bool TryCreate(PaperbunkrDbContext context, int sourceEventId, int targetEventId, RelationType relationType)
    {
        if (sourceEventId == targetEventId)
        {
            return false;
        }

        bool isDuplicate = context.EventRelations.Any(r =>
            r.RelationType == relationType &&
            ((r.SourceEventId == sourceEventId && r.TargetEventId == targetEventId) ||
             (r.SourceEventId == targetEventId && r.TargetEventId == sourceEventId)));
        if (isDuplicate)
        {
            return false;
        }

        var relation = new EventRelation { SourceEventId = sourceEventId, TargetEventId = targetEventId, RelationType = relationType };
        relation.Evidence.Add(new EventRelationEvidence { EventRelation = relation, Provider = RelationEvidenceProvider.User, Confidence = 1.0m });
        context.EventRelations.Add(relation);
        context.SaveChanges();
        return true;
    }

    /// <summary>
    /// The transitive connected component of events reachable from <paramref name="rootEventId"/>
    /// by following <see cref="EventRelation"/> edges (docs/superpowers/specs/2026-08-27-metadata-
    /// model-phase4d-event-relations-design.md's "fuller visualization... once real connected-event
    /// data exists"). Each entry carries its BFS <c>Depth</c> (hop count) from the root so a view
    /// can indent by chain distance. The root itself is included at depth 0.
    /// </summary>
    public static IReadOnlyList<(StoryEvent Event, int Depth)> GetEventFamily(PaperbunkrDbContext context, int rootEventId)
    {
        var root = context.StoryEvents.FirstOrDefault(e => e.Id == rootEventId);
        if (root is null)
        {
            return System.Array.Empty<(StoryEvent, int)>();
        }

        var depthById = new Dictionary<int, int> { [rootEventId] = 0 };
        var order = new List<StoryEvent> { root };
        var queue = new Queue<int>();
        queue.Enqueue(rootEventId);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            int nextDepth = depthById[current] + 1;

            foreach (var (other, _, _) in GetRelatedEvents(context, current))
            {
                if (depthById.TryAdd(other.Id, nextDepth))
                {
                    order.Add(other);
                    queue.Enqueue(other.Id);
                }
            }
        }

        return order
            .Select(e => (e, depthById[e.Id]))
            .OrderBy(t => t.Item2)
            .ThenBy(t => t.e.Name)
            .ToList();
    }

    /// <summary>Removes an <see cref="EventRelation"/> (and cascades to its <see cref="EventRelationEvidence"/>) - a no-op if it no longer exists.</summary>
    public static void Remove(PaperbunkrDbContext context, int eventRelationId)
    {
        var relation = context.EventRelations.Find(eventRelationId);
        if (relation is not null)
        {
            context.EventRelations.Remove(relation);
            context.SaveChanges();
        }
    }
}
