using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>Result of a completed <see cref="LibraryHealthService"/> Verify pass.</summary>
public record LibraryHealthVerifyResult(int Checked, int MissingNow, int ConfirmedMissingCount);

/// <summary>
/// Preferences → Library Health's Verify pass (docs/superpowers/specs/2026-09-06-missing-files-
/// library-health-design.md) - the actual gap that design closes: no code path anywhere did a live
/// <c>File.Exists</c> sweep over the whole library. <see cref="LibraryFolderScanner.ScanAllAsync"/>
/// only discovers new files; <c>SyncMetadata</c>/<c>ResyncSeriesFromFile</c> silently skip a missing
/// file with no <c>else</c> branch. Reuses <see cref="Issue.FileIsMissing"/> as the single source of
/// truth (sets and clears it directly, no parallel flag) and tracks
/// <see cref="Issue.MissingVerificationCount"/> for the two-strikes bulk-cleanup grace window. Same
/// "Task.Run + IProgress + one-bad-file-doesn't-stop-batch" contract as
/// <see cref="LibraryFolderScanner.SyncMetadataAsync"/>.
/// </summary>
public class LibraryHealthService
{
    /// <summary>Consecutive missing Verify passes before an issue is eligible for the bulk "Remove
    /// All Confirmed Missing" action. User-configurable (docs/superpowers/specs/2026-09-07-library-
    /// health-redesign-design.md §7) - was a hardcoded constant, now read from
    /// <see cref="Data.Entities.AppSettings.LibraryHealthConfirmedMissingThreshold"/> by the caller.</summary>
    public int ConfirmedMissingThreshold { get; set; } = 2;

    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public LibraryHealthService(Func<PaperbunkrDbContext>? contextFactory = null)
    {
        _contextFactory = contextFactory ?? PaperbunkrDb.CreateContext;
    }

    /// <summary>Full-library sweep - every non-placeholder issue with a FilePath, regardless of source folder or <c>WatchedFolder.Watch</c>.</summary>
    public async Task<LibraryHealthVerifyResult> VerifyAsync(IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
    {
        return await Task.Run(() => Verify(progress, ct, issueIds: null), ct);
    }

    /// <summary>
    /// Scoped sweep over exactly the given issue ids - used by <c>PreferencesScreenViewModel.RemoveFolder</c>
    /// so a removed folder's issues are checked and correctly flagged/counted immediately instead of
    /// going silently stale until someone happens to run a full Verify.
    /// </summary>
    public async Task<LibraryHealthVerifyResult> VerifyAsync(IReadOnlyCollection<int> issueIds, IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
    {
        return await Task.Run(() => Verify(progress, ct, issueIds), ct);
    }

    private LibraryHealthVerifyResult Verify(IProgress<(int Done, int Total)> progress, CancellationToken ct, IReadOnlyCollection<int>? issueIds)
    {
        using var context = _contextFactory();

        var query = context.Issues.Where(i => !i.IsPlaceholder && i.FilePath != null);
        if (issueIds is not null)
        {
            query = query.Where(i => issueIds.Contains(i.Id));
        }

        var issues = query.ToList();
        int total = issues.Count;
        int done = 0;
        progress.Report((0, total));

        int missingNow = 0;
        int confirmedMissing = 0;

        foreach (var issue in issues)
        {
            ct.ThrowIfCancellationRequested();

            bool exists = File.Exists(issue.FilePath);
            issue.FileIsMissing = !exists;
            issue.MissingVerificationCount = exists ? 0 : issue.MissingVerificationCount + 1;

            if (!exists)
            {
                missingNow++;
                if (issue.MissingVerificationCount >= ConfirmedMissingThreshold && !issue.MissingAcknowledged)
                {
                    confirmedMissing++;
                }
            }

            progress.Report((++done, total));
        }

        context.SaveChanges();
        return new LibraryHealthVerifyResult(total, missingNow, confirmedMissing);
    }

    /// <summary>
    /// CE parity for <c>driveChecker.IsConnected</c> (docs/superpowers/specs/2026-09-06-scan-
    /// missing-file-handling-design.md) - true when the path's drive/root is currently reachable,
    /// false for an unplugged drive letter or an unmounted network share. Only consulted by the
    /// unattended auto-remove-on-scan path: two consecutive Verify passes while a drive is simply
    /// disconnected would otherwise hit the two-strikes threshold and, with no human reviewing the
    /// list the way the manual "Remove All Confirmed Missing" button is, silently delete real
    /// library entries for a drive that's just not plugged in. Null/empty path is treated as
    /// unreachable (nothing to remove safely).
    /// </summary>
    public static bool IsPathRootReachable(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        try
        {
            string? root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && Directory.Exists(root);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
