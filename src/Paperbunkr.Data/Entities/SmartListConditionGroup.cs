namespace Paperbunkr.Data.Entities;

/// <summary>
/// One AND/OR node in a <see cref="SmartList"/>'s condition tree
/// (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md §2). Replaces the v1 flat
/// always-AND <c>SmartList.Conditions</c> list. Ported from CE's <c>ComicBookGroupMatcher</c>
/// (<c>_reference/ComicRackCE/ComicRack.Engine/ComicBookGroupMatcher.cs</c>), which likewise
/// carries a <c>MatcherMode</c> plus arbitrary nesting.
///
/// A group is either a <see cref="SmartList"/>'s root (<see cref="SmartListId"/> set,
/// <see cref="ParentGroupId"/> null) or a nested child (<see cref="ParentGroupId"/> set,
/// <see cref="SmartListId"/> null). <see cref="SmartListQueryBuilder"/> evaluates a group by
/// combining its own <see cref="Conditions"/> (each XOR'd with its <see cref="SmartListCondition.Not"/>
/// flag) and its <see cref="ChildGroups"/> (recursively) with <see cref="Mode"/>.
/// </summary>
public class SmartListConditionGroup
{
    public int Id { get; set; }

    /// <summary>Set only for a <see cref="SmartList"/>'s single root group; null for every nested group.</summary>
    public int? SmartListId { get; set; }

    public SmartList? SmartList { get; set; }

    /// <summary>Self-referencing FK — null for a root group, otherwise the enclosing group.</summary>
    public int? ParentGroupId { get; set; }

    public SmartListConditionGroup? ParentGroup { get; set; }

    public SmartListGroupMode Mode { get; set; } = SmartListGroupMode.And;

    public int SortOrder { get; set; }

    public List<SmartListCondition> Conditions { get; set; } = new();

    public List<SmartListConditionGroup> ChildGroups { get; set; } = new();
}
