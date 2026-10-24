using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Timer = System.Timers.Timer;

namespace Paperbunkr.App.Services;

/// <summary>
/// Debounced, serial background pipeline for file metadata write-back
/// (docs/superpowers/specs/2026-09-03-file-metadata-write-back-design.md). Owned by
/// <c>MainViewModel</c> like <see cref="LiveFolderWatchService"/>. Trigger sites call
/// <see cref="Enqueue"/> fire-and-forget after their own <c>SaveChanges()</c> - the database write
/// is already the source of truth, the file write is best-effort.
///
/// Coalesces by issue id (a second <see cref="Enqueue"/> for a pending id just resets its debounce),
/// then processes one file at a time (7-Zip spawns a process per call; concurrent archive writes
/// invite corruption). Settings are re-read at flush time: nothing writes unless
/// <see cref="Data.Entities.AppSettings.WriteMetadataToFiles"/>; a non-<c>manual</c> item also needs
/// <see cref="Data.Entities.AppSettings.WriteMetadataAutomatically"/>. Outcomes over one flush are
/// summarised into a single toast.
///
/// Manga chapters (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §10) add three things on top. A write that fails is retried
/// after 5 s, 30 s and 5 min, then the chapter is marked as needing attention (it stays dirty, so the next launch tries again). A batch of
/// <see cref="ProgressJobThreshold"/> or more files is shown in the Activity Center while it runs, with progress. A <c>manual</c> item is forced: it is written even if the
/// file looks current, was changed outside Paperbunkr, or its series has sync switched off.
/// </summary>
public class MetadataWriteBackQueue : IDisposable
{
    /// <summary>Batches this big get a live progress row in the Activity Center instead of a single summary at the end.</summary>
    public const int ProgressJobThreshold = 20;

    private static readonly TimeSpan[] DefaultRetryDelays = { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5) };

    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly MetadataFileWriteBackService _service;
    private readonly Action<string, string> _showToast;
    private readonly IActivityService _activity;
    private readonly TimeSpan _debounceWindow;
    private readonly TimeSpan[] _retryDelays;

    private readonly object _pendingLock = new();
    private readonly Dictionary<int, bool> _pending = new();
    private readonly Dictionary<int, int> _attempts = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly Timer _debounceTimer;
    private bool _disposed;

    public MetadataWriteBackQueue(Action<string, string> showToast, IActivityService? activity = null)
        : this(PaperbunkrDb.CreateContext, new MetadataFileWriteBackService(), showToast, TimeSpan.FromMilliseconds(300), activity)
    {
    }

    /// <summary>Test seam - shorter debounce and retry delays, injectable context/service.</summary>
    internal MetadataWriteBackQueue(
        Func<PaperbunkrDbContext> contextFactory,
        MetadataFileWriteBackService service,
        Action<string, string> showToast,
        TimeSpan debounceWindow,
        IActivityService? activity = null,
        IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        _contextFactory = contextFactory;
        _service = service;
        _showToast = showToast;
        _activity = activity ?? new ActivityService();
        _debounceWindow = debounceWindow;
        _retryDelays = retryDelays?.ToArray() ?? DefaultRetryDelays;

        _debounceTimer = new Timer(_debounceWindow.TotalMilliseconds) { AutoReset = false };
        _debounceTimer.Elapsed += OnDebounceElapsed;
    }

    /// <summary>
    /// Queue a write for <paramref name="issueId"/>. <paramref name="manual"/> = the user asked for
    /// it explicitly ("Write metadata to files" action) - bypasses the automatic-mode gate and any
    /// caller-side "did anything change" check, and forces the write of a manga chapter.
    /// </summary>
    public void Enqueue(int issueId, bool manual = false)
    {
        if (_disposed)
        {
            return;
        }

        lock (_pendingLock)
        {
            _pending[issueId] = _pending.TryGetValue(issueId, out bool existing) && existing || manual;
        }

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public void Enqueue(IEnumerable<int> issueIds, bool manual = false)
    {
        foreach (int id in issueIds)
        {
            Enqueue(id, manual);
        }
    }

    /// <summary>
    /// Startup catch-up (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §10): queues every chapter that still owes a write,
    /// that is, <see cref="Issue.EmbeddedDirty"/> in a series whose sync is on. Never "everything": a chapter nobody edited is not dirty. Returns how many were queued.
    /// </summary>
    public int EnqueueDirty()
    {
        List<int> ids;
        using (var context = _contextFactory())
        {
            ids = context.Issues.AsNoTracking()
                .Where(i => i.EmbeddedDirty && i.Series!.EmbeddedSyncEnabled && i.EmbeddedSyncState != EmbeddedSyncState.UnsupportedFormat && i.EmbeddedSyncState != EmbeddedSyncState.SkippedMissingFile)
                .Select(i => i.Id)
                .ToList();
        }

        Enqueue(ids);
        return ids.Count;
    }

    private void OnDebounceElapsed(object? sender, ElapsedEventArgs e) => _ = FlushAsync();

    /// <summary>
    /// Writes whatever is pending now and completes when that (and any write already running) is done - "Scrape and organize" waits on it so
    /// the organizer never moves a comic its new metadata is still being written into (docs/superpowers/specs/2026-10-21-scraper-organizer-c-automation-ux-design.md section 7).
    /// </summary>
    public Task FlushNowAsync() => DrainNowAsync();

    /// <summary>Test hook - runs the pending batch now without waiting on the debounce timer.</summary>
    internal Task DrainNowAsync()
    {
        _debounceTimer.Stop();
        return FlushAsync();
    }

    private async Task FlushAsync()
    {
        Dictionary<int, bool> batch;
        lock (_pendingLock)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            batch = new Dictionary<int, bool>(_pending);
            _pending.Clear();
        }

        await _flushGate.WaitAsync().ConfigureAwait(false);
        IActivityJobHandle? progressJob = null;
        try
        {
            bool masterEnabled;
            bool automaticEnabled;
            bool includeSidecar;
            bool includeMetronInfo;
            int writers;
            using (var context = _contextFactory())
            {
                var settings = context.GetOrCreateAppSettings();
                masterEnabled = settings.WriteMetadataToFiles;
                automaticEnabled = settings.WriteMetadataAutomatically;
                includeSidecar = settings.WriteNativeSidecar;
                includeMetronInfo = settings.WriteMetronInfo;

                // docs/superpowers/specs/2026-10-21-scraper-organizer-b-speed-core-design.md section 7: different files at once, by the Speed setting.
                writers = Paperbunkr.Data.ComicVine.Scraping.ScrapeSettings.Load(context).Speed switch
                {
                    Paperbunkr.Data.ComicVine.Scraping.ScrapeSpeed.Careful => 1,
                    Paperbunkr.Data.ComicVine.Scraping.ScrapeSpeed.Fast => 3,
                    _ => 2,
                };
            }

            if (!masterEnabled)
            {
                return;
            }

            var tally = new BatchTally();
            int done = 0;
            int eligible = batch.Count(kv => kv.Value || automaticEnabled);
            if (eligible >= ProgressJobThreshold)
            {
                progressJob = _activity.StartJob(ActivityJobKind.SyncMetadata, "Embedding metadata in files");
                progressJob.Report(0, eligible);
            }

            var tallyGate = new object();
            using var slots = new SemaphoreSlim(writers, writers);
            var running = new List<Task>();
            foreach (var (issueId, manual) in batch)
            {
                if (!manual && !automaticEnabled)
                {
                    continue;
                }

                if (progressJob?.CancellationToken.IsCancellationRequested == true)
                {
                    // Stopped from the Activity Center: put the rest back as still owed (they are dirty in the database, so the next launch also finds them).
                    break;
                }

                await slots.WaitAsync().ConfigureAwait(false);
                bool single = batch.Count == 1 && !manual;
                running.Add(Task.Run(async () =>
                {
                    try
                    {
                        // Each write opens its own database context, so writes of different files can overlap; the batch never holds one id twice.
                        var outcome = await _service.WriteAsync(issueId, includeSidecar, includeMetronInfo, force: manual).ConfigureAwait(false);
                        lock (tallyGate)
                        {
                            tally.Add(outcome, single);
                            HandleRetry(issueId, outcome);
                            if (outcome.Result is MetadataWriteBackResult.SkippedExternalModification or MetadataWriteBackResult.Failed)
                            {
                                tally.NoteAttention(issueId);
                            }

                            progressJob?.Report(++done, eligible, outcome.FileName);
                        }
                    }
                    finally
                    {
                        slots.Release();
                    }
                }));
            }

            await Task.WhenAll(running).ConfigureAwait(false);

            ReportBatch(tally, progressJob);
            progressJob = null;
            ScheduleLeftoverSweep(batch.Keys);
        }
        finally
        {
            progressJob?.Dispose();
            _flushGate.Release();
        }
    }

    /// <summary>
    /// After a batch, sweeps the written comics' folders for swap copies once they are old enough to be sure no write still owns them
    /// (docs/superpowers/specs/2026-10-21-scraper-organizer-a-reliability-design.md). Best effort, off the flush path.
    /// </summary>
    private void ScheduleLeftoverSweep(IEnumerable<int> issueIds)
    {
        var ids = issueIds.ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Paperbunkr.Data.Library.SwapFileSweeper.MinimumAge + TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                List<string> paths;
                using (var context = _contextFactory())
                {
                    paths = context.Issues.AsNoTracking().Where(i => ids.Contains(i.Id) && i.FilePath != null).Select(i => i.FilePath!).ToList();
                }

                Paperbunkr.Data.Library.SwapFileSweeper.SweepBeside(paths);
            }
            catch (Exception)
            {
                // The startup sweep and Library Health catch anything this misses.
            }
        });
    }

    /// <summary>A failed write is tried again after the next delay, up to <c>_retryDelays.Length</c> times, then marked as needing attention. Any other outcome ends the streak.</summary>
    private void HandleRetry(int issueId, MetadataWriteBackOutcome outcome)
    {
        if (outcome.Result != MetadataWriteBackResult.Failed)
        {
            lock (_pendingLock)
            {
                _attempts.Remove(issueId);
            }

            return;
        }

        int attempt;
        lock (_pendingLock)
        {
            _attempts[issueId] = attempt = _attempts.GetValueOrDefault(issueId) + 1;
        }

        if (attempt > _retryDelays.Length)
        {
            lock (_pendingLock)
            {
                _attempts.Remove(issueId);
            }

            _service.MarkNeedsAttention(issueId, outcome.ErrorMessage);
            return;
        }

        TimeSpan delay = _retryDelays[attempt - 1];
        _ = Task.Delay(delay).ContinueWith(_ => Enqueue(issueId), TaskScheduler.Default);
    }

    /// <summary>What one flush did, kept together so the summary reads the same whichever way it is shown.</summary>
    private sealed class BatchTally
    {
        public int Wrote;
        public int AlreadyCurrent;
        public int Unwritable;
        public int Failed;
        public int WithoutMetronInfo;
        public int ChangedOutside;
        public List<string> SkippedFormat { get; } = new();
        public string? LastFailureMessage;
        public string? SingleSkipToast;
        public HashSet<int> AttentionIssueIds { get; } = new();

        public void NoteAttention(int issueId) => AttentionIssueIds.Add(issueId);

        public void Add(MetadataWriteBackOutcome outcome, bool singleAutomaticItem)
        {
            switch (outcome.Result)
            {
                case MetadataWriteBackResult.Success:
                    Wrote++;
                    break;
                case MetadataWriteBackResult.SuccessWithoutMetronInfo:
                    Wrote++;
                    WithoutMetronInfo++;
                    LastFailureMessage = outcome.ErrorMessage;
                    break;
                case MetadataWriteBackResult.SkippedUnsupportedFormat:
                    SkippedFormat.Add(outcome.FileName ?? "a file");
                    if (singleAutomaticItem)
                    {
                        SingleSkipToast = $"{outcome.FileName} can't be updated (not a CBZ/CB7/CBT), so its metadata is in the library only.";
                    }

                    break;
                case MetadataWriteBackResult.SkippedMissingFile:
                case MetadataWriteBackResult.SkippedReadOnly:
                    Unwritable++;
                    break;
                case MetadataWriteBackResult.SkippedAlreadyCurrent:
                case MetadataWriteBackResult.SkippedSyncDisabled:
                    AlreadyCurrent++;
                    break;
                case MetadataWriteBackResult.SkippedExternalModification:
                    ChangedOutside++;
                    break;
                case MetadataWriteBackResult.Failed:
                    Failed++;
                    LastFailureMessage = outcome.ErrorMessage;
                    break;
            }
        }

        public bool NothingToSay => Wrote == 0 && SkippedFormat.Count == 0 && Unwritable == 0 && Failed == 0 && ChangedOutside == 0 && AlreadyCurrent == 0;
    }

    private void ReportBatch(BatchTally tally, IActivityJobHandle? progressJob)
    {
        if (tally.SingleSkipToast is not null)
        {
            progressJob?.Dispose();
            _showToast("Saved to library only", tally.SingleSkipToast);
            return;
        }

        // A true no-op, and a lone "already up to date", say nothing: no toast and no row.
        if (tally.NothingToSay || (tally.Wrote == 0 && tally.SkippedFormat.Count == 0 && tally.Unwritable == 0 && tally.Failed == 0 && tally.ChangedOutside == 0 && progressJob is null))
        {
            progressJob?.Dispose();
            return;
        }

        var parts = new List<string>();
        if (tally.Wrote > 0)
        {
            parts.Add($"{tally.Wrote} file{(tally.Wrote == 1 ? "" : "s")} updated");
        }

        if (tally.AlreadyCurrent > 0)
        {
            parts.Add($"{tally.AlreadyCurrent} already up to date");
        }

        if (tally.SkippedFormat.Count > 0)
        {
            parts.Add($"{tally.SkippedFormat.Count} skipped (unsupported format)");
        }

        if (tally.Unwritable > 0)
        {
            // Used to be silent: a missing or read-only file produced no notice at all, which looked
            // exactly like the write never having run.
            parts.Add($"{tally.Unwritable} not written (file missing or read-only)");
        }

        if (tally.ChangedOutside > 0)
        {
            parts.Add($"{tally.ChangedOutside} changed outside Paperbunkr, left alone");
        }

        if (tally.Failed > 0)
        {
            parts.Add($"{tally.Failed} failed, will retry");
        }

        if (tally.WithoutMetronInfo > 0)
        {
            parts.Add($"{tally.WithoutMetronInfo} without MetronInfo.xml");
        }

        string message = string.Join(" · ", parts) + ".";
        if ((tally.Failed > 0 || tally.WithoutMetronInfo > 0) && tally.LastFailureMessage is not null)
        {
            message += $" Last error: {tally.LastFailureMessage}";
        }

        bool problem = tally.Failed > 0 || tally.WithoutMetronInfo > 0 || tally.ChangedOutside > 0;
        var link = AttentionLink(tally);

        // Job-tracked (docs/superpowers/specs/2026-09-06-feedback-notification-system-design.md
        // §6) - created here, only once the batch's real outcome is known, not before the loop
        // above ran. That's deliberate: the single-skip toast just above and the true-no-op early
        // return before it must keep their own toast behavior exactly as today, and
        // IActivityJobHandle's ToastPolicy is fixed at StartJob() - there's no single policy that
        // covers all three paths. Resolving it here (lazily, once this is provably the "general
        // summary" outcome) sidesteps that entirely instead of forcing one policy on all three.
        // A big batch already has its live row; that one is finished instead of starting a second.
        var job = progressJob ?? _activity.StartJob(ActivityJobKind.SyncMetadata, "Writing metadata to files");
        using (job)
        {
            if (problem)
            {
                job.Fail(message, link);
            }
            else
            {
                job.Succeed(message, link);
            }
        }
    }

    /// <summary>Open the series when everything that needs attention belongs to one (the manga screen's Edit Series, Advanced section, has Retry and Write anyway).</summary>
    private ActivityLink? AttentionLink(BatchTally tally)
    {
        if (tally.AttentionIssueIds.Count == 0)
        {
            return null;
        }

        try
        {
            using var context = _contextFactory();
            var seriesIds = context.Issues.AsNoTracking().Where(i => tally.AttentionIssueIds.Contains(i.Id)).Select(i => i.SeriesId).Distinct().Take(2).ToList();
            return seriesIds.Count == 1 ? new ActivityLink(ActivityLinkKind.SeriesDetail, seriesIds[0].ToString()) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _debounceTimer.Elapsed -= OnDebounceElapsed;
        _debounceTimer.Dispose();
        _flushGate.Dispose();
    }
}
