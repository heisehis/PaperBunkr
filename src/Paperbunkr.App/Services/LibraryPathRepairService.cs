using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Paperbunkr.Data;

namespace Paperbunkr.App.Services;

/// <summary>
/// One-time (idempotent) repair for comics whose <c>FilePath</c> got repointed at a Windows
/// <c>ReplaceFile</c> backup (<c>&lt;name&gt;~RF&lt;hex&gt;.TMP</c>) by the metadata-write-back /
/// folder-watch bug fixed in <see cref="FileWriteBackCoordinator"/>. Those backups are deleted
/// milliseconds after they appear, so an affected issue points at nothing and shows as missing /
/// unrecognised even though the real file is right where it always was.
///
/// Runs at startup. After it succeeds no issue carries a <c>~RF*.TMP</c> path, so re-running is a
/// no-op. Only ever <b>reads</b> the disk and rewrites the DB <c>FilePath</c> back to the real
/// file - it never deletes or moves anything.
/// </summary>
public static class LibraryPathRepairService
{
    private static readonly Regex Backup =
        new(@"^(?<real>.+?)~RF[0-9a-fA-F]+\.TMP$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public readonly record struct RepairResult(int Reconnected, int NeedsManualReview)
    {
        public bool DidSomething => Reconnected > 0 || NeedsManualReview > 0;
    }

    public static RepairResult RunOnce(PaperbunkrDbContext context)
    {
        var suspects = context.Issues
            .Where(i => i.FilePath != null && i.FilePath.Contains("~RF"))
            .ToList()
            .Where(i => Backup.IsMatch(i.FilePath!))
            .ToList();

        if (suspects.Count == 0)
        {
            return default;
        }

        var takenPaths = new System.Collections.Generic.HashSet<string>(
            context.Issues.Where(i => i.FilePath != null).Select(i => i.FilePath!),
            StringComparer.OrdinalIgnoreCase);

        int reconnected = 0;
        int manual = 0;

        foreach (var issue in suspects)
        {
            string real = Backup.Match(issue.FilePath!).Groups["real"].Value;

            if (!File.Exists(real))
            {
                // The real file genuinely isn't there (write-back failed, or it was moved since) -
                // flag it so it lands in Needs Review's Missing Files instead of pointing at a
                // scratch name.
                issue.FileIsMissing = true;
                manual++;
                continue;
            }

            if (takenPaths.Contains(real))
            {
                // A rescan already re-imported the real file as a separate issue. Auto-merging would
                // mean deleting a row - leave both and let the user resolve it in Needs Review.
                manual++;
                continue;
            }

            issue.FilePath = real;
            issue.FileIsMissing = false;
            takenPaths.Add(real);
            reconnected++;
        }

        if (reconnected > 0 || manual > 0)
        {
            context.SaveChanges();
        }

        return new RepairResult(reconnected, manual);
    }
}
