namespace Paperbunkr.App.Models;

/// <summary>One collection the current series belongs to, shown as a removable chip on the Related tab (docs/superpowers/specs/2026-08-27-collections-design.md, step 10). Clone of <see cref="ContinuityChip"/>.</summary>
public sealed class CollectionChip
{
    public required int CollectionId { get; init; }
    public required string Name { get; init; }
}
