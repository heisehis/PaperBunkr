using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;

namespace Paperbunkr.App.Services;

/// <summary>Result of a completed <see cref="LibraryHealthService"/> Verify pass.</summary>
public record LibraryHealthVerifyResult(int Checked, int MissingNow, int ConfirmedMissingCount, int ContentEmptyNow);

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
    private readonly LibraryEvents _events;

    /// <param name="events">Where a newly-confirmed-missing file is announced (the <c>MissingFileDetected</c> plugin hook); defaults to <see cref="LibraryEvents.Default"/>, tests pass their own.</param>
    public LibraryHealthService(Func<PaperbunkrDbContext>? contextFactory = null, LibraryEvents? events = null)
    {
        _contextFactory = contextFactory ?? PaperbunkrDb.CreateContext;
        _events = events ?? LibraryEvents.Default;
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

    /// <summary>Issues read, checked and saved per round trip. Internal so a test can force several pages with a handful of rows.</summary>
    internal int PageSize { get; set; } = SweepPaging.DefaultPageSize;

    private static IQueryable<Issue> Candidates(PaperbunkrDbContext context, IReadOnlyCollection<int>? issueIds)
    {
        var query = context.Issues.Where(i => !i.IsPlaceholder && i.FilePath != null);
        return issueIds is null ? query : query.Where(i => issueIds.Contains(i.Id));
    }

    /// <summary>
    /// Works through the library a page at a time (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md §4.4): each
    /// page is read by id cursor into its own context, checked, saved and let go, so the sweep never holds every issue of a
    /// 5,000-comic library as a tracked entity at once. A pass that is cancelled keeps the pages it already saved; each issue's
    /// own result is complete and correct either way.
    /// </summary>
    private LibraryHealthVerifyResult Verify(IProgress<(int Done, int Total)> progress, CancellationToken ct, IReadOnlyCollection<int>? issueIds)
    {
        int total;
        using (var counting = _contextFactory())
        {
            total = Candidates(counting, issueIds).Count();
        }

        int done = 0;
        progress.Report((0, total));

        int missingNow = 0;
        int confirmedMissing = 0;
        int contentEmptyNow = 0;
        int lastId = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var newlyConfirmed = new List<MissingFileConfirmedEvent>();
            int pageCount;
            using (var context = _contextFactory())
            {
                int cursor = lastId;
                var issues = Candidates(context, issueIds).Where(i => i.Id > cursor).OrderBy(i => i.Id).Take(PageSize).ToList();
                pageCount = issues.Count;
                if (pageCount == 0)
                {
                    break;
                }

                foreach (var issue in issues)
                {
                    ct.ThrowIfCancellationRequested();
                    VerifyOne(issue, newlyConfirmed, ref missingNow, ref confirmedMissing, ref contentEmptyNow);
                    progress.Report((++done, total));
                }

                lastId = issues[^1].Id;
                context.SaveChanges();
            }

            // Only after the counts are durably saved - a failed save must not announce anything.
            foreach (var confirmed in newlyConfirmed)
            {
                _events.Raise(confirmed);
            }

            if (pageCount < PageSize)
            {
                break;
            }

            SweepPaging.PauseBetweenPages(ct);
        }

        // Same one-episode rule for an empty series: once it has issues again, its dismissal no longer
        // applies. Full passes only - a scoped pass (one removed folder) isn't a statement about every series.
        if (issueIds is null)
        {
            using var context = _contextFactory();
            foreach (var series in context.Series.Where(s => s.EmptyRowAcknowledged && s.Issues.Any()))
            {
                series.EmptyRowAcknowledged = false;
            }

            context.SaveChanges();
        }

        return new LibraryHealthVerifyResult(done, missingNow, confirmedMissing, contentEmptyNow);
    }

    private void VerifyOne(Issue issue, List<MissingFileConfirmedEvent> newlyConfirmed, ref int missingNow, ref int confirmedMissing, ref int contentEmptyNow)
    {
        int previousCount = issue.MissingVerificationCount;
        bool exists = File.Exists(issue.FilePath);
        issue.FileIsMissing = !exists;
        issue.MissingVerificationCount = exists ? 0 : issue.MissingVerificationCount + 1;

        // A dismissal covers one missing episode (docs/superpowers/specs/2026-09-28-library-health-
        // dismissed-rows-design.md): once the file is back, forget it, so a later disappearance
        // shows up in Missing Files again instead of staying silently hidden.
        if (exists)
        {
            issue.MissingAcknowledged = false;
        }

        if (!exists)
        {
            missingNow++;
            if (issue.MissingVerificationCount >= ConfirmedMissingThreshold && !issue.MissingAcknowledged)
            {
                confirmedMissing++;

                // MissingFileDetected plugin hook (docs/superpowers/specs/2026-09-20-plugin-api-4-1-
                // design.md §5.3): announced only on the pass where the count CROSSES the threshold,
                // not on every later pass while the file stays missing (confirmedMissing above counts
                // every eligible item each pass, which is right for the summary but would spam a
                // plugin). A single failed check never announces - a disconnected drive would be noisy.
                if (previousCount < ConfirmedMissingThreshold)
                {
                    newlyConfirmed.Add(new MissingFileConfirmedEvent(
                        ReadingItemType.Comic,
                        issue.Id,
                        issue.FilePath!,
                        issue.Title ?? Path.GetFileNameWithoutExtension(issue.FilePath!)));
                }
            }

            // Empty Rows (docs/superpowers/specs/2026-09-17-series-name-matching-and-empty-row-
            // cleanup-design.md) is a distinct concept from "missing" - a missing file has
            // nothing to probe, so IsContentEmpty stays false and the row surfaces in Missing
            // Files, not Empty Rows.
            issue.IsContentEmpty = false;
        }
        else
        {
            // Cheap probe: PageDecodeCore.TryOpenProvider already returns null for an unopenable
            // file or one that opens with zero pages - a corrupt/empty archive. Only opens the
            // archive header, doesn't decode any image bytes.
            var provider = PageDecodeCore.TryOpenProvider(issue.FilePath!);
            provider?.Dispose();
            issue.IsContentEmpty = provider is null;
            if (issue.IsContentEmpty)
            {
                contentEmptyNow++;
            }
            else
            {
                issue.EmptyRowAcknowledged = false;
            }
        }

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
