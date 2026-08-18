namespace Paperbunkr.App.Models;

/// <summary>One candidate continuity shown while searching/creating (docs/superpowers/specs/2026-08-17-metadata-model-phase4a-continuity-design.md). <see cref="IsNew"/> distinguishes a "create new" row from an existing-name match in the picker.</summary>
public sealed class ContinuitySearchResult
{
    public int ContinuityId { get; init; }
    public required string Name { get; init; }
    public bool IsNew { get; init; }
}
