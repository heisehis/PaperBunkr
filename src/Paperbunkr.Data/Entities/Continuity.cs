namespace Paperbunkr.Data.Entities;

/// <summary>
/// A named fictional universe/timeline ("Earth-616", "DC Prime Earth") that any number of
/// <see cref="Series"/> can belong to at once (docs/superpowers/specs/2026-08-17-metadata-model-
/// phase4a-continuity-design.md). Distinct from <see cref="MediaRelation"/>'s
/// <c>SameContinuity</c>/<c>SharedUniverse</c> types, which are pairwise assertions between two
/// specific series - this is a first-class grouping so "what else is in this continuity" is one
/// query instead of an all-pairs relation web. M:M with <see cref="Series"/> via EF Core's implicit
/// skip-navigation join table, same pattern as <see cref="Category"/> - no extra columns on the
/// join, so no explicit join entity.
/// </summary>
public class Continuity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Publisher { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<Series> Series { get; set; } = new();
}
