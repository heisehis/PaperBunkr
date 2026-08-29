using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.SmartLists;

/// <summary>
/// Loads a <see cref="SmartList"/>'s full nested condition tree
/// (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md §2). EF Core can't express an
/// arbitrary-depth self-referencing <c>Include</c>, so the tree is walked explicitly: two cheap
/// collection loads per group (its <see cref="SmartListConditionGroup.Conditions"/> and its
/// <see cref="SmartListConditionGroup.ChildGroups"/>), recursing into each child. For the common
/// single-group list that's exactly two queries.
/// </summary>
internal static class SmartListTreeLoader
{
    /// <summary>Loads the list by id with its <see cref="SmartList.RootGroup"/> and the whole tree beneath it, or null if no such list.</summary>
    public static SmartList? LoadWithTree(PaperbunkrDbContext ctx, int smartListId)
    {
        var list = ctx.SmartLists
            .Include(s => s.RootGroup)
            .FirstOrDefault(s => s.Id == smartListId);
        if (list is null)
        {
            return null;
        }

        LoadGroup(ctx, list.RootGroup);
        return list;
    }

    /// <summary>Loads <paramref name="rootGroup"/>'s conditions and descendant groups. Use for a detached/in-memory list already holding its root group.</summary>
    public static void LoadTree(PaperbunkrDbContext ctx, SmartListConditionGroup rootGroup) => LoadGroup(ctx, rootGroup);

    private static void LoadGroup(PaperbunkrDbContext ctx, SmartListConditionGroup group)
    {
        ctx.Entry(group).Collection(g => g.Conditions).Load();
        ctx.Entry(group).Collection(g => g.ChildGroups).Load();
        foreach (var child in group.ChildGroups)
        {
            LoadGroup(ctx, child);
        }
    }
}
