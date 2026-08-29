namespace Paperbunkr.App.Models;

/// <summary>One collection related to the collection being edited, shown as a removable chip in
/// <see cref="ViewModels.CollectionPropertiesScreenViewModel"/> - the Collections analogue of how
/// <see cref="ContinuityChip"/>/<see cref="CollectionChip"/> display a membership, except this shows
/// a typed <see cref="Data.Entities.RelationType"/> relation between two collections rather than a
/// membership.</summary>
public sealed class RelatedCollectionChip
{
    public required int CollectionRelationId { get; init; }

    public required int CollectionId { get; init; }

    public required string Name { get; init; }

    public required string RelationTypeLabel { get; init; }
}
