using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Tracking;

namespace Paperbunkr.App.Services;

/// <summary>
/// Orchestrates the automatic tracker push/pull paths (docs/superpowers/specs/2026-09-18-tracker-
/// behavior-settings-design.md §3.2-3.7, §4). Every automatic request runs through one serial
/// executor with per-service minimum spacing (in-memory pacing only - the persisted retry queue is
/// deferred to the roadmap backlog), every batch is one Activity Center <c>TrackerFetch</c> job
/// (status-bar indicator + history), and results surface as toasts and deduped per-series/service
/// alerts. Jobs start with <see cref="ActivityToastPolicy.Never"/> because whether anything is worth
/// a toast ("nothing to do" is silent) is only known afterwards, so this service raises its own.
/// Auto-push never sets <c>UpdateScore</c>/<c>UpdateFinishDate</c> - it can't clobber a rating or date.
/// </summary>
public sealed class TrackerAutoSyncService : ITrackerAutoSyncService
{
    /// <summary>Per-link pull throttle, persisted in <see cref="TrackingLink.LastSyncedAt"/>.</summary>
    public static readonly TimeSpan PullThrottle = TimeSpan.FromMinutes(10);

    private readonly IActivityService _activity;
    private readonly IToastHost _toasts;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Func<TrackingService, ITrackerAdapter?> _adapterFactory;
    private readonly Func<PaperbunkrDbContext, TrackingService, bool> _isConnected;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<TimeSpan, Task> _delay;

    // Serial executor: one automatic request stream at a time, across all batches.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<TrackingService, DateTime> _lastRequestUtc = new();
    private readonly object _lock = new();
    private readonly HashSet<int> _inFlight = new();
    private readonly HashSet<int> _dirty = new();

    public TrackerAutoSyncService(
        IActivityService activity,
        IToastHost toasts,
        Func<PaperbunkrDbContext>? contextFactory = null,
        Func<TrackingService, ITrackerAdapter?>? adapterFactory = null,
        Func<PaperbunkrDbContext, TrackingService, bool>? isConnected = null,
        Func<DateTime>? utcNow = null,
        Func<TimeSpan, Task>? delay = null)
    {
        _activity = activity;
        _toasts = toasts;
        _contextFactory = contextFactory ?? PaperbunkrDb.CreateContext;
        _adapterFactory = adapterFactory ?? TrackerAdapterFactory.CreateAdapter;
        _isConnected = isConnected ?? TrackerAdapterFactory.IsConnected;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Minimum gap between two requests to the same service. AniList's documented limit is
    /// 90 requests/minute, so ~700 ms; a conservative 1 s for the rest.</summary>
    public static TimeSpan MinSpacing(TrackingService service) =>
        service == TrackingService.AniList ? TimeSpan.FromMilliseconds(700) : TimeSpan.FromSeconds(1);

    // ---- Hooks ----

    public async Task OnIssueFinishedInReaderAsync(int seriesId)
    {
        try
        {
            if (!ReadSettings().TrackerUpdateAfterReading)
            {
                return;
            }

            await PushAsync(new[] { seriesId }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RaiseUnexpectedFailure(ex);
        }
    }

    public async Task OnIssuesMarkedReadAsync(IReadOnlyCollection<int> seriesIds)
    {
        try
        {
            if (seriesIds.Count == 0)
            {
                return;
            }

            switch (ReadSettings().TrackerUpdateOnMarkRead)
            {
                case TrackerAutoUpdateMode.Never:
                    return;
                case TrackerAutoUpdateMode.Ask:
                    ShowAskPrompt(seriesIds);
                    return;
                default:
                    await PushAsync(seriesIds).ConfigureAwait(false);
                    return;
            }
        }
        catch (Exception ex)
        {
            RaiseUnexpectedFailure(ex);
        }
    }

    // ---- Ask prompt ----

    private void ShowAskPrompt(IReadOnlyCollection<int> seriesIds)
    {
        using var context = _contextFactory();
        var candidates = LoadPushCandidates(context, seriesIds);
        if (candidates.Count == 0)
        {
            return;
        }

        string message;
        if (candidates.Count == 1)
        {
            int? progress = TrackerProgressCalculator.ComputeChapterProgress(candidates[0].Issues);
            message = progress is int chapter ? $"Update trackers to chapter {chapter}?" : "Update trackers?";
        }
        else
        {
            var names = candidates.Select(c => c.Name).Take(3).ToList();
            string extra = candidates.Count > 3 ? $", +{candidates.Count - 3} more" : string.Empty;
            message = $"Update trackers for {candidates.Count} series? ({string.Join(", ", names)}{extra})";
        }

        var ids = candidates.Select(c => c.Id).ToList();
        ToastRequest? prompt = null;
        var update = new RelayCommand(() =>
        {
            _toasts.Close(prompt!);
            _ = PushAsync(ids);
        });
        var dismiss = new RelayCommand(() => _toasts.Close(prompt!));
        prompt = new ToastRequest("Update trackers?", message, ToastSeverity.Info,
            new[] { new ToastAction("Update trackers", update), new ToastAction("Dismiss", dismiss) });
        _toasts.Show(prompt);
    }

    // ---- Push ----

    /// <summary>Pushes the given series to every connected linked tracker (forward-only). Same-series
    /// requests are coalesced: one already in flight marks it dirty and re-runs once afterwards.</summary>
    public async Task PushAsync(IReadOnlyCollection<int> seriesIds)
    {
        var batch = new List<int>();
        lock (_lock)
        {
            foreach (int id in seriesIds.Distinct())
            {
                if (_inFlight.Contains(id))
                {
                    _dirty.Add(id);
                }
                else
                {
                    _inFlight.Add(id);
                    batch.Add(id);
                }
            }
        }

        while (batch.Count > 0)
        {
            try
            {
                await RunPushBatchAsync(batch).ConfigureAwait(false);
            }
            finally
            {
                var rerun = new List<int>();
                lock (_lock)
                {
                    foreach (int id in batch)
                    {
                        _inFlight.Remove(id);
                        if (_dirty.Remove(id))
                        {
                            _inFlight.Add(id);
                            rerun.Add(id);
                        }
                    }
                }

                batch = rerun;
            }
        }
    }

    private sealed record PushCandidate(int Id, string Name, List<Issue> Issues, ReadingStatus Status, List<TrackingLink> Links);

    private List<PushCandidate> LoadPushCandidates(PaperbunkrDbContext context, IEnumerable<int> seriesIds)
    {
        var result = new List<PushCandidate>();
        foreach (var series in context.Series.Include(s => s.Issues).Include(s => s.TrackingLinks).Where(s => seriesIds.Contains(s.Id)).ToList())
        {
            var links = series.TrackingLinks.Where(l => _isConnected(context, l.Service) && _adapterFactory(l.Service) is not null).ToList();
            if (links.Count > 0)
            {
                result.Add(new PushCandidate(series.Id, series.Name, series.Issues.ToList(), series.ReadingStatus, links));
            }
        }

        return result;
    }

    private async Task RunPushBatchAsync(List<int> seriesIds)
    {
        using var probe = _contextFactory();
        if (LoadPushCandidates(probe, seriesIds).Count == 0)
        {
            return; // nothing linked+connected: no job, no history noise
        }

        using var job = _activity.StartJob(ActivityJobKind.TrackerFetch, "Updating trackers", cancellable: false, toastPolicy: ActivityToastPolicy.Never);
        var pushed = new SortedSet<string>();
        var failures = new List<(int SeriesId, string SeriesName, TrackingService Service, string Error)>();
        int? lastChapter = null;
        int done = 0;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (int seriesId in seriesIds)
            {
                job.Report(done++, seriesIds.Count);
                using var context = _contextFactory();
                var series = context.Series.Include(s => s.Issues).Include(s => s.TrackingLinks).FirstOrDefault(s => s.Id == seriesId);
                if (series is null)
                {
                    continue;
                }

                int? chapter = TrackerProgressCalculator.ComputeChapterProgress(series.Issues);
                var landed = new List<TrackingService>();
                foreach (var link in series.TrackingLinks.ToList())
                {
                    var adapter = _adapterFactory(link.Service);
                    if (adapter is null || !_isConnected(context, link.Service))
                    {
                        continue;
                    }

                    await PaceAsync(link.Service).ConfigureAwait(false);
                    var remote = await adapter.GetEntryAsync(context, link, CancellationToken.None).ConfigureAwait(false);
                    if (remote is not null && TrackerSyncResolver.RemoteWins(chapter, series.ReadingStatus, remote))
                    {
                        continue; // remote already ahead: forward-only, and pulling is a separate setting
                    }

                    await PaceAsync(link.Service).ConfigureAwait(false);
                    var payload = new TrackerPushPayload(series.ReadingStatus, chapter);
                    var (ok, error) = await TrackerAdapterFactory.PushDetailedAsync(adapter, context, link, payload, CancellationToken.None).ConfigureAwait(false);
                    if (ok)
                    {
                        link.LastSyncedAt = _utcNow();
                        landed.Add(link.Service);
                        pushed.Add(link.Service.ToString());
                        lastChapter = chapter;
                    }
                    else
                    {
                        failures.Add((series.Id, series.Name, link.Service, error ?? "Unknown error"));
                    }
                }

                if (landed.Count > 0)
                {
                    SeriesActivityLog.Record(context, series.Id, SeriesActivityEventKind.TrackerSynced,
                        $"Synced to {string.Join(", ", landed)}" + (chapter is int c ? $" (chapter {c})." : "."));
                }

                context.SaveChanges();
            }
        }
        finally
        {
            _gate.Release();
        }

        Settle(job, pushed, failures, seriesIds.Count, lastChapter);
    }

    private void Settle(IActivityJobHandle job, SortedSet<string> pushed, List<(int SeriesId, string SeriesName, TrackingService Service, string Error)> failures, int seriesCount, int? lastChapter)
    {
        foreach (var f in failures)
        {
            _activity.RaiseAlert(new ActivityAlert
            {
                Severity = ActivityAlertSeverity.Warning,
                Title = $"{f.Service} sync failed for {f.SeriesName}",
                Detail = f.Error,
                ActionLabel = "Open series",
                ActionLink = new ActivityLink(ActivityLinkKind.SeriesDetail, f.SeriesId.ToString()),
                DedupeKey = $"tracker-sync:{f.SeriesId}:{f.Service}",
            });
        }

        string failedList = string.Join(", ", failures.Select(f => f.Service.ToString()).Distinct());
        if (pushed.Count > 0)
        {
            string where = string.Join(", ", pushed);
            string summary = seriesCount == 1 && lastChapter is int chapter
                ? $"Synced {where} to chapter {chapter}"
                : $"Updated {where} for {seriesCount} series";
            if (failures.Count > 0)
            {
                summary += $" ({failedList} failed)";
            }

            job.Succeed(summary, itemsProcessed: pushed.Count, itemsFailed: failures.Count);
            _toasts.Show(new ToastRequest("Trackers updated", summary, failures.Count > 0 ? ToastSeverity.Warning : ToastSeverity.Success));
        }
        else if (failures.Count > 0)
        {
            string summary = $"{failedList} sync failed";
            job.Fail(summary, ex: new InvalidOperationException(failures[0].Error));
            _toasts.Show(new ToastRequest("Tracker sync failed", summary + " - see the Activity Center.", ToastSeverity.Error));
        }
        else
        {
            job.Succeed("Already up to date");
        }
    }

    // ---- Pull ----

    public async Task<TrackerPullResult> PullSeriesAsync(int seriesId)
    {
        try
        {
            if (!ReadSettings().TrackerAutoSyncFromTrackers)
            {
                return TrackerPullResult.None;
            }

            return await PullCoreAsync(seriesId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RaiseUnexpectedFailure(ex);
            return TrackerPullResult.None;
        }
    }

    private async Task<TrackerPullResult> PullCoreAsync(int seriesId)
    {
        DateTime now = _utcNow();
        using (var probe = _contextFactory())
        {
            var links = probe.TrackingLinks.Where(l => l.SeriesId == seriesId).ToList();
            bool anyDue = links.Any(l => _isConnected(probe, l.Service) && _adapterFactory(l.Service) is not null
                && (l.LastSyncedAt is not DateTime last || now - last >= PullThrottle));
            if (!anyDue)
            {
                return TrackerPullResult.None;
            }
        }

        using var job = _activity.StartJob(ActivityJobKind.TrackerFetch, "Syncing progress from trackers", cancellable: false, toastPolicy: ActivityToastPolicy.Never);
        var pulledFrom = new List<string>();
        var newlyRead = new List<int>();
        int? pulledChapter = null;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = _contextFactory();
            var series = context.Series.Include(s => s.Issues).Include(s => s.TrackingLinks).FirstOrDefault(s => s.Id == seriesId);
            if (series is null)
            {
                job.Succeed("Already up to date");
                return TrackerPullResult.None;
            }

            foreach (var link in series.TrackingLinks.ToList())
            {
                var adapter = _adapterFactory(link.Service);
                if (adapter is null || !_isConnected(context, link.Service)
                    || (link.LastSyncedAt is DateTime last && now - last < PullThrottle))
                {
                    continue;
                }

                await PaceAsync(link.Service).ConfigureAwait(false);
                var remote = await adapter.GetEntryAsync(context, link, CancellationToken.None).ConfigureAwait(false);
                if (remote is null)
                {
                    continue; // "no entry" and "couldn't check" are collapsed by ITrackerAdapter.GetEntryAsync
                }

                link.LastSyncedAt = _utcNow();
                if (TrackerSyncResolver.RemoteWins(TrackerProgressCalculator.ComputeChapterProgress(series.Issues), series.ReadingStatus, remote))
                {
                    var read = TrackerSyncResolver.ApplyRemote(series, remote);
                    newlyRead.AddRange(read.Select(i => i.Id));
                    pulledFrom.Add(link.Service.ToString());
                    pulledChapter = remote.ChapterProgress;
                    SeriesActivityLog.Record(context, series.Id, SeriesActivityEventKind.TrackerSynced, $"Pulled from {link.Service}.");
                }
            }

            context.SaveChanges();
        }
        finally
        {
            _gate.Release();
        }

        if (pulledFrom.Count > 0)
        {
            string summary = pulledChapter is int c
                ? $"Pulled from {string.Join(", ", pulledFrom)} up to chapter {c}"
                : $"Pulled from {string.Join(", ", pulledFrom)}";
            job.Succeed(summary, itemsProcessed: newlyRead.Count);
            _toasts.Show(new ToastRequest("Progress synced", summary, ToastSeverity.Success));
        }
        else
        {
            job.Succeed("Already up to date");
        }

        return new TrackerPullResult(newlyRead);
    }

    // ---- Shared ----

    private async Task PaceAsync(TrackingService service)
    {
        TimeSpan wait = TimeSpan.Zero;
        DateTime now = _utcNow();
        if (_lastRequestUtc.TryGetValue(service, out DateTime last))
        {
            wait = MinSpacing(service) - (now - last);
        }

        if (wait > TimeSpan.Zero)
        {
            await _delay(wait).ConfigureAwait(false);
        }

        _lastRequestUtc[service] = _utcNow();
    }

    private AppSettings ReadSettings()
    {
        using var context = _contextFactory();
        return context.GetOrCreateAppSettings();
    }

    private void RaiseUnexpectedFailure(Exception ex) =>
        _activity.RaiseAlert(new ActivityAlert
        {
            Severity = ActivityAlertSeverity.Warning,
            Title = "Tracker sync failed",
            Detail = ex.Message,
            DedupeKey = "tracker-sync:unexpected",
        });
}
