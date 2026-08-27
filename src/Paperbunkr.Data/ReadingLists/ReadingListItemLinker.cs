using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>
/// Manually relinks a reading-list row (typically one showing the "Missing" badge - a placeholder
/// created by <see cref="ReadingListMatcher.ResolveOrCreatePlaceholder"/>, or a real issue whose
/// file went missing) to a different, real issue - docs/superpowers/specs/2026-08-23-cbl-manager-
/// manual-editing-and-list-aware-reading-design.md §1. Data-layer (not arc-specific) since
/// placeholders also come from CBL/CSV import, not just <see cref="ArcReadingListBuilder"/>.
/// </summary>
public static class ReadingListItemLinker
{
    public static void Relink(PaperbunkrDbContext context, int readingListItemId, int newIssueId)
    {
        var item = context.ReadingListItems.Include(i => i.Issue).First(i => i.Id == readingListItemId);
        var oldIssue = item.Issue;

        item.IssueId = newIssueId;

        if (oldIssue is { IsPlaceholder: true })
        {
            bool referencedElsewhere = context.ReadingListItems
                .Any(i => i.IssueId == oldIssue.Id && i.Id != item.Id);
            if (!referencedElsewhere)
            {
                context.Issues.Remove(oldIssue);
            }
        }

        var list = context.ReadingLists.Find(item.ReadingListId);
        if (list is not null)
        {
            list.UpdatedAt = DateTime.UtcNow;
        }

        context.SaveChanges();
    }
}
