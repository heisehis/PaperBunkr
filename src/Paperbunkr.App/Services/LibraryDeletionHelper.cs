using System;
using System.Linq;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;

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
    /// <param name="deleteFile">False keeps the file on disk: only the library entry goes, and its path is remembered so a rescan does not bring it back.</param>
    public static void RemoveIssue(PaperbunkrDbContext context, Issue issue, bool deleteFile = true)
    {
        // Through ReadingListManager so the ReadingListChanged plugin hook hears about the removal
        // (one Removed event per affected list, released after the caller's SaveChanges).
        ReadingListManager.RemoveIssueFromAllLists(context, issue.Id);

        var memberships = context.EventMemberships.Where(m => m.IssueId == issue.Id);
        context.EventMemberships.RemoveRange(memberships);

        // Two library entries can point at ONE file (an organize that raced the folder watcher, a re-scan after a restore...). Removing one
        // must then leave the file alone - it is still the other entry's file - and must not add its path to the "do not re-import" list,
        // which would keep the surviving entry's own file from ever being imported again. Only the entry that is the last to reference a
        // file takes the file (to the Recycle Bin) with it.
        if (!IsFileStillUsedByAnotherEntry(context, issue))
        {
            RecordRemovedFilePath(context, issue.FilePath, keepFile: !deleteFile);

            if (deleteFile && !issue.FileIsMissing)
            {
                RecycleBinHelper.SendToRecycleBin(issue.FilePath);
            }
        }

        // Its page notes and clip rows go with the issue (cascade); the clip PNGs are files, so they are removed here (best effort).
        DeleteClipFiles(context, issue.Id);

        context.Issues.Remove(issue);
        CoverImageCache.Invalidate(issue.Id);
    }

    private static void DeleteClipFiles(PaperbunkrDbContext context, int issueId)
    {
        foreach (string path in context.PageClips.Where(c => c.IssueId == issueId).Select(c => c.ImagePath).ToList())
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
            catch (System.IO.IOException)
            {
                // A clip PNG that cannot be deleted right now is not worth failing the removal of the issue for.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>True when another entry that is not itself being removed (in this same save) has the same file path.</summary>
    internal static bool IsFileStillUsedByAnotherEntry(PaperbunkrDbContext context, Issue issue)
    {
        if (string.IsNullOrEmpty(issue.FilePath))
        {
            return false;
        }

        string path = issue.FilePath;
        return context.Issues
            .Where(i => i.Id != issue.Id && i.FilePath == path)
            .AsEnumerable()
            .Any(other => context.Entry(other).State != Microsoft.EntityFrameworkCore.EntityState.Deleted);
    }

    /// <summary>
    /// Upserts a permanent <see cref="RemovedFilePath"/> row for the given path - unconditional,
    /// regardless of <see cref="AppSettings.DontReimportRemovedFiles"/> (that setting only gates
    /// whether <c>LibraryFolderScanner</c> consults this table, not whether it's populated). No-op
    /// for a null/placeholder path - a fileless entry has nothing to blacklist. A repeat removal of
    /// the same path just bumps <see cref="RemovedFilePath.RemovedAtUtc"/> rather than duplicating
    /// the row.
    /// </summary>
    private static void RecordRemovedFilePath(PaperbunkrDbContext context, string? filePath, bool keepFile = false)
    {
        if (filePath is null)
        {
            return;
        }

        var existing = context.RemovedFilePaths.FirstOrDefault(r => r.FilePath == filePath);
        if (existing is not null)
        {
            existing.RemovedAtUtc = DateTime.UtcNow;
            existing.KeepFile |= keepFile;
        }
        else
        {
            context.RemovedFilePaths.Add(new RemovedFilePath { FilePath = filePath, RemovedAtUtc = DateTime.UtcNow, KeepFile = keepFile });
        }
    }

    /// <summary>Removes every Issue in a Series (see <see cref="RemoveIssue"/>), then the Series itself. Caller owns <c>SaveChanges</c>.</summary>
    public static void RemoveSeries(PaperbunkrDbContext context, Series series, bool deleteFile = true)
    {
        foreach (var issue in series.Issues.ToList())
        {
            RemoveIssue(context, issue, deleteFile);
        }

        context.Series.Remove(series);
    }
}
