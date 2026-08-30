namespace Paperbunkr.App.Models;

/// <summary>One series related to the collection being edited, shown as a removable chip in
/// <see cref="ViewModels.CollectionPropertiesScreenViewModel"/> - the Series-node analogue of
/// <see cref="RelatedCollectionChip"/> (docs/superpowers/specs/2026-08-30-media-relation-
/// collection-nodes-design.md), backed by a mixed <see cref="Data.Entities.MediaRelation"/> edge
/// rather than a <see cref="Data.Entities.CollectionRelation"/>.</summary>
public sealed class RelatedSeriesChip
{
    public required int MediaRelationId { get; init; }

    public required int SeriesId { get; init; }

    public required string Name { get; init; }

    public required string RelationTypeLabel { get; init; }
}
