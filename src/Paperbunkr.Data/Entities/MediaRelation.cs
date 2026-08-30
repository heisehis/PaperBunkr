namespace Paperbunkr.Data.Entities;

/// <summary>
/// A first-class connection between two nodes in the fictional-universe relation graph
/// (docs/superpowers/specs/2026-08-17-metadata-model-phase3-media-relations-design.md;
/// Collection nodes added docs/superpowers/specs/2026-08-30-media-relation-collection-nodes-
/// design.md). CE has no equivalent at all, confirmed by search, not a port. Each side is exactly
/// one of a <see cref="Series"/> or a <see cref="Collection"/> - <see cref="SourceSeriesId"/>/
/// <see cref="SourceCollectionId"/> are mutually exclusive, same for the target pair (enforced by
/// a DB <c>CHECK</c>). Collection↔Collection is rejected in <c>MediaRelationResolver.TryCreate</c>
/// - that combination is <see cref="CollectionRelation"/>'s job, not this entity's.
///
/// <see cref="RelationType"/> always describes the source side's own role relative to the target
/// side - e.g. <c>RelationType.Prequel</c> means "Source is the Prequel of Target," not the
/// reverse. A relation is always stored as exactly one row regardless of which side's page it's
/// being viewed from; see <c>MediaRelationResolver.GetRelatedFromSeries</c>/
/// <c>GetRelatedFromCollection</c> for how a directional relation reads correctly from either end
/// without a duplicate row.
/// </summary>
public class MediaRelation
{
    public int Id { get; set; }

    public int? SourceSeriesId { get; set; }

    public Series? SourceSeries { get; set; }

    public int? SourceCollectionId { get; set; }

    public Collection? SourceCollection { get; set; }

    public int? TargetSeriesId { get; set; }

    public Series? TargetSeries { get; set; }

    public int? TargetCollectionId { get; set; }

    public Collection? TargetCollection { get; set; }

    public RelationType RelationType { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<RelationEvidence> Evidence { get; set; } = new();
}
