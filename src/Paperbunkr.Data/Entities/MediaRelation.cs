namespace Paperbunkr.Data.Entities;

/// <summary>
/// A first-class Series-to-Series connection (docs/superpowers/specs/2026-08-17-metadata-model-
/// phase3-media-relations-design.md) - CE has no equivalent at all, confirmed by search, not a
/// port. <see cref="RelationType"/> always describes <see cref="SourceSeries"/>'s own role
/// relative to <see cref="TargetSeries"/> - e.g. <c>RelationType.Prequel</c> means "Source is the
/// Prequel of Target," not the reverse. A relation is always stored as exactly one row regardless
/// of which series' Detail page it's being viewed from; see
/// <c>MediaRelationResolver.GetRelatedSeries</c> for how a directional relation reads correctly
/// from either end without a duplicate row.
/// </summary>
public class MediaRelation
{
    public int Id { get; set; }

    public int SourceSeriesId { get; set; }

    public Series? SourceSeries { get; set; }

    public int TargetSeriesId { get; set; }

    public Series? TargetSeries { get; set; }

    public RelationType RelationType { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<RelationEvidence> Evidence { get; set; } = new();
}
