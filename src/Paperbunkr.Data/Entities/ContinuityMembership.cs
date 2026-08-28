namespace Paperbunkr.Data.Entities;

/// <summary>
/// Explicit join row for the <see cref="Continuity"/> ↔ <see cref="Series"/> many-to-many
/// (docs/superpowers/specs/2026-08-28-continuity-editing-design.md, Part C). Replaces the implicit
/// skip-navigation join EF used before, so a membership can carry its own metadata: a free-text
/// <see cref="Note"/> ("flagship title", "spin-off") and a deliberate <see cref="SortOrder"/> for
/// the continuity's member grid. One row per (continuity, series) pair - enforced by a unique
/// index.
/// </summary>
public class ContinuityMembership
{
    public int Id { get; set; }

    public int ContinuityId { get; set; }

    public Continuity Continuity { get; set; } = null!;

    public int SeriesId { get; set; }

    public Series Series { get; set; } = null!;

    /// <summary>Free-text label for this series within the continuity. Null when unset.</summary>
    public string? Note { get; set; }

    /// <summary>Deliberate position in the continuity's member grid. New members append (max + 1).</summary>
    public int SortOrder { get; set; }
}
