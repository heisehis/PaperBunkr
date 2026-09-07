using System;
using System.Linq;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Correctly deletes an <see cref="Issue"/> or <see cref="Series"/>, including the cross-references
/// a naive <c>context.Issues.Remove(issue)</c> can't touch (docs/superpowers/specs/2026-08-22-
/// delete-functionality-design.md) - <c>ReadingListItem.Issue</c> and <c>EventMembership.Issue</c>
/// are both <c>DeleteBehavior.Restrict</c> (confirmed in <c>PaperbunkrDbContext.OnModelCreating</c>,
/// deliberately, so deleting one issue can't silently cascade into an unrelated reading list or
/// event), which means removing an Issue that's still referenced by either throws a
/// <c>DbUpdateException</c> unless those references are removed first. Found as a real latent bug in
/// <c>NeedsReviewViewModel.RemoveMissingFile</c> (the app's original destructive-delete, predating
/// this helper) while building this - it never handled either reference, so it would have thrown
/// for any missing-file issue that also happened to be in a reading list or event. Fixed there too,
/// not left as a second, differently-buggy delete path.
///
/// Also the single choke point every removal path in the app already shares (Library card delete,
/// series delete, Needs Review's duplicate-resolve, Smart List grouped-resolve, the plugin API's
/// RemoveBook, and Library Health's own Remove/bulk-remove) - which is why the removed-files
/// blacklist (docs/superpowers/specs/2026-09-06-scan-missing-file-handling-design.md) is recorded
/// here rather than at each call site individually.
/// </summary>
public static class LibraryDeletionHelper
{
    /// <summary>Removes an Issue: its cross-references, records its path in the removed-files blacklist, its file (to the Recycle Bin, best-effort), then the row itself. Caller owns <c>SaveChanges</c>.</summary>
    public static void RemoveIssue(PaperbunkrDbContext context, Issue issue)
    {
        var listItems = context.ReadingListItems.Where(i => i.IssueId == issue.Id);
        context.ReadingListItems.RemoveRange(listItems);

        var memberships = context.EventMemberships.Where(m => m.IssueId == issue.Id);
        context.EventMemberships.RemoveRange(memberships);

        RecordRemovedFilePath(context, issue.FilePath);

        if (!issue.FileIsMissing)
        {
            RecycleBinHelper.SendToRecycleBin(issue.FilePath);
        }

        context.Issues.Remove(issue);
        CoverImageCache.Invalidate(issue.Id);
    }

    /// <summary>
    /// Upserts a permanent <see cref="RemovedFilePath"/> row for the given path - unconditional,
    /// regardless of <see cref="AppSettings.DontReimportRemovedFiles"/> (that setting only gates
    /// whether <c>LibraryFolderScanner</c> consults this table, not whether it's populated). No-op
    /// for a null/placeholder path - a fileless entry has nothing to blacklist. A repeat removal of
    /// the same path just bumps <see cref="RemovedFilePath.RemovedAtUtc"/> rather than duplicating
    /// the row.
    /// </summary>
    private static void RecordRemovedFilePath(PaperbunkrDbContext context, string? filePath)
    {
        if (filePath is null)
        {
            return;
        }

        var existing = context.RemovedFilePaths.FirstOrDefault(r => r.FilePath == filePath);
        if (existing is not null)
        {
            existing.RemovedAtUtc = DateTime.UtcNow;
        }
        else
        {
            context.RemovedFilePaths.Add(new RemovedFilePath { FilePath = filePath, RemovedAtUtc = DateTime.UtcNow });
        }
    }

    /// <summary>Removes every Issue in a Series (see <see cref="RemoveIssue"/>), then the Series itself. Caller owns <c>SaveChanges</c>.</summary>
    public static void RemoveSeries(PaperbunkrDbContext context, Series series)
    {
        foreach (var issue in series.Issues.ToList())
        {
            RemoveIssue(context, issue);
        }

        context.Series.Remove(series);
    }
}
