namespace Paperbunkr.App.Models;

/// <summary>One candidate collection shown while searching/creating (docs/superpowers/specs/2026-08-27-collections-design.md, step 10). <see cref="IsNew"/> distinguishes a "create new" row from an existing-name match in the picker. Clone of <see cref="ContinuitySearchResult"/>.</summary>
public sealed class CollectionSearchResult
{
    public int CollectionId { get; init; }
    public required string Name { get; init; }
    public bool IsNew { get; init; }
}
