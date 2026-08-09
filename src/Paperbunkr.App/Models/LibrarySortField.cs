namespace Paperbunkr.App.Models;

/// <summary>
/// Library grid's sort keys (docs/superpowers/specs/2026-08-09-library-toolbar-design.md Phase C).
/// Series-level aggregates of real existing data - not a 1:1 port of Smart Lists' per-Issue field
/// catalog, since Library sorts series, not individual issues.
/// </summary>
public enum LibrarySortField
{
    Name,
    DateAdded,
    LastRead,
    Size,
    IssueCount,
    UnreadCount,
    Publisher,
}
