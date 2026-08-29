namespace Paperbunkr.Data.Entities;

/// <summary>
/// How a <see cref="SmartListConditionGroup"/> combines its own conditions and child groups
/// (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md §2). Mirrors CE's
/// <c>MatcherMode</c> (<c>_reference/ComicRackCE/ComicRack.Engine/ComicBookGroupMatcher.cs</c>) -
/// <see cref="And"/> = every member must match, <see cref="Or"/> = any member matching is enough.
/// Stored as its string name, same enum-as-string convention as every other enum in
/// <c>PaperbunkrDbContext.OnModelCreating</c>.
/// </summary>
public enum SmartListGroupMode
{
    And,
    Or,
}
