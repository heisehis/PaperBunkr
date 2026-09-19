using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.Tracking;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="TrackerAutoSyncService"/> (docs/superpowers/specs/2026-09-18-tracker-
/// behavior-settings-design.md) with fake adapters - the injectable seam that closes the long-
/// standing "no seam to inject a fake tracker adapter" gap. Real <see cref="ActivityService"/> with
/// synchronous dispatch; a fake toast host; a no-op delay so pacing is asserted, not slept.
/// </summary>
public class TrackerAutoSyncServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly ActivityService _activity = new(dispatch: a => a(), recordRun: _ => { });
    private readonly FakeToastHost _toasts = new();
    private readonly Dictionary<TrackingService, FakeTracker> _adapters = new();
    private readonly HashSet<TrackingService> _connected = new() { TrackingService.AniList };
    private readonly List<TimeSpan> _delays = new();
    private readonly DateTime _now = new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

    public TrackerAutoSyncServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_trackerautosync_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
        _adapters[TrackingService.AniList] = new FakeTracker(TrackingService.AniList);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private TrackerAutoSyncService CreateService() => new(
        _activity,
        _toasts,
        () => new PaperbunkrDbContext(_dbOptions),
        s => _adapters.GetValueOrDefault(s),
        (_, s) => _connected.Contains(s),
        () => _now,
        d => { _delays.Add(d); return Task.CompletedTask; });

    private int SeedSeries(string name, int readThrough, params TrackingService[] links)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = name };
        context.Series.Add(series);
        context.SaveChanges();
        for (int n = 1; n <= 5; n++)
        {
            var issue = new Issue { SeriesId = series.Id, Number = n.ToString(), PageCount = 20 };
            if (n <= readThrough)
            {
                IssueReadStateResolver.MarkAsRead(issue);
            }

            context.Issues.Add(issue);
        }

        foreach (var service in links)
        {
            context.TrackingLinks.Add(new TrackingLink { SeriesId = series.Id, Service = service, ExternalId = "1" });
        }

        context.SaveChanges();
        return series.Id;
    }

    private void SetSettings(Action<AppSettings> mutate)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        mutate(context.GetOrCreateAppSettings());
        context.SaveChanges();
    }

    // ---- Push ----

    [Fact]
    public async Task AfterReading_PushesForwardOnly_NeverSendsScoreOrDate_AndToastsSuccess()
    {
        int id = SeedSeries("One", readThrough: 3, TrackingService.AniList);

        await CreateService().OnIssueFinishedInReaderAsync(id);

        var push = Assert.Single(_adapters[TrackingService.AniList].Pushes);
        Assert.Equal(3, push.ChapterProgress);
        Assert.False(push.UpdateScore);
        Assert.False(push.UpdateFinishDate);
        var toast = Assert.Single(_toasts.Shown);
        Assert.Equal(ToastSeverity.Success, toast.Severity);
        Assert.Contains("Synced AniList to chapter 3", toast.Message);
        var job = Assert.Single(_activity.RecentJobs);
        Assert.Equal(ActivityJobKind.TrackerFetch, job.Kind);
        Assert.Equal(ActivityJobStatus.Succeeded, job.Status);
        using var verify = new PaperbunkrDbContext(_dbOptions);
        Assert.NotNull(verify.TrackingLinks.Single().LastSyncedAt);
        Assert.Contains(verify.SeriesActivityEvents, e => e.Kind == SeriesActivityEventKind.TrackerSynced);
    }

    [Fact]
    public async Task AfterReading_SettingOff_DoesNothing()
    {
        SetSettings(s => s.TrackerUpdateAfterReading = false);
        int id = SeedSeries("One", 3, TrackingService.AniList);

        await CreateService().OnIssueFinishedInReaderAsync(id);

        Assert.Empty(_adapters[TrackingService.AniList].Pushes);
        Assert.Empty(_toasts.Shown);
        Assert.Empty(_activity.RecentJobs);
    }

    [Fact]
    public async Task RemoteAlreadyAhead_SkipsPush_QuietOutcome_NoToast()
    {
        _adapters[TrackingService.AniList].Remote = new TrackerRemoteEntry(ReadingStatus.Reading, ChapterProgress: 9);
        int id = SeedSeries("One", 3, TrackingService.AniList);

        await CreateService().OnIssueFinishedInReaderAsync(id);

        Assert.Empty(_adapters[TrackingService.AniList].Pushes);
        Assert.Empty(_toasts.Shown);
        Assert.Equal("Already up to date", Assert.Single(_activity.RecentJobs).ResultSummary);
    }

    [Fact]
    public async Task NoConnectedLink_StartsNoJobAtAll()
    {
        int id = SeedSeries("One", 3, TrackingService.MangaBaka); // linked, but MangaBaka isn't connected
        _adapters[TrackingService.MangaBaka] = new FakeTracker(TrackingService.MangaBaka);

        await CreateService().OnIssueFinishedInReaderAsync(id);

        Assert.Empty(_activity.RecentJobs);
        Assert.Empty(_toasts.Shown);
    }

    [Fact]
    public async Task Failure_RaisesDedupedAlertWithRealError_ErrorToast_AndFailedJob()
    {
        _adapters[TrackingService.AniList].PushError = "400: Invalid token";
        int id = SeedSeries("One", 3, TrackingService.AniList);
        var service = CreateService();

        await service.OnIssueFinishedInReaderAsync(id);
        await service.OnIssueFinishedInReaderAsync(id);

        var alert = Assert.Single(_activity.Alerts); // second failure dedupes onto the same key
        Assert.Equal($"tracker-sync:{id}:AniList", alert.DedupeKey);
        Assert.Contains("Invalid token", alert.Detail);
        Assert.Equal(ActivityLinkKind.SeriesDetail, alert.ActionLink!.Kind);
        Assert.Equal(id.ToString(), alert.ActionLink.Payload);
        Assert.All(_toasts.Shown, t => Assert.Equal(ToastSeverity.Error, t.Severity));
        Assert.Equal(ActivityJobStatus.Failed, _activity.RecentJobs[0].Status);
    }

    [Fact]
    public async Task PartialFailure_JobSucceedsAndListsTheFailedTracker()
    {
        _connected.Add(TrackingService.MangaBaka);
        _adapters[TrackingService.MangaBaka] = new FakeTracker(TrackingService.MangaBaka) { PushError = "nope" };
        int id = SeedSeries("One", 3, TrackingService.AniList, TrackingService.MangaBaka);

        await CreateService().OnIssueFinishedInReaderAsync(id);

        var job = Assert.Single(_activity.RecentJobs);
        Assert.Equal(ActivityJobStatus.Succeeded, job.Status);
        Assert.Contains("AniList", job.ResultSummary);
        Assert.Contains("MangaBaka failed", job.ResultSummary);
        Assert.Single(_activity.Alerts);
    }

    // ---- Mark as read ----

    [Fact]
    public async Task MarkRead_Always_PushesImmediately()
    {
        int id = SeedSeries("One", 3, TrackingService.AniList);

        await CreateService().OnIssuesMarkedReadAsync(new[] { id });

        Assert.Single(_adapters[TrackingService.AniList].Pushes);
    }

    [Fact]
    public async Task MarkRead_Never_DoesNothing()
    {
        SetSettings(s => s.TrackerUpdateOnMarkRead = TrackerAutoUpdateMode.Never);
        int id = SeedSeries("One", 3, TrackingService.AniList);

        await CreateService().OnIssuesMarkedReadAsync(new[] { id });

        Assert.Empty(_adapters[TrackingService.AniList].Pushes);
        Assert.Empty(_toasts.Shown);
    }

    [Fact]
    public async Task MarkRead_Ask_ShowsActionableToast_UpdateRunsPush_DismissDoesNot()
    {
        SetSettings(s => s.TrackerUpdateOnMarkRead = TrackerAutoUpdateMode.Ask);
        int id = SeedSeries("One", 3, TrackingService.AniList);
        var service = CreateService();

        await service.OnIssuesMarkedReadAsync(new[] { id });

        var prompt = Assert.Single(_toasts.Shown);
        Assert.Equal("Update trackers to chapter 3?", prompt.Message);
        Assert.Empty(_adapters[TrackingService.AniList].Pushes);

        prompt.Actions!.Single(a => a.Label == "Dismiss").Command.Execute(null);
        Assert.Contains(prompt, _toasts.Closed);
        Assert.Empty(_adapters[TrackingService.AniList].Pushes);

        prompt.Actions!.Single(a => a.Label == "Update trackers").Command.Execute(null);
        await WaitForAsync(() => _adapters[TrackingService.AniList].Pushes.Count == 1);
    }

    [Fact]
    public async Task MarkRead_Ask_BulkNamesUpToThreeSeries()
    {
        SetSettings(s => s.TrackerUpdateOnMarkRead = TrackerAutoUpdateMode.Ask);
        var ids = new[]
        {
            SeedSeries("A", 1, TrackingService.AniList), SeedSeries("B", 1, TrackingService.AniList),
            SeedSeries("C", 1, TrackingService.AniList), SeedSeries("D", 1, TrackingService.AniList),
        };

        await CreateService().OnIssuesMarkedReadAsync(ids);

        var prompt = Assert.Single(_toasts.Shown);
        Assert.StartsWith("Update trackers for 4 series?", prompt.Message);
        Assert.Contains("+1 more", prompt.Message);
    }

    // ---- Pacing / coalescing ----

    [Fact]
    public async Task BulkPush_IsSerialAndPacedPerService()
    {
        var ids = new[] { SeedSeries("A", 1, TrackingService.AniList), SeedSeries("B", 1, TrackingService.AniList) };

        await CreateService().PushAsync(ids);

        Assert.Equal(2, _adapters[TrackingService.AniList].Pushes.Count);
        // Frozen clock: every request after the first waits the full AniList spacing.
        Assert.NotEmpty(_delays);
        Assert.All(_delays, d => Assert.Equal(TrackerAutoSyncService.MinSpacing(TrackingService.AniList), d));
        Assert.Equal("Updated AniList for 2 series", Assert.Single(_activity.RecentJobs).ResultSummary);
    }

    [Fact]
    public async Task SameSeriesWhileInFlight_IsCoalescedIntoOneRerun()
    {
        var adapter = _adapters[TrackingService.AniList];
        adapter.Gate = new TaskCompletionSource();
        int id = SeedSeries("One", 3, TrackingService.AniList);
        var service = CreateService();

        var first = service.PushAsync(new[] { id });
        await WaitForAsync(() => adapter.PushStarted);
        var second = service.PushAsync(new[] { id });
        var third = service.PushAsync(new[] { id });
        await Task.WhenAll(second, third); // both just mark the series dirty and return

        adapter.Gate.SetResult();
        await first;

        Assert.Equal(2, adapter.Pushes.Count); // the original push + exactly one coalesced rerun
    }

    // ---- Pull ----

    [Fact]
    public async Task Pull_SettingOff_DoesNothing()
    {
        _adapters[TrackingService.AniList].Remote = new TrackerRemoteEntry(ReadingStatus.Reading, 4);
        int id = SeedSeries("One", 1, TrackingService.AniList);

        var result = await CreateService().PullSeriesAsync(id);

        Assert.Empty(result.NewlyReadIssueIds);
        Assert.Equal(0, _adapters[TrackingService.AniList].GetCalls);
    }

    [Fact]
    public async Task Pull_RemoteAhead_MarksIssuesRead_Toasts_NeverPushes_AndStampsThrottle()
    {
        SetSettings(s => s.TrackerAutoSyncFromTrackers = true);
        _adapters[TrackingService.AniList].Remote = new TrackerRemoteEntry(ReadingStatus.Reading, 4);
        int id = SeedSeries("One", 1, TrackingService.AniList);

        var result = await CreateService().PullSeriesAsync(id);

        Assert.Equal(3, result.NewlyReadIssueIds.Count); // issues 2..4
        Assert.Empty(_adapters[TrackingService.AniList].Pushes);
        Assert.Contains("Pulled from AniList up to chapter 4", Assert.Single(_toasts.Shown).Message);
        using var verify = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(_now, verify.TrackingLinks.Single().LastSyncedAt);
        Assert.Equal(4, verify.Issues.Count(i => i.SeriesId == id && i.LastPageRead != null && i.LastPageRead > 0));
    }

    [Fact]
    public async Task Pull_LocalAhead_ChangesNothing_Quiet()
    {
        SetSettings(s => s.TrackerAutoSyncFromTrackers = true);
        _adapters[TrackingService.AniList].Remote = new TrackerRemoteEntry(ReadingStatus.Reading, 1);
        int id = SeedSeries("One", 3, TrackingService.AniList);

        var result = await CreateService().PullSeriesAsync(id);

        Assert.Empty(result.NewlyReadIssueIds);
        Assert.Empty(_toasts.Shown);
        Assert.Empty(_adapters[TrackingService.AniList].Pushes);
    }

    [Fact]
    public async Task Pull_WithinThrottleWindow_MakesNoRemoteCall_EvenForANewServiceInstance()
    {
        SetSettings(s => s.TrackerAutoSyncFromTrackers = true);
        int id = SeedSeries("One", 1, TrackingService.AniList);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.TrackingLinks.Single().LastSyncedAt = _now.AddMinutes(-3);
            context.SaveChanges();
        }

        await CreateService().PullSeriesAsync(id); // a fresh instance = a simulated app restart

        Assert.Equal(0, _adapters[TrackingService.AniList].GetCalls);
        Assert.Empty(_activity.RecentJobs);
    }

    [Fact]
    public async Task Pull_AfterTheThrottleWindow_ChecksAgain()
    {
        SetSettings(s => s.TrackerAutoSyncFromTrackers = true);
        int id = SeedSeries("One", 1, TrackingService.AniList);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.TrackingLinks.Single().LastSyncedAt = _now.AddMinutes(-11);
            context.SaveChanges();
        }

        await CreateService().PullSeriesAsync(id);

        Assert.Equal(1, _adapters[TrackingService.AniList].GetCalls);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "timed out waiting for condition");
    }

    private sealed class FakeToastHost : IToastHost
    {
        public List<ToastRequest> Shown { get; } = new();
        public List<ToastRequest> Closed { get; } = new();
        public void Show(ToastRequest toast) => Shown.Add(toast);
        public void Close(ToastRequest toast) => Closed.Add(toast);
    }

    private sealed class FakeTracker : ITrackerAdapter, ITrackerDetailedPush
    {
        private int _getCalls;
        public FakeTracker(TrackingService service) { Service = service; }
        public TrackingService Service { get; }
        public TrackerRemoteEntry? Remote { get; set; }
        public string? PushError { get; set; }
        public TaskCompletionSource? Gate { get; set; }
        public bool PushStarted { get; private set; }
        public List<TrackerPushPayload> Pushes { get; } = new();
        public int GetCalls => _getCalls;

        public async Task<(bool Success, string? ErrorDetail)> PushEntryDetailedAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken)
        {
            bool ok = await PushEntryAsync(context, link, payload, cancellationToken);
            return (ok, ok ? null : PushError);
        }

        public async Task<bool> PushEntryAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken)
        {
            PushStarted = true;
            if (Gate is { } gate && Pushes.Count == 0)
            {
                Pushes.Add(payload);
                await gate.Task;
                return PushError is null;
            }

            Pushes.Add(payload);
            return PushError is null;
        }

        public Task<TrackerRemoteEntry?> GetEntryAsync(PaperbunkrDbContext context, TrackingLink link, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _getCalls);
            return Task.FromResult(Remote);
        }
    }
}
