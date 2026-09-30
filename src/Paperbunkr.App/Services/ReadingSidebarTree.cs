using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Services;

public sealed record SidebarFolderInfo(int Id, int? ParentId, int SortOrder, bool IsCollapsed);

public sealed record SidebarListInfo(int Id, int? FolderId, int SortOrder);

/// <summary>One visible sidebar row, in order: a folder or a list, how deep it sits, and its containing folder.</summary>
public sealed record SidebarSlot(bool IsFolder, int Id, int Depth, int? ParentFolderId);

public enum SidebarDropZone
{
    Before,
    Into,
    After,
}

/// <summary>A resolved drop: put the folder or list <see cref="Id"/> into <see cref="TargetFolderId"/> (null = top level) at <see cref="Index"/>
/// among that folder's folders or lists (clamped by the write path, so <c>int.MaxValue</c> means "at the end").</summary>
public sealed record SidebarMove(bool IsFolder, int Id, int? TargetFolderId, int Index);

/// <summary>
/// The Reading Lists sidebar's folder tree, as pure functions (docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-
/// design.md §2): depth-first flattening (folders before lists, collapsed folders hide their contents), recursive list counts, and what a
/// drag-and-drop means. A folder whose parent is gone, or a cycle, is treated as top level so nothing ever disappears from the sidebar.
/// </summary>
public static class ReadingSidebarTree
{
    public static IReadOnlyList<SidebarSlot> Flatten(IReadOnlyList<SidebarFolderInfo> folders, IReadOnlyList<SidebarListInfo> lists)
    {
        var folderIds = folders.Select(f => f.Id).ToHashSet();
        var childFolders = folders
            .GroupBy(f => f.ParentId is int p && folderIds.Contains(p) && p != f.Id ? p : (int?)null)
            .ToDictionary(g => g.Key ?? -1, g => g.OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToList());
        var childLists = lists
            .GroupBy(l => l.FolderId is int f && folderIds.Contains(f) ? f : (int?)null)
            .ToDictionary(g => g.Key ?? -1, g => g.OrderBy(l => l.SortOrder).ThenBy(l => l.Id).ToList());

        var slots = new List<SidebarSlot>();
        var visited = new HashSet<int>();

        void Walk(int? parent, int depth)
        {
            foreach (var folder in childFolders.GetValueOrDefault(parent ?? -1) ?? new List<SidebarFolderInfo>())
            {
                if (!visited.Add(folder.Id))
                {
                    continue;
                }

                slots.Add(new SidebarSlot(true, folder.Id, depth, parent));
                if (!folder.IsCollapsed)
                {
                    Walk(folder.Id, depth + 1);
                }
                else
                {
                    MarkVisited(folder.Id);
                }
            }

            foreach (var list in childLists.GetValueOrDefault(parent ?? -1) ?? new List<SidebarListInfo>())
            {
                slots.Add(new SidebarSlot(false, list.Id, depth, parent));
            }
        }

        void MarkVisited(int folderId)
        {
            foreach (var child in childFolders.GetValueOrDefault(folderId) ?? new List<SidebarFolderInfo>())
            {
                if (visited.Add(child.Id))
                {
                    MarkVisited(child.Id);
                }
            }
        }

        Walk(null, 0);

        // Folders caught in a parent cycle were never reached from the top: show them (and their lists) at the top level.
        foreach (var stray in folders.Where(f => !visited.Contains(f.Id)).OrderBy(f => f.SortOrder).ThenBy(f => f.Id))
        {
            if (visited.Add(stray.Id))
            {
                slots.Add(new SidebarSlot(true, stray.Id, 0, null));
                if (!stray.IsCollapsed)
                {
                    foreach (var list in childLists.GetValueOrDefault(stray.Id) ?? new List<SidebarListInfo>())
                    {
                        slots.Add(new SidebarSlot(false, list.Id, 1, stray.Id));
                    }
                }
            }
        }

        return slots;
    }

    /// <summary>Lists anywhere under each folder.</summary>
    public static IReadOnlyDictionary<int, int> ListCounts(IReadOnlyList<SidebarFolderInfo> folders, IReadOnlyList<SidebarListInfo> lists)
    {
        var parentOf = folders.ToDictionary(f => f.Id, f => f.ParentId);
        var counts = folders.ToDictionary(f => f.Id, _ => 0);
        foreach (var list in lists)
        {
            var seen = new HashSet<int>();
            for (int? f = list.FolderId; f is int id && counts.ContainsKey(id) && seen.Add(id); f = parentOf[id])
            {
                counts[id]++;
            }
        }

        return counts;
    }

    /// <summary>
    /// What dropping <paramref name="source"/> on <paramref name="target"/> means. The middle of a folder row moves the item into that
    /// folder (a list dropped anywhere on a folder row does too, since lists always sit below folders). The top or bottom of a row of the
    /// same kind reorders before or after it, in that row's folder. A folder dropped on a list goes to the end of that list's folder's
    /// folders. Null when the drop means nothing or would put a folder inside itself.
    /// </summary>
    public static SidebarMove? ResolveDrop(SidebarSlot source, SidebarSlot target, SidebarDropZone zone, IReadOnlyList<SidebarSlot> slots)
    {
        if (source.IsFolder == target.IsFolder && source.Id == target.Id)
        {
            return null;
        }

        int? destination;
        int index;
        if (target.IsFolder && (zone == SidebarDropZone.Into || !source.IsFolder))
        {
            destination = target.Id;
            index = int.MaxValue;
        }
        else if (source.IsFolder && !target.IsFolder)
        {
            destination = target.ParentFolderId;
            index = int.MaxValue;
        }
        else
        {
            destination = target.ParentFolderId;
            var siblings = slots.Where(s => s.IsFolder == source.IsFolder && s.ParentFolderId == target.ParentFolderId
                                            && !(s.IsFolder == source.IsFolder && s.Id == source.Id)).ToList();
            index = siblings.FindIndex(s => s.Id == target.Id) + (zone == SidebarDropZone.After ? 1 : 0);
        }

        if (source.IsFolder && destination is int d && IsSelfOrDescendant(source.Id, d, slots))
        {
            return null;
        }

        return new SidebarMove(source.IsFolder, source.Id, destination, index);
    }

    private static bool IsSelfOrDescendant(int folderId, int candidate, IReadOnlyList<SidebarSlot> slots)
    {
        var parentOf = slots.Where(s => s.IsFolder).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().ParentFolderId);
        var seen = new HashSet<int>();
        for (int? f = candidate; f is int id && seen.Add(id); f = parentOf.GetValueOrDefault(id))
        {
            if (id == folderId)
            {
                return true;
            }
        }

        return false;
    }
}
