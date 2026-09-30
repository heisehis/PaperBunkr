using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>One child of a folder (or of the top level) in sidebar order: folders first, then lists.</summary>
public sealed record ReadingListFolderChild(bool IsFolder, int Id, string Name);

/// <summary>
/// The one write path for the Reading Lists sidebar's folder structure (docs/superpowers/specs/2026-09-28-reading-lists-organize-and-
/// track-design.md §1). Like <see cref="ReadingListManager"/>, the caller owns <c>SaveChanges</c>. Folders follow ComicRack CE's
/// <c>ComicListItemFolder</c>: nested to any depth, folders always before lists, and "Sort" orders folders then lists A-Z ignoring a
/// leading article. List <em>contents</em> never change here, so nothing is announced to plugins.
/// </summary>
public static class ReadingListFolders
{
    public static ReadingListFolder Create(PaperbunkrDbContext context, string name, int? parentFolderId)
    {
        var now = DateTime.UtcNow;
        var folder = new ReadingListFolder
        {
            Name = string.IsNullOrWhiteSpace(name) ? "New folder" : name.Trim(),
            ParentFolderId = parentFolderId,
            SortOrder = NextFolderOrder(context, parentFolderId),
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.ReadingListFolders.Add(folder);
        return folder;
    }

    public static void Rename(PaperbunkrDbContext context, int folderId, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || context.ReadingListFolders.Find(folderId) is not { } folder)
        {
            return;
        }

        folder.Name = name.Trim();
        folder.UpdatedAt = DateTime.UtcNow;
    }

    public static void SetDescription(PaperbunkrDbContext context, int folderId, string? description)
    {
        if (context.ReadingListFolders.Find(folderId) is not { } folder)
        {
            return;
        }

        folder.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        folder.UpdatedAt = DateTime.UtcNow;
    }

    public static void SetCollapsed(PaperbunkrDbContext context, int folderId, bool collapsed)
    {
        if (context.ReadingListFolders.Find(folderId) is { } folder)
        {
            folder.IsCollapsed = collapsed;
        }
    }

    /// <summary>
    /// Moves a folder under <paramref name="newParentId"/> (null = top level) at <paramref name="index"/> among that parent's folders
    /// (clamped). Returns false - and changes nothing - for a move into itself or one of its own descendants (CE's <c>RecursionTest</c>).
    /// </summary>
    public static bool Move(PaperbunkrDbContext context, int folderId, int? newParentId, int index)
    {
        if (context.ReadingListFolders.Find(folderId) is not { } folder || IsSelfOrDescendant(context, folderId, newParentId))
        {
            return false;
        }

        int? oldParentId = folder.ParentFolderId;
        var siblings = FoldersIn(context, newParentId).Where(f => f.Id != folderId).ToList();
        siblings.Insert(Math.Clamp(index, 0, siblings.Count), folder);
        folder.ParentFolderId = newParentId;
        folder.UpdatedAt = DateTime.UtcNow;
        Renumber(siblings);
        if (oldParentId != newParentId)
        {
            Renumber(FoldersIn(context, oldParentId).Where(f => f.Id != folderId).ToList());
        }

        return true;
    }

    /// <summary>Moves a list into <paramref name="folderId"/> (null = top level) at <paramref name="index"/> among that folder's lists (clamped).</summary>
    public static bool MoveList(PaperbunkrDbContext context, int listId, int? folderId, int index)
    {
        if (context.ReadingLists.Find(listId) is not { } list)
        {
            return false;
        }

        if (folderId is int id && context.ReadingListFolders.Find(id) is null)
        {
            return false;
        }

        int? oldFolderId = list.FolderId;
        var siblings = ListsIn(context, folderId).Where(l => l.Id != listId).ToList();
        siblings.Insert(Math.Clamp(index, 0, siblings.Count), list);
        list.FolderId = folderId;
        Renumber(siblings);
        if (oldFolderId != folderId)
        {
            Renumber(ListsIn(context, oldFolderId).Where(l => l.Id != listId).ToList());
        }

        return true;
    }

    /// <summary>
    /// Deletes a folder but never its contents (decision Q7): its sub-folders and lists move up to the deleted folder's parent, appended
    /// after what that parent already holds, in their current order.
    /// </summary>
    public static void Delete(PaperbunkrDbContext context, int folderId)
    {
        if (context.ReadingListFolders.Find(folderId) is not { } folder)
        {
            return;
        }

        int? parentId = folder.ParentFolderId;
        var parentFolders = FoldersIn(context, parentId).Where(f => f.Id != folderId).ToList();
        var parentLists = ListsIn(context, parentId).ToList();

        foreach (var child in FoldersIn(context, folderId))
        {
            child.ParentFolderId = parentId;
            parentFolders.Add(child);
        }

        foreach (var list in ListsIn(context, folderId))
        {
            list.FolderId = parentId;
            parentLists.Add(list);
        }

        Renumber(parentFolders);
        Renumber(parentLists);
        context.ReadingListFolders.Remove(folder);
    }

    /// <summary>CE's Sort (<c>ComicListLibraryBrowser</c>): folders A-Z, then lists A-Z, each ignoring a leading "The", "A" or "An". Not recursive.</summary>
    public static void SortAlphabetically(PaperbunkrDbContext context, int? folderId)
    {
        Renumber(FoldersIn(context, folderId).OrderBy(f => SortKey(f.Name), StringComparer.CurrentCultureIgnoreCase).ToList());
        Renumber(ListsIn(context, folderId).OrderBy(l => SortKey(l.Name), StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    /// <summary>Puts a newly created (possibly not yet saved) list at the end of <paramref name="folderId"/>'s lists (decision Q8).</summary>
    public static void PlaceNewList(PaperbunkrDbContext context, ReadingList list, int? folderId)
    {
        if (folderId is int id && context.ReadingListFolders.Find(id) is null)
        {
            folderId = null;
        }

        int? max = ListsIn(context, folderId).Where(l => !ReferenceEquals(l, list)).Select(l => (int?)l.SortOrder).Max();
        list.FolderId = folderId;
        list.SortOrder = max is int m ? m + 1 : 0;
    }

    /// <summary>A folder's (or the top level's) children in sidebar order: folders first, then lists.</summary>
    public static IReadOnlyList<ReadingListFolderChild> Children(PaperbunkrDbContext context, int? folderId) =>
        FoldersIn(context, folderId).Select(f => new ReadingListFolderChild(true, f.Id, f.Name))
            .Concat(ListsIn(context, folderId).Select(l => new ReadingListFolderChild(false, l.Id, l.Name)))
            .ToList();

    /// <summary>Every folder id at or below <paramref name="folderId"/>.</summary>
    public static IReadOnlySet<int> SelfAndDescendants(PaperbunkrDbContext context, int folderId)
    {
        var parents = context.ReadingListFolders.Select(f => new { f.Id, f.ParentFolderId }).ToList();
        var result = new HashSet<int> { folderId };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var f in parents)
            {
                if (f.ParentFolderId is int p && result.Contains(p) && result.Add(f.Id))
                {
                    grew = true;
                }
            }
        }

        return result;
    }

    /// <summary>The name with a leading English article removed, for sorting.</summary>
    public static string SortKey(string name)
    {
        foreach (var article in new[] { "The ", "A ", "An " })
        {
            if (name.StartsWith(article, StringComparison.OrdinalIgnoreCase) && name.Length > article.Length)
            {
                return name[article.Length..];
            }
        }

        return name;
    }

    private static bool IsSelfOrDescendant(PaperbunkrDbContext context, int folderId, int? candidate) =>
        candidate is int id && SelfAndDescendants(context, folderId).Contains(id);

    private static int NextFolderOrder(PaperbunkrDbContext context, int? parentId) =>
        FoldersIn(context, parentId).Select(f => (int?)f.SortOrder).Max() is int max ? max + 1 : 0;

    // Tracked entities first (Local), so a caller's unsaved moves are seen; the query only adds rows not yet loaded.
    private static List<ReadingListFolder> FoldersIn(PaperbunkrDbContext context, int? parentId)
    {
        _ = context.ReadingListFolders.Where(f => f.ParentFolderId == parentId).ToList();
        return context.ReadingListFolders.Local
            .Where(f => f.ParentFolderId == parentId && context.Entry(f).State != Microsoft.EntityFrameworkCore.EntityState.Deleted)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Id)
            .ToList();
    }

    private static List<ReadingList> ListsIn(PaperbunkrDbContext context, int? folderId)
    {
        _ = context.ReadingLists.Where(l => l.FolderId == folderId).ToList();
        return context.ReadingLists.Local
            .Where(l => l.FolderId == folderId && context.Entry(l).State != Microsoft.EntityFrameworkCore.EntityState.Deleted)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
            .ToList();
    }

    private static void Renumber(List<ReadingListFolder> folders)
    {
        for (int i = 0; i < folders.Count; i++)
        {
            folders[i].SortOrder = i;
        }
    }

    private static void Renumber(List<ReadingList> lists)
    {
        for (int i = 0; i < lists.Count; i++)
        {
            lists[i].SortOrder = i;
        }
    }
}
