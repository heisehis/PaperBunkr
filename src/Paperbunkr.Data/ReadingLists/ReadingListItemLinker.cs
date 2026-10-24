using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
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
    /// <param name="method">How the new link was decided, recorded on the entry's source record when it has one. The default is the
    /// user's own pick (Find &amp; link), which no automatic pass ever revisits
    /// (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §4).</param>
    public static void Relink(PaperbunkrDbContext context, int readingListItemId, int newIssueId, MatchMethod method = MatchMethod.User)
    {
        var item = context.ReadingListItems.Include(i => i.Issue).Include(i => i.Source).First(i => i.Id == readingListItemId);
        var oldIssue = Repoint(context, item, newIssueId, method);

        // Persist the new link first. Deleting the old placeholder in this same SaveChanges could
        // make an unrelated restrictive FK (including one added by a later feature) roll back the
        // user's link along with the cleanup.
        context.SaveChanges();

        if (oldIssue is { IsPlaceholder: true })
        {
            TryDeleteUnreferencedPlaceholder(context, oldIssue.Id, exceptReadingListItemId: item.Id);
        }
    }

    /// <summary>
    /// The relink itself, without saving and without touching the old issue: the item keeps its place, role and notes, and its source
    /// record keeps what the source asked for - only how it is linked changes. Returns the issue the item pointed at before. The item must
    /// be loaded with <c>Issue</c> and <c>Source</c>.
    /// </summary>
    internal static Issue? Repoint(PaperbunkrDbContext context, ReadingListItem item, int newIssueId, MatchMethod method)
    {
        var oldIssue = item.Issue;
        item.Issue = null;
        item.IssueId = newIssueId;

        if (item.Source is { } source)
        {
            source.Method = method;
            source.State = MatchState.Linked;
            source.LastCheckedUtc = DateTime.UtcNow;
        }

        var list = context.ReadingLists.Find(item.ReadingListId);
        if (list is not null)
        {
            list.UpdatedAt = DateTime.UtcNow;
        }

        return oldIssue;
    }

    /// <summary>
    /// Deletes a placeholder only when no relationship with Restrict delete behavior still points to it.
    /// A list row may be the last reading-list reference while a Story Event membership or a series cover
    /// still depends on the same issue.
    /// </summary>
    internal static bool DeleteIfUnreferencedPlaceholder(PaperbunkrDbContext context, int issueId, int? exceptReadingListItemId = null)
    {
        bool referencedByList = context.ReadingListItems.Any(i => i.IssueId == issueId
            && (exceptReadingListItemId == null || i.Id != exceptReadingListItemId.Value));
        if (referencedByList
            || context.EventMemberships.Any(m => m.IssueId == issueId)
            || context.Series.Any(s => s.CoverIssueId == issueId))
        {
            return false;
        }

        var issue = context.Issues.FirstOrDefault(i => i.Id == issueId && i.IsPlaceholder);
        if (issue is null)
        {
            return false;
        }

        context.Issues.Remove(issue);
        return true;
    }

    private static void TryDeleteUnreferencedPlaceholder(PaperbunkrDbContext context, int issueId, int exceptReadingListItemId)
    {
        if (!DeleteIfUnreferencedPlaceholder(context, issueId, exceptReadingListItemId))
        {
            return;
        }

        try
        {
            context.SaveChanges();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            // The link is already committed. If the live DB has another FK reference that this
            // version does not know about, keep the placeholder instead of surfacing an error or
            // retrying the link. SaveChanges rolls back its failed statement/transaction; restore
            // the tracked state so this context remains usable.
            var issue = context.ChangeTracker.Entries<Issue>()
                .FirstOrDefault(e => e.Entity.Id == issueId && e.State == EntityState.Deleted);
            if (issue is not null)
            {
                issue.State = EntityState.Unchanged;
            }
        }
    }
}
