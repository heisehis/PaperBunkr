using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>What <see cref="ReadingListReconciler.Reconcile"/> changed.</summary>
public sealed record ReconcileResult(
    IReadOnlyList<int> AddedIssueIds,
    IReadOnlyList<int> RemovedIssueIds,
    int MovedCount,
    int ReplacedPlaceholderCount,
    IReadOnlyDictionary<int, ReadingListItem> ItemsByIssue)
{
    public bool Reordered => MovedCount > 0;

    public ReadingListChangeKind Kind =>
        (AddedIssueIds.Count > 0 ? ReadingListChangeKind.Added : ReadingListChangeKind.None)
        | (RemovedIssueIds.Count > 0 ? ReadingListChangeKind.Removed : ReadingListChangeKind.None)
        | (Reordered ? ReadingListChangeKind.Reordered : ReadingListChangeKind.None);
}

/// <summary>
/// Brings a reading list in line with a freshly computed order - shared by an arc list's Refresh (docs/superpowers/specs/2026-08-22-
/// cbl-manager-arc-lookup-design.md §4) and a continuity list's Rebuild (docs/superpowers/specs/2026-09-28-reading-lists-build-from-
/// events-design.md §2). Items in the new order move to their position, new issues are added, items no longer in it are removed and
/// a placeholder issue nothing else references is deleted. <c>Role</c> and <c>Notes</c> are never touched; <c>GroupLabel</c> only
/// when the caller supplies labels (a generated order owns them). The caller announces the result and owns <c>SaveChanges</c>.
/// The list must be loaded with <c>Items.Issue</c>.
/// </summary>
internal static class ReadingListReconciler
{
    /// <param name="order">Issue ids in the new order; a repeated id keeps its first position.</param>
    /// <param name="labels">Group label per position of <paramref name="order"/>, or null to leave labels alone.</param>
    public static ReconcileResult Reconcile(PaperbunkrDbContext context, ReadingList list, IReadOnlyList<int> order, IReadOnlyList<string?>? labels = null)
    {
        context.MarkReadingListManaged(list);

        // Defensive cleanup: a prior bug (or race) could have left more than one item pointing at the same issue within this list,
        // which would crash the dictionary below. Keep the oldest (lowest Id), remove the rest.
        foreach (var duplicateGroup in list.Items.GroupBy(i => i.IssueId).Where(g => g.Count() > 1).ToList())
        {
            foreach (var extra in duplicateGroup.OrderBy(i => i.Id).Skip(1).ToList())
            {
                context.ReadingListItems.Remove(extra);
                list.Items.Remove(extra);
            }
        }

        var oldByIssueId = list.Items.ToDictionary(i => i.IssueId);
        var itemsByIssue = new Dictionary<int, ReadingListItem>();
        var added = new List<int>();
        int moved = 0;
        int sortOrder = 0;

        for (int k = 0; k < order.Count; k++)
        {
            int issueId = order[k];
            if (itemsByIssue.ContainsKey(issueId))
            {
                continue;
            }

            string? label = labels is null ? null : labels[k];
            if (oldByIssueId.TryGetValue(issueId, out var existing))
            {
                if (existing.SortOrder != sortOrder)
                {
                    moved++;
                }

                existing.SortOrder = sortOrder++;
                if (labels is not null)
                {
                    existing.GroupLabel = label;
                }

                itemsByIssue[issueId] = existing;
            }
            else
            {
                var item = new ReadingListItem { ReadingListId = list.Id, IssueId = issueId, SortOrder = sortOrder++, GroupLabel = label };
                context.ReadingListItems.Add(item);
                itemsByIssue[issueId] = item;
                added.Add(issueId);
            }
        }

        var removed = new List<int>();
        int replacedPlaceholders = 0;
        foreach (var oldItem in oldByIssueId.Values.Where(i => !itemsByIssue.ContainsKey(i.IssueId)).ToList())
        {
            var orphanedIssue = oldItem.Issue;
            int oldItemId = oldItem.Id;
            context.ReadingListItems.Remove(oldItem);
            removed.Add(oldItem.IssueId);

            if (orphanedIssue is { IsPlaceholder: true })
            {
                replacedPlaceholders++;
                bool referencedElsewhere = context.ReadingListItems.Any(i => i.IssueId == orphanedIssue.Id && i.Id != oldItemId);
                if (!referencedElsewhere)
                {
                    context.Issues.Remove(orphanedIssue);
                }
            }
        }

        list.UpdatedAt = DateTime.UtcNow;
        return new ReconcileResult(added, removed, moved, replacedPlaceholders, itemsByIssue);
    }
}
