using System.Linq;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Relinks a missing library entry to the entry that now holds its file (docs/superpowers/specs/2026-10-06-smart-features-design.md
/// §5.1). The old entry is the one kept - it carries the read history, ratings, list memberships and edits - and the newer duplicate
/// entry is removed. The file itself is never touched.
/// </summary>
public static class MissingFileRelinker
{
    /// <summary>
    /// Points <paramref name="missingIssueId"/> at <paramref name="candidateIssueId"/>'s file, clears its missing flags the way a manual
    /// relink does, and removes the candidate entry. False (nothing changed) if either entry is gone or the candidate has no file.
    /// </summary>
    public static bool RelinkToCandidate(PaperbunkrDbContext context, int missingIssueId, int candidateIssueId)
    {
        if (missingIssueId == candidateIssueId)
        {
            return false;
        }

        var missing = context.Issues.FirstOrDefault(i => i.Id == missingIssueId);
        var candidate = context.Issues.FirstOrDefault(i => i.Id == candidateIssueId);
        if (missing is null || candidate is null || string.IsNullOrEmpty(candidate.FilePath))
        {
            return false;
        }

        missing.FilePath = candidate.FilePath;
        missing.FileSize = candidate.FileSize ?? missing.FileSize;
        missing.PageCount = candidate.PageCount ?? missing.PageCount;
        missing.FileModifiedTime = candidate.FileModifiedTime ?? missing.FileModifiedTime;
        missing.FileCreationTime = candidate.FileCreationTime ?? missing.FileCreationTime;
        missing.FileIsMissing = false;
        missing.IsPlaceholder = false;
        missing.MissingVerificationCount = 0;
        missing.MissingAcknowledged = false;

        // If the re-imported copy was read further than the old entry, that reading happened to this same issue: keep it.
        if ((candidate.LastPageRead ?? 0) > (missing.LastPageRead ?? 0))
        {
            missing.LastPageRead = candidate.LastPageRead;
        }

        if (candidate.OpenedTime is { } opened && (missing.OpenedTime is null || opened > missing.OpenedTime))
        {
            missing.OpenedTime = opened;
        }

        missing.OpenCount += candidate.OpenCount;

        // Saved first, so the removal below sees the file as still used by another entry: it then leaves the file where it is and does
        // not add its path to the "do not re-import" list (which would hide the relinked entry's own file from the next scan).
        context.SaveChanges();

        LibraryDeletionHelper.RemoveIssue(context, candidate, deleteFile: false);
        context.SaveChanges();

        CoverImageCache.Invalidate(missing.Id);
        return true;
    }
}
