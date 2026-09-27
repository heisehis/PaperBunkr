using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Library;

/// <summary>
/// Fills <see cref="Issue.FileSize"/>/<see cref="Issue.FileModifiedTime"/>/<see cref="Issue.FileCreationTime"/>
/// from the file on disk - CE parity with <c>ComicBook.RefreshFileProperties</c>, which reads
/// <c>FileInfo.Length</c>/<c>LastWriteTimeUtc</c>/<c>CreationTimeUtc</c>. Nothing populated these before
/// 2026-09-26 (library audit), so the status bar read "0 MB" and every File Size/Modified/Created
/// sort, column and Smart List rule saw empty values.
/// </summary>
public static class IssueFileStats
{
    /// <summary>Reads the file's size and UTC timestamps into <paramref name="issue"/>. Returns false (and leaves the
    /// issue untouched) when it has no path or the file isn't reachable - a missing file keeps its last known values.</summary>
    public static bool TryApply(Issue issue)
    {
        if (string.IsNullOrWhiteSpace(issue.FilePath))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(issue.FilePath);
            if (!info.Exists)
            {
                return false;
            }

            issue.FileSize = info.Length;
            issue.FileModifiedTime = info.LastWriteTimeUtc;
            issue.FileCreationTime = info.CreationTimeUtc;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>One-time backfill for issues catalogued before these fields were populated: every issue with a path and
    /// no <see cref="Issue.FileSize"/> yet. Idempotent - a no-op once every reachable file has been read. Returns how many
    /// issues it filled.</summary>
    public static int BackfillMissing(PaperbunkrDbContext context, CancellationToken ct = default)
    {
        var issues = context.Issues
            .Where(i => i.FileSize == null && i.FilePath != null && i.FilePath != "")
            .ToList();

        int filled = 0;
        foreach (var issue in issues)
        {
            ct.ThrowIfCancellationRequested();
            if (TryApply(issue))
            {
                filled++;
            }
        }

        if (filled > 0)
        {
            context.SaveChanges();
        }

        return filled;
    }
}
