namespace Paperbunkr.App.Models;

/// <summary>One continuity the current series belongs to, shown as a removable chip on the Related tab (docs/superpowers/specs/2026-08-17-metadata-model-phase4a-continuity-design.md).</summary>
public sealed class ContinuityChip
{
    public required int ContinuityId { get; init; }
    public required string Name { get; init; }
}
