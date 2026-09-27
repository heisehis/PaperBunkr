namespace Paperbunkr.Data.Entities;

/// <summary>
/// A first-class named team, mirroring <see cref="Character"/> exactly (docs/superpowers/specs/
/// 2026-09-23-metron-api-utilization-design.md). Auto-materialized from the free-text
/// <see cref="Issue.Teams"/> ComicInfo field by <c>TeamResolver</c>; that string field stays the
/// editable source of truth (this is a derived index over it).
/// </summary>
public class Team
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<TeamAppearance> Appearances { get; set; } = new();
}
