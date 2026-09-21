using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Daemon.Clients;
using Paperbunkr.Daemon.Events;
using Paperbunkr.Daemon.Indexers;
using Paperbunkr.Daemon.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>Slice 2 on the Wanted screen: grab, reject, retry, cancel, and the downloads section.</summary>
public class WantedDownloadsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_wanted_dl_{Guid.NewGuid():N}.db");
    private readonly FakeClient _client = new();
    private readonly ChannelEventPublisher _events = new();
    private bool _clientConfigured = true;
    private readonly List<(string Message, bool IsError)> _toasts = new();

    private static IEnumerable<QueueIssueViewModel> Issues(WantedScreenViewModel vm) => vm.QueueItems.OfType<QueueIssueViewModel>();

    public WantedDownloadsTests()
    {
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath};Foreign Keys=True").Options);

    private WantedScreenViewModel Create() => new(
        NewContext, _ => Task.CompletedTask, _ => { }, () => { }, _ => Task.CompletedTask,
        new GrabService(NewContext, _ => _clientConfigured ? _client : null, _events),
        _ => new NoComicVine(), post: a => a(), today: () => new DateTime(2026, 9, 19),
        notify: (message, isError) => _toasts.Add((message, isError)));

    private sealed class NoComicVine : IComicVineClient
    {
        public Task<IReadOnlyList<ComicVineVolume>> SearchVolumesAsync(string query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ComicVineVolume>>(Array.Empty<ComicVineVolume>());
        public Task<ComicVineVolume?> GetVolumeAsync(int volumeId, CancellationToken cancellationToken) => Task.FromResult<ComicVineVolume?>(null);
        public Task<IReadOnlyList<ComicVineIssue>> GetVolumeIssuesAsync(int volumeId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ComicVineIssue>>(Array.Empty<ComicVineIssue>());
    }

    private sealed class FakeClient : IDownloadClient
    {
        public List<string> Added { get; } = new();
        public List<string> Removed { get; } = new();
        public Exception? Throw { get; set; }

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken) => Task.FromResult(ConnectionTestResult.Ok("ok"));

        public Task<string> AddAsync(string downloadUrl, CancellationToken cancellationToken)
        {
            if (Throw is not null) throw Throw;
            Added.Add(downloadUrl);
            return Task.FromResult(TorrentHash.FromMagnet(downloadUrl)!);
        }

        public Task<IReadOnlyList<DownloadStatus>> GetStatusAsync(IReadOnlyCollection<string>? hashes, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DownloadStatus>>(Array.Empty<DownloadStatus>());
        public Task<IReadOnlyList<DownloadFile>> GetFilesAsync(string hash, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DownloadFile>>(Array.Empty<DownloadFile>());

        public Task<bool> RemoveAsync(string hash, bool deleteFiles, CancellationToken cancellationToken)
        {
            Removed.Add(hash);
            return Task.FromResult(true);
        }
    }

    private static string Magnet(char c) => "magnet:?xt=urn:btih:" + new string(c, 40);

    /// <summary>Three wanted issues of "Spawn": #261 with two candidates, #262 downloading at 40%, #263 failed.</summary>
    private (int Wanted, int GoodCandidate, int BadCandidate, int Downloading, int Failed) Seed()
    {
        using var context = NewContext();
        var watched = WantedService.TrackVolume(context, new ComicVineVolume(100, "Spawn", "Image", 1992, 300, null), null, false);
        var w1 = WantedService.Request(context, watched, new ComicVineIssue(1, "261", null, null, null, null, 100));
        var w2 = WantedService.Request(context, watched, new ComicVineIssue(2, "262", null, null, null, null, 100));
        var w3 = WantedService.Request(context, watched, new ComicVineIssue(3, "263", null, null, null, null, 100));
        w2.Status = WantedIssueStatus.Downloading; w2.DownloadProgress = 0.4; w2.GrabbedTitle = "Spawn 262 (1992) cbz"; w2.TorrentHash = new string('b', 40);
        w3.Status = WantedIssueStatus.Failed; w3.FailureReason = "The torrent was removed from qBittorrent before it finished.";
        var good = new ReleaseCandidate { WantedIssueId = w1.Id, Title = "Spawn 261 (1992) cbz", DownloadUrl = Magnet('a'), Score = 40, FoundAt = DateTime.UtcNow };
        var bad = new ReleaseCandidate { WantedIssueId = w1.Id, Title = "Spawn 261 (1992) cbr", DownloadUrl = Magnet('c'), Score = 5, FoundAt = DateTime.UtcNow };
        context.ReleaseCandidates.AddRange(good, bad);
        context.SaveChanges();
        return (w1.Id, good.Id, bad.Id, w2.Id, w3.Id);
    }

    private void ConfigureClient()
    {
        using var context = NewContext();
        context.GetOrCreateAcquisitionSettings().QBittorrentUrl = "http://qbit:8080";
        context.SaveChanges();
    }

    [Fact]
    public void Refresh_ShowsDownloadsAndFailures_WithProgressAndReasons()
    {
        Seed();
        var vm = Create();

        vm.Refresh();

        var failed = Issues(vm).Single(r => r.IsFailed);
        Assert.Equal("Spawn #263", failed.Title);
        Assert.False(failed.ShowProgress);
        Assert.Equal("Failed", failed.StatusText);
        Assert.Contains("removed from qBittorrent", failed.Detail);
        var running = Issues(vm).Single(r => r.IsDownloading);
        Assert.Equal("Downloading", running.StatusText);
        Assert.Equal(40, running.ProgressPercent);
        Assert.Equal("40%", running.ProgressText);
        Assert.Equal("Spawn 262 (1992) cbz", running.Detail);
        Assert.Equal(3, vm.QueueCount);                                                                 // one wanted, one downloading, one failed
        Assert.Equal(running, Assert.Single(vm.DownloadStrip));                                         // only the one in flight sits in the strip
        Assert.Equal("Downloading 1 issue", vm.DownloadStripHeading);
    }

    [Fact]
    public void ALiveDownloadEvent_AddsSpeedAndTimeLeft_ToTheRow()
    {
        Seed();
        var vm = Create();
        vm.Refresh();
        var running = Issues(vm).Single(r => r.IsDownloading);

        vm.ApplyDownloadProgress(new DownloadProgressEvent(running.Id, "Spawn #262", 0.5, 2 * 1024 * 1024, TimeSpan.FromSeconds(100)));

        Assert.Equal("50% · 2.0 MB/s · 2m", running.ProgressText);

        vm.Refresh();                                                                                    // the reload keeps the client's live numbers
        Assert.Same(running, Issues(vm).Single(r => r.IsDownloading));
        Assert.Contains("MB/s", running.ProgressText);
    }

    [Fact]
    public void AScreenWithOnlyDownloads_IsNotShownAsEmpty()
    {
        using (var context = NewContext())
        {
            var watched = WantedService.TrackVolume(context, new ComicVineVolume(100, "Spawn", "Image", 1992, 300, null), null, false);
            var w = WantedService.Request(context, watched, new ComicVineIssue(1, "261", null, null, null, null, 100));
            w.Status = WantedIssueStatus.Snatched; w.TorrentHash = new string('a', 40);
            context.SaveChanges();
        }

        var vm = Create();
        vm.Refresh();

        Assert.False(vm.HasNoQueue);
        Assert.Equal("Sent to qBittorrent", Assert.Single(Issues(vm)).StatusText);
    }

    [Fact]
    public void Grab_IsOnlyOffered_WhenQBittorrentIsSetUp()
    {
        var vm = Create();
        vm.Refresh();
        Assert.False(vm.HasDownloadClient);

        ConfigureClient();
        vm.Refresh();
        Assert.True(vm.HasDownloadClient);
    }

    [Fact]
    public async Task Grab_SendsTheReleaseToTheClient_AndTheIssueMovesToDownloads()
    {
        var ids = Seed();
        ConfigureClient();
        var vm = Create();
        vm.Refresh();
        var row = Issues(vm).Single(r => r.Id == ids.Wanted).Candidates.Single(c => c.Id == ids.GoodCandidate);

        await vm.GrabCommand.ExecuteAsync(row);

        Assert.Equal(new[] { Magnet('a') }, _client.Added);
        Assert.Contains(_toasts, t => !t.IsError && t.Message.Contains("Sent Spawn #261"));
        var grabbed = Issues(vm).Single(r => r.Title == "Spawn #261");
        Assert.Empty(grabbed.Candidates);                                    // the alternatives are dropped once one is chosen
        Assert.Equal(QueueStage.Downloading, grabbed.Stage);
        Assert.Equal("Sent to qBittorrent", grabbed.StatusText);
    }

    [Fact]
    public async Task AFailedGrab_ExplainsWhy_AndChangesNothing()
    {
        var ids = Seed();
        ConfigureClient();
        _client.Throw = new DownloadClientException("qBittorrent rejected the username or password.");
        var vm = Create();
        vm.Refresh();

        await vm.GrabCommand.ExecuteAsync(Issues(vm).Single(r => r.Id == ids.Wanted).Candidates.First(c => c.Id == ids.GoodCandidate));

        Assert.Contains(_toasts, t => t.IsError && t.Message == "qBittorrent rejected the username or password.");
        Assert.Equal(2, Issues(vm).Single(r => r.Id == ids.Wanted).Candidates.Count);
    }

    [Fact]
    public async Task Grab_WithoutAClient_TellsTheUserToSetOneUp()
    {
        var ids = Seed();
        _clientConfigured = false;
        var vm = Create();
        vm.Refresh();

        await vm.GrabCommand.ExecuteAsync(Issues(vm).Single(r => r.Id == ids.Wanted).Candidates.First(c => c.Id == ids.GoodCandidate));

        Assert.Contains(_toasts, t => t.Message.Contains("Set up qBittorrent"));
    }

    [Fact]
    public void Reject_BlocklistsTheRelease_AndDropsItFromTheList()
    {
        var ids = Seed();
        var vm = Create();
        vm.Refresh();

        vm.RejectCandidateCommand.Execute(Issues(vm).Single(r => r.Id == ids.Wanted).Candidates.Single(c => c.Id == ids.BadCandidate));

        Assert.Equal("Spawn 261 (1992) cbz", Assert.Single(Issues(vm).Single(r => r.Id == ids.Wanted).Candidates).Title);
        using var context = NewContext();
        var blocked = Assert.Single(context.ReleaseBlocklist);
        Assert.Equal("Spawn 261 (1992) cbr", blocked.ReleaseName);
        Assert.Equal(BlocklistReason.UserRejected, blocked.Reason);
    }

    [Fact]
    public void Retry_PutsAFailedIssueBackInWanted()
    {
        var ids = Seed();
        var vm = Create();
        vm.Refresh();

        vm.RetryDownloadCommand.Execute(Issues(vm).Single(r => r.IsFailed));

        Assert.DoesNotContain(Issues(vm), r => r.IsFailed);
        Assert.Contains(Issues(vm), r => r.Title == "Spawn #263" && r.Stage == QueueStage.Wanted);
        using var context = NewContext();
        Assert.Equal(WantedIssueStatus.Wanted, context.WantedIssues.Single(w => w.Id == ids.Failed).Status);
    }

    [Fact]
    public async Task Cancel_RemovesTheTorrent_AndReturnsTheIssueToWanted()
    {
        var ids = Seed();
        var vm = Create();
        vm.Refresh();

        await vm.CancelDownloadCommand.ExecuteAsync(Issues(vm).Single(r => r.Title == "Spawn #262"));

        Assert.Equal(new[] { new string('b', 40) }, _client.Removed);
        Assert.Contains(Issues(vm), r => r.Title == "Spawn #262" && r.Stage == QueueStage.Wanted);
        Assert.Empty(vm.DownloadStrip);
    }
}

public class AcquisitionBridgeDownloadTests
{
    private static (AcquisitionActivityBridge Bridge, ActivityService Activity, List<ActivityRun> Runs, List<int> Refreshes) Create()
    {
        var runs = new List<ActivityRun>();
        var refreshes = new List<int>();
        var activity = new ActivityService(dispatch: a => a(), recordRun: runs.Add);
        var bridge = new AcquisitionActivityBridge(activity, new ChannelEventPublisher().Reader, post: a => a(),
            resultLink: () => new ActivityLink(ActivityLinkKind.WantedScreen), onWantedChanged: () => refreshes.Add(1));
        return (bridge, activity, runs, refreshes);
    }

    [Fact]
    public void RunningDownloads_ShowAsOneAggregateJob_ThatSucceedsWithATally()
    {
        var (bridge, activity, runs, _) = Create();

        bridge.Handle(new DownloadsChangedEvent(2, 0.25, "Spawn #261"));
        Thread.Sleep(150);                                                   // the job handle coalesces progress to ~10 updates a second
        bridge.Handle(new DownloadsChangedEvent(2, 0.5, "Spawn #262"));

        var job = Assert.Single(activity.ActiveJobs);                       // still one job, not two
        Assert.Equal("Downloading comics", job.Title);
        Assert.Equal(0.5, activity.ActiveJobs[0].Fraction, 3);

        bridge.Handle(new IssueImportedEvent(1, "Spawn #261", "C:\\lib\\a.cbz"));
        bridge.Handle(new IssueImportedEvent(2, "Spawn #262", "C:\\lib\\b.cbz"));
        bridge.Handle(new DownloadsChangedEvent(0, 0, null));

        Assert.Empty(activity.ActiveJobs);
        var run = Assert.Single(runs);
        Assert.Equal("2 comics imported.", run.ResultSummary);
        Assert.Equal(ActivityRunStatus.Succeeded, run.Status);
        Assert.Equal("WantedScreen", run.ResultLinkKind);
    }

    [Fact]
    public void AFailedImport_RaisesADedupedAlert_AndTheTallyReportsIt()
    {
        var (bridge, activity, runs, _) = Create();
        bridge.Handle(new DownloadsChangedEvent(1, 0.9, "Spawn #261"));

        bridge.Handle(new IssueFailedEvent(7, "Spawn #261", "The archive is password protected."));
        bridge.Handle(new IssueFailedEvent(7, "Spawn #261", "The archive is password protected."));   // same issue again
        bridge.Handle(new DownloadsChangedEvent(0, 0, null));

        var alert = Assert.Single(activity.Alerts);
        Assert.Equal("Couldn't get Spawn #261", alert.Title);
        Assert.Equal("The archive is password protected.", alert.Detail);
        Assert.Equal(ActivityLinkKind.WantedScreen, alert.ActionLink!.Kind);
        Assert.Equal("0 imported, 2 failed.", Assert.Single(runs).ResultSummary);
    }

    [Fact]
    public void EveryDownloadEvent_TellsTheWantedScreenToRefresh()
    {
        var (bridge, _, _, refreshes) = Create();

        bridge.Handle(new IssueSnatchedEvent(1, "Spawn #261", "x", false));
        bridge.Handle(new DownloadProgressEvent(1, "Spawn #261", 0.3, 100, null));
        bridge.Handle(new IssueImportedEvent(1, "Spawn #261", "p"));
        bridge.Handle(new IssueFailedEvent(2, "Spawn #262", "boom"));

        Assert.Equal(4, refreshes.Count);
    }

    [Fact]
    public void ADownloadsChangedToZero_WithNoJobOpen_IsHarmless()
    {
        var (bridge, activity, runs, _) = Create();

        bridge.Handle(new DownloadsChangedEvent(0, 0, null));

        Assert.Empty(activity.ActiveJobs);
        Assert.Empty(runs);
    }
}
