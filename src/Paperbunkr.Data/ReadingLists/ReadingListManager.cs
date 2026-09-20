using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>Outcome of <see cref="ReadingListManager.AddIssues"/>.</summary>
public sealed record AddIssuesResult(int Added, int Skipped, IReadOnlyList<int> AddedIssueIds);

/// <summary>
/// The one sanctioned path for changing a reading list's membership or order in production code
/// (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §5.4). It exists so the
/// <c>ReadingListChanged</c> plugin hook is fed from one place instead of ~10 scattered
/// <c>ReadingListItems.Add/Remove</c> sites that each have to remember to announce themselves.
/// <para>
/// <b>Caller owns <c>SaveChanges</c>.</b> Several writers (deleting an issue, refreshing an arc)
/// use a context whose save belongs to a larger unit of work, so these methods mutate the context and
/// <em>stage</em> the change via <see cref="PaperbunkrDbContext.RunAfterSave"/>; the announcement is
/// released only after that context's next successful save, and never for a failed or abandoned one.
/// One operation is one announcement - a 300-issue import is one event.
/// </para>
/// <para>
/// Nothing enforces its use: a future direct <c>ReadingListItems.Add</c> compiles fine and silently
/// skips the hook. That is a known, accepted gap (backlog item 20 - an EF interceptor or a Roslyn
/// analyzer would close it). <b>New code that changes list membership should call this class.</b>
/// Not covered on purpose: relinking a placeholder to a real issue (the membership set is unchanged),
/// and creating/renaming/deleting a whole list.
/// </para>
/// </summary>
public static class ReadingListManager
{
    /// <summary>
    /// Appends <paramref name="issueIds"/> to the list, skipping any already in it, bumps
    /// <see cref="ReadingList.UpdatedAt"/>, and stages an <see cref="ReadingListChangeKind.Added"/>
    /// announcement if anything was added. The list must already be persisted (real <c>Id</c>).
    /// </summary>
    public static AddIssuesResult AddIssues(PaperbunkrDbContext context, int listId, IEnumerable<int> issueIds, LibraryEvents? events = null)
    {
        var existing = context.ReadingListItems.Where(i => i.ReadingListId == listId).Select(i => i.IssueId).ToHashSet();
        int nextOrder = context.ReadingListItems.Where(i => i.ReadingListId == listId).Select(i => (int?)i.SortOrder).Max() is int max ? max + 1 : 0;

        var added = new List<int>();
        int skipped = 0;
        foreach (int issueId in issueIds)
        {
            if (!existing.Add(issueId))
            {
                skipped++;
                continue;
            }

            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = issueId, SortOrder = nextOrder++ });
            added.Add(issueId);
        }

        if (added.Count > 0)
        {
            Touch(context, listId);
            Stage(context, listId, ReadingListChangeKind.Added, added, Array.Empty<int>(), events);
        }

        return new AddIssuesResult(added.Count, skipped, added);
    }

    /// <summary>Removes the given items (by <see cref="ReadingListItem.Id"/>) from <paramref name="listId"/>, bumps <see cref="ReadingList.UpdatedAt"/>, and stages a <see cref="ReadingListChangeKind.Removed"/> announcement. Returns how many were removed.</summary>
    public static int RemoveItems(PaperbunkrDbContext context, int listId, IReadOnlyCollection<int> itemIds, LibraryEvents? events = null)
    {
        var items = context.ReadingListItems.Where(i => i.ReadingListId == listId && itemIds.Contains(i.Id)).ToList();
        if (items.Count == 0)
        {
            return 0;
        }

        var removedIssueIds = items.Select(i => i.IssueId).ToList();
        context.ReadingListItems.RemoveRange(items);
        Touch(context, listId);
        Stage(context, listId, ReadingListChangeKind.Removed, Array.Empty<int>(), removedIssueIds, events);
        return items.Count;
    }

    /// <summary>Swaps an item with its neighbour (<paramref name="offset"/> -1 = up, +1 = down), bumps <see cref="ReadingList.UpdatedAt"/>, and stages a <see cref="ReadingListChangeKind.Reordered"/> announcement. False if it can't move (not found / already at the edge).</summary>
    public static bool MoveItem(PaperbunkrDbContext context, int listId, int itemId, int offset, LibraryEvents? events = null)
    {
        var items = context.ReadingListItems.Where(i => i.ReadingListId == listId).OrderBy(i => i.SortOrder).ToList();
        int index = items.FindIndex(i => i.Id == itemId);
        int swapWith = index + offset;
        if (index < 0 || swapWith < 0 || swapWith >= items.Count)
        {
            return false;
        }

        (items[index].SortOrder, items[swapWith].SortOrder) = (items[swapWith].SortOrder, items[index].SortOrder);
        Touch(context, listId);
        Stage(context, listId, ReadingListChangeKind.Reordered, Array.Empty<int>(), Array.Empty<int>(), events);
        return true;
    }

    /// <summary>
    /// Removes every list item that points at <paramref name="issueId"/> - what deleting an issue from
    /// the library needs (the FK is <c>Restrict</c>, so they must go first). Stages one
    /// <see cref="ReadingListChangeKind.Removed"/> announcement <em>per affected list</em>. Deliberately
    /// does not bump <see cref="ReadingList.UpdatedAt"/> (unchanged from before this method existed).
    /// </summary>
    public static void RemoveIssueFromAllLists(PaperbunkrDbContext context, int issueId, LibraryEvents? events = null)
    {
        var items = context.ReadingListItems.Where(i => i.IssueId == issueId).ToList();
        if (items.Count == 0)
        {
            return;
        }

        context.ReadingListItems.RemoveRange(items);
        foreach (int listId in items.Select(i => i.ReadingListId).Distinct())
        {
            Stage(context, listId, ReadingListChangeKind.Removed, Array.Empty<int>(), new[] { issueId }, events);
        }
    }

    /// <summary>
    /// Announces a list that was just created together with its items by an import or a builder
    /// (CBL/CSV, an external arc, a continuity). Call it right after adding the list to the context;
    /// the list's <c>Id</c> and items are read when the save lands, so it is fine that the list isn't
    /// persisted yet. No-op for a list with no items.
    /// </summary>
    public static void RecordCreatedWithItems(PaperbunkrDbContext context, ReadingList list, ReadingListChangeKind kind = ReadingListChangeKind.Imported, LibraryEvents? events = null)
    {
        LibraryEvents hub = events ?? LibraryEvents.Default;
        context.RunAfterSave(() =>
        {
            var issueIds = list.Items.Select(i => i.IssueId).ToList();
            if (issueIds.Count == 0)
            {
                return;
            }

            hub.Raise(new ReadingListChangedEvent(list.Id, list.Name, kind, issueIds, Array.Empty<int>()));
        });
    }

    /// <summary>
    /// Low-level announce for a bespoke compound change the caller computed itself - an arc refresh
    /// that adds, removes and reorders in one pass. Prefer the specific methods above where one fits.
    /// The list's <c>Id</c> and name are read when the save lands. No-op when <paramref name="kind"/>
    /// is <see cref="ReadingListChangeKind.None"/>.
    /// </summary>
    public static void Record(PaperbunkrDbContext context, ReadingList list, ReadingListChangeKind kind, IReadOnlyList<int> addedIssueIds, IReadOnlyList<int> removedIssueIds, LibraryEvents? events = null)
    {
        if (kind == ReadingListChangeKind.None)
        {
            return;
        }

        LibraryEvents hub = events ?? LibraryEvents.Default;
        context.RunAfterSave(() => hub.Raise(new ReadingListChangedEvent(list.Id, list.Name, kind, addedIssueIds, removedIssueIds)));
    }

    private static void Touch(PaperbunkrDbContext context, int listId)
    {
        var list = context.ReadingLists.Find(listId);
        if (list is not null)
        {
            list.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static void Stage(PaperbunkrDbContext context, int listId, ReadingListChangeKind kind, IReadOnlyList<int> added, IReadOnlyList<int> removed, LibraryEvents? events)
    {
        LibraryEvents hub = events ?? LibraryEvents.Default;

        // The name is read now, while the list is certainly still tracked: by the time the save lands
        // a caller may have moved on. Falls back to an empty name if the list is unknown.
        string name = context.ReadingLists.Local.FirstOrDefault(l => l.Id == listId)?.Name
            ?? context.ReadingLists.AsNoTracking().Where(l => l.Id == listId).Select(l => l.Name).FirstOrDefault()
            ?? string.Empty;

        context.RunAfterSave(() => hub.Raise(new ReadingListChangedEvent(listId, name, kind, added, removed)));
    }
}
