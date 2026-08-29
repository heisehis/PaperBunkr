namespace Paperbunkr.Data.Entities;

/// <summary>
/// A first-class Collection-to-Collection connection - the Collections analogue of
/// <see cref="MediaRelation"/> (docs/superpowers/specs/2026-08-17-metadata-model-phase3-media-
/// relations-design.md), for asserting things like "these two collections are the same fictional
/// universe" directly between two curated groupings rather than pairwise between every member
/// series. Reuses the same <see cref="RelationType"/> vocabulary - the concepts (SameUniverse,
/// Crossover, SharedCharacters, ...) apply just as well to a pair of collections as to a pair of
/// series.
///
/// No <c>Evidence</c> sub-table like <see cref="MediaRelation"/>/<see cref="RelationEvidence"/> has:
/// a Collection is a purely local, user-curated grouping with no external-provider vocabulary to
/// preserve provenance for, so every row here is inherently a single first-party user assertion -
/// the provenance-tracking machinery MediaRelation needs for AniList-sourced relations doesn't apply.
///
/// <see cref="RelationType"/> always describes <see cref="SourceCollection"/>'s own role relative to
/// <see cref="TargetCollection"/>, same directional convention as <see cref="MediaRelation"/> - see
/// <c>CollectionRelationResolver.GetRelatedCollections</c> for how a directional relation reads
/// correctly from either end without a duplicate row.
/// </summary>
public class CollectionRelation
{
    public int Id { get; set; }

    public int SourceCollectionId { get; set; }

    public Collection? SourceCollection { get; set; }

    public int TargetCollectionId { get; set; }

    public Collection? TargetCollection { get; set; }

    public RelationType RelationType { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
