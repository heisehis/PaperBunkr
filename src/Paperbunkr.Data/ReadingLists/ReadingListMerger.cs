using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>What <see cref="ReadingListMerger.Merge"/> did.</summary>
public sealed record ReadingListMergeResult(int AddedCount, int FilledCount, bool DeletedOther);

/// <summary>
/// Merges one reading list into another (docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-design.md §7, decision
/// Q16). The target keeps its order; each issue only the other list has goes in right after the last issue both lists share that
/// precedes it in the other list (at the top when none does). The other list's notes, role and group label fill the target's
/// blanks and never overwrite them. The caller owns <c>SaveChanges</c>; the change is announced once.
/// </summary>
public static class ReadingListMerger
{
    /// <summary>How many issues <see cref="Merge"/> would add - for the confirm dialog.</summary>
    public static int CountToAdd(PaperbunkrDbContext context, int intoListId, int fromListId)
    {
        var into = context.ReadingListItems.Where(i => i.ReadingListId == intoListId).Select(i => i.IssueId).ToHashSet();
        return context.ReadingListItems.Where(i => i.ReadingListId == fromListId).Select(i => i.IssueId).Distinct().AsEnumerable().Count(id => !into.Contains(id));
    }

    public static ReadingListMergeResult Merge(PaperbunkrDbContext context, int intoListId, int fromListId, bool deleteOther, LibraryEvents? events = null)
    {
        if (intoListId == fromListId)
        {
            throw new InvalidOperationException("A list can't be merged into itself.");
        }

        var into = context.ReadingLists.Include(r => r.Items).Include(r => r.Tags).First(r => r.Id == intoListId);
        var from = context.ReadingLists.Include(r => r.Items).Include(r => r.Tags).First(r => r.Id == fromListId);
        context.MarkReadingListManaged(into);

        var ordered = into.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToList();
        var byIssue = new Dictionary<int, ReadingListItem>();
        foreach (var item in ordered)
        {
            byIssue.TryAdd(item.IssueId, item);
        }

        var added = new List<int>();
        int filled = 0;
        ReadingListItem? anchor = null;          // the last shared item seen so far, walking the other list in order
        int insertedAfterAnchor = 0;
        foreach (var source in from.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id))
        {
            if (byIssue.TryGetValue(source.IssueId, out var existing))
            {
                if (FillBlanks(existing, source))
                {
                    filled++;
                }

                anchor = existing;
                insertedAfterAnchor = 0;
                continue;
            }

            var item = new ReadingListItem
            {
                ReadingListId = into.Id,
                IssueId = source.IssueId,
                GroupLabel = source.GroupLabel,
                Notes = source.Notes,
                Role = source.Role,
                RoleSource = source.RoleSource,
                RoleReason = source.RoleReason,
            };
            int at = anchor is null ? insertedAfterAnchor : ordered.IndexOf(anchor) + 1 + insertedAfterAnchor;
            ordered.Insert(at, item);
            insertedAfterAnchor++;
            byIssue[source.IssueId] = item;
            into.Items.Add(item);
            added.Add(source.IssueId);
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].SortOrder = i;
        }

        foreach (var tag in from.Tags)
        {
            if (!into.Tags.Any(t => string.Equals(t.Value, tag.Value, StringComparison.OrdinalIgnoreCase)))
            {
                into.Tags.Add(new ReadingListTag { Value = tag.Value, Category = tag.Category, Weight = tag.Weight });
            }
        }

        into.UpdatedAt = DateTime.UtcNow;
        ReadingListManager.Record(context, into, added.Count > 0 ? ReadingListChangeKind.Added | ReadingListChangeKind.Reordered : ReadingListChangeKind.None,
            added, Array.Empty<int>(), events);

        if (deleteOther)
        {
            context.MarkReadingListManaged(from);
            context.ReadingLists.Remove(from);
        }

        return new ReadingListMergeResult(added.Count, filled, deleteOther);
    }

    private static bool FillBlanks(ReadingListItem target, ReadingListItem source)
    {
        bool changed = false;
        if (string.IsNullOrWhiteSpace(target.Notes) && !string.IsNullOrWhiteSpace(source.Notes))
        {
            target.Notes = source.Notes;
            changed = true;
        }

        if (target.Role is null && source.Role is not null)
        {
            target.Role = source.Role;
            target.RoleSource = source.RoleSource;
            target.RoleReason = source.RoleReason;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(target.GroupLabel) && !string.IsNullOrWhiteSpace(source.GroupLabel))
        {
            target.GroupLabel = source.GroupLabel;
            changed = true;
        }

        return changed;
    }
}
