namespace Paperbunkr.Data.Entities;

/// <summary>
/// A saved rule-based issue filter — new entity (docs/superpowers/specs/2026-08-06-smart-lists-design.md).
/// Ported concept from CE's <c>ComicSmartListItem</c>/matcher system, but as a real persisted
/// entity rather than an in-memory matcher-object graph.
///
/// Conditions live in a nested AND/OR tree rooted at <see cref="RootGroup"/>
/// (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md §2) — the v1 flat always-AND
/// <c>Conditions</c> list is gone. A list with a single <see cref="SmartListGroupMode.And"/> root
/// group holding a flat condition list is exactly the pre-v2 semantics, which is what the v2
/// migration produces for every existing list.
/// </summary>
public class SmartList
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>True for seeded built-ins (My Favorites, Read, Missing Files, ...) — not user-deletable, conditions render read-only.</summary>
    public bool IsSystem { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// Root of the condition tree — every list has exactly one, <see cref="SmartListGroupMode.And"/>
    /// by default. Load the full tree via <see cref="SmartLists.SmartListTreeLoader"/> before
    /// handing the list to <see cref="SmartLists.SmartListQueryBuilder"/>.
    /// </summary>
    public SmartListConditionGroup RootGroup { get; set; } = new();
}
