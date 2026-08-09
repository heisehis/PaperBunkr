namespace Paperbunkr.Data.Entities;

/// <summary>
/// A saved rule-based issue filter — new entity (docs/superpowers/specs/2026-08-06-smart-lists-design.md).
/// Ported concept from CE's <c>ComicSmartListItem</c>/matcher system, but as a real persisted
/// entity rather than an in-memory matcher-object graph. Conditions are always AND-ed (v1 —
/// matches the wireframe, which only shows "AND" pills, no OR/grouping).
/// </summary>
public class SmartList
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>True for seeded built-ins (My Favorites, Read, Missing Files, ...) — not user-deletable, conditions render read-only.</summary>
    public bool IsSystem { get; set; }

    public int SortOrder { get; set; }

    public List<SmartListCondition> Conditions { get; set; } = new();
}
