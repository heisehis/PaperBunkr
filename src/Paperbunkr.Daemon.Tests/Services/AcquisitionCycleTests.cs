using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Daemon.Events;
using Paperbunkr.Daemon.Indexers;
using Paperbunkr.Daemon.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Daemon.Tests.Services;

public abstract class CycleTestBase : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_cycle_{Guid.NewGuid():N}.db");
    protected readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
    protected readonly ChannelEventPublisher Events = new();
    protected readonly FakeIndexer Indexer = new();
    protected readonly FakeComicVine ComicVine = new();

    protected PaperbunkrDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath};Foreign Keys=True").Options);

    protected CycleTestBase()
    {
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    protected AcquisitionCycle NewCycle() => new(NewContext, (_, _) => Indexer, _ => ComicVine, Events, () => Now);

    protected void Configure(bool enabled = true, bool prowlarr = true, bool comicVineKey = false)
    {
        using var context = NewContext();
        var settings = context.GetOrCreateAcquisitionSettings();
        settings.Enabled = enabled;
        settings.ProwlarrUrl = prowlarr ? "http://prowlarr.local:9696" : "";
        settings.PollIntervalMinutes = 60;
        context.SaveChanges();
        if (prowlarr) CredentialStore.Set(context, "Prowlarr", CredentialKind.ApiKey, "KEY");
        if (comicVineKey) CredentialStore.Set(context, "ComicVine", CredentialKind.ApiKey, "CVKEY");
    }

    /// <summary>A watched "Spawn" volume with wanted issues at the given store dates (null = unknown date, due now).</summary>
    protected int[] AddWanted(params DateTime?[] storeDates)
    {
        using var context = NewContext();
        var watched = WantedService.TrackVolume(context, new ComicVineVolume(100, "Spawn", "Image", 1992, 300, null), seriesId: null, watchFutureReleases: false);
        var ids = new List<int>();
        int n = 1;
        foreach (var date in storeDates)
        {
            var wanted = WantedService.Request(context, watched, new ComicVineIssue(1000 + n, (260 + n).ToString(), null, date, null, null, 100));
            ids.Add(wanted.Id);
            n++;
        }

        return ids.ToArray();
    }

    protected List<DaemonEvent> Drain()
    {
        var seen = new List<DaemonEvent>();
        while (Events.Reader.TryRead(out var e)) seen.Add(e);
        return seen;
    }

    protected static IndexerRelease Release(string title, int seeders = 10) => new()
    {
        Title = title, DownloadUrl = "magnet:?xt=urn:btih:" + Math.Abs(title.GetHashCode()), Guid = title, SizeBytes = 50 * 1024 * 1024, Seeders = seeders, Indexer = "IdxA",
    };

    protected sealed class FakeIndexer : IIndexerClient
    {
        public Func<string, IReadOnlyList<IndexerRelease>> Respond { get; set; } = _ => Array.Empty<IndexerRelease>();
        public Exception? Throw { get; set; }
        public List<string> Queries { get; } = new();

        public Task<IReadOnlyList<IndexerRelease>> SearchAsync(string queryText, CancellationToken cancellationToken)
        {
            Queries.Add(queryText);
            if (Throw is not null) throw Throw;
            return Task.FromResult(Respond(queryText));
        }

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken) => Task.FromResult(ConnectionTestResult.Ok("ok"));
    }

    protected sealed class FakeComicVine : IComicVineClient
    {
        public Dictionary<int, List<ComicVineIssue>> IssuesByVolume { get; } = new();
        public Exception? Throw { get; set; }
        public List<int> IssueCalls { get; } = new();

        public Task<IReadOnlyList<ComicVineVolume>> SearchVolumesAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ComicVineVolume>>(Array.Empty<ComicVineVolume>());

        public Task<ComicVineVolume?> GetVolumeAsync(int volumeId, CancellationToken cancellationToken) => Task.FromResult<ComicVineVolume?>(null);

        public Task<IReadOnlyList<ComicVineIssue>> GetVolumeIssuesAsync(int volumeId, CancellationToken cancellationToken)
        {
            IssueCalls.Add(volumeId);
            if (Throw is not null) throw Throw;
            return Task.FromResult<IReadOnlyList<ComicVineIssue>>(IssuesByVolume.TryGetValue(volumeId, out var l) ? l : new List<ComicVineIssue>());
        }
    }
}

public class AcquisitionCycleTests : CycleTestBase
{
    [Fact]
    public async Task WhenDisabled_ScheduledRunsDoNothing_ButManualExplainsWhy()
    {
        Configure(enabled: false);

        await NewCycle().RunAsync(manual: false, CancellationToken.None);
        Assert.Empty(Drain());

        await NewCycle().RunAsync(manual: true, CancellationToken.None);
        var alert = Assert.IsType<DaemonAlertEvent>(Assert.Single(Drain()));
        Assert.Equal(DaemonAlertSeverity.Info, alert.Severity);
    }

    [Fact]
    public async Task WithoutProwlarrDetails_RaisesAConfigurationAlert_AndSearchesNothing()
    {
        Configure(prowlarr: false);
        AddWanted((DateTime?)null);

        await NewCycle().RunAsync(manual: false, CancellationToken.None);

        var alert = Assert.IsType<DaemonAlertEvent>(Assert.Single(Drain()));
        Assert.Equal(AcquisitionCycle.NotConfiguredAlert, alert.Key);
        Assert.Empty(Indexer.Queries);
    }

    [Fact]
    public async Task DueIssues_AreSearched_AndCandidatesStored_WithoutAnyDownload()
    {
        Configure();
        var ids = AddWanted((DateTime?)null);
        Indexer.Respond = q => q == "Spawn 261" ? new[] { Release("Spawn 261 (1992) cbz", 30), Release("Spawn 261 (1992) cbr", 5) } : Array.Empty<IndexerRelease>();

        await NewCycle().RunAsync(manual: false, CancellationToken.None);

        using var context = NewContext();
        var candidates = context.ReleaseCandidates.Where(c => c.WantedIssueId == ids[0]).OrderByDescending(c => c.Score).ToList();
        Assert.Equal(2, candidates.Count);
        Assert.Contains("cbz", candidates[0].Title);
        Assert.Equal("IdxA", candidates[0].Indexer);
        Assert.NotNull(context.WantedIssues.Single().LastSearchedAt);
        Assert.Equal(WantedIssueStatus.Wanted, context.WantedIssues.Single().Status);   // still awaiting the user's approval

        var events = Drain();
        Assert.Contains(events, e => e is CycleStartedEvent);
        var found = events.OfType<CandidatesFoundEvent>().Single();
        Assert.Equal(2, found.Count);
        Assert.Contains("Spawn #261", found.Label);
        var done = events.OfType<CycleCompletedEvent>().Single();
        Assert.Equal(1, done.IssuesSearched);
        Assert.Equal(2, done.CandidatesFound);
    }

    [Fact]
    public async Task UpcomingIssues_AreNotSearchedUntilTheirStoreDate()
    {
        Configure();
        AddWanted(Now.AddDays(10));

        await NewCycle().RunAsync(manual: false, CancellationToken.None);

        Assert.Empty(Indexer.Queries);
        Assert.Equal("Nothing to search for.", Drain().OfType<CycleCompletedEvent>().Single().Summary);
    }

    [Fact]
    public async Task RecentlySearchedIssues_AreSkippedByScheduledRuns_ButNotByManualOnes()
    {
        Configure();
        AddWanted((DateTime?)null);
        await NewCycle().RunAsync(manual: false, CancellationToken.None);
        Indexer.Queries.Clear();

        await NewCycle().RunAsync(manual: false, CancellationToken.None);
        Assert.Empty(Indexer.Queries);                                   // searched moments ago

        await NewCycle().RunAsync(manual: true, CancellationToken.None);
        Assert.NotEmpty(Indexer.Queries);                                // "Search now" always searches what is due
    }

    [Fact]
    public async Task ASecondSearch_ReplacesTheOldCandidates_InsteadOfPilingUp()
    {
        Configure();
        AddWanted((DateTime?)null);
        Indexer.Respond = _ => new[] { Release("Spawn 261 (1992) old") };
        await NewCycle().RunAsync(manual: true, CancellationToken.None);

        Indexer.Respond = _ => new[] { Release("Spawn 261 (1992) new") };
        await NewCycle().RunAsync(manual: true, CancellationToken.None);

        using var context = NewContext();
        Assert.Equal("Spawn 261 (1992) new", Assert.Single(context.ReleaseCandidates).Title);
    }

    [Fact]
    public async Task AnUnreachableIndexer_RaisesAnAlert_FailsTheCycle_AndCountsTheFailure()
    {
        Configure();
        AddWanted((DateTime?)null);
        Indexer.Throw = new IndexerException("Couldn't reach Prowlarr");
        var cycle = NewCycle();

        await cycle.RunAsync(manual: false, CancellationToken.None);

        var events = Drain();
        Assert.Contains(events.OfType<DaemonAlertEvent>(), a => a.Key == AcquisitionCycle.IndexerAlert && a.Severity == DaemonAlertSeverity.Warning);
        Assert.Contains(events, e => e is CycleFailedEvent);
        Assert.DoesNotContain(events, e => e is CycleCompletedEvent);
        Assert.Equal(1, cycle.ConsecutiveIndexerFailures);

        // Recovery clears the alert and resets the counter.
        Indexer.Throw = null;
        await cycle.RunAsync(manual: true, CancellationToken.None);
        Assert.Equal(0, cycle.ConsecutiveIndexerFailures);
        Assert.Contains(Drain(), e => e is DaemonAlertClearedEvent c && c.Key == AcquisitionCycle.IndexerAlert);
    }

    [Fact]
    public async Task AnUnexpectedError_FailsTheCycle_WithoutThrowing()
    {
        Configure();
        AddWanted((DateTime?)null);
        Indexer.Throw = new InvalidOperationException("boom");

        await NewCycle().RunAsync(manual: false, CancellationToken.None);   // must not throw

        Assert.Contains(Drain().OfType<CycleFailedEvent>(), f => f.Message.Contains("boom"));
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        Configure();
        AddWanted((DateTime?)null);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NewCycle().RunAsync(manual: false, cts.Token));
    }

    [Fact]
    public async Task FollowedVolumes_AreRefreshed_AndTheirUpcomingIssuesBecomeSearches()
    {
        Configure(comicVineKey: true);
        using (var context = NewContext())
        {
            var series = new Series { Name = "Spawn" };
            context.Series.Add(series);
            context.SaveChanges();
            WantedService.TrackVolume(context, new ComicVineVolume(100, "Spawn", "Image", 1992, 300, null), series.Id, watchFutureReleases: true);
        }

        ComicVine.IssuesByVolume[100] = new List<ComicVineIssue>
        {
            new(1, "1", null, Now.AddDays(-400), null, null, 100),   // old gap: never auto-requested
            new(2, "300", null, Now.Date, null, null, 100),          // due today: requested and searched
            new(3, "301", null, Now.AddDays(21), null, null, 100),   // upcoming: requested, not yet searched
        };
        Indexer.Respond = q => q == "Spawn 300" ? new[] { Release("Spawn 300 (2026)") } : Array.Empty<IndexerRelease>();

        await NewCycle().RunAsync(manual: false, CancellationToken.None);

        using var check = NewContext();
        Assert.Equal(new[] { 100 }, ComicVine.IssueCalls);
        Assert.Equal(new[] { 2, 3 }, check.WantedIssues.OrderBy(w => w.ComicVineIssueId).Select(w => w.ComicVineIssueId));
        Assert.Contains(Indexer.Queries, q => q == "Spawn 300");
        Assert.DoesNotContain(Indexer.Queries, q => q.StartsWith("Spawn 301"));
        Assert.NotEmpty(check.ReleaseCandidates);
    }

    [Fact]
    public async Task FreshCatalogs_AreNotRefreshedAgain()
    {
        Configure(comicVineKey: true);
        using (var context = NewContext())
        {
            var watched = WantedService.TrackVolume(context, new ComicVineVolume(100, "Spawn", "Image", 1992, 300, null), null, watchFutureReleases: true);
            watched.LastRefreshedAt = Now.AddHours(-1);
            context.SaveChanges();
        }

        await NewCycle().RunAsync(manual: false, CancellationToken.None);

        Assert.Empty(ComicVine.IssueCalls);
    }

    [Fact]
    public async Task ComicVineTrouble_RaisesAnAlert_ButSearchingStillHappens()
    {
        Configure(comicVineKey: true);
        AddWanted((DateTime?)null);
        using (var context = NewContext())
        {
            var watched = context.WatchedSeries.Single();
            watched.WatchFutureReleases = true;
            context.SaveChanges();
        }

        ComicVine.Throw = new ComicVineException("Rate limit exceeded", 107);
        Indexer.Respond = _ => new[] { Release("Spawn 261 (1992)") };

        await NewCycle().RunAsync(manual: false, CancellationToken.None);

        var events = Drain();
        Assert.Contains(events.OfType<DaemonAlertEvent>(), a => a.Key == AcquisitionCycle.ComicVineAlert);
        Assert.Contains(events, e => e is CycleCompletedEvent);          // the cycle still completed
        using var check = NewContext();
        Assert.NotEmpty(check.ReleaseCandidates);

        ComicVine.Throw = new ComicVineException("Invalid key", 100);
        await NewCycle().RunAsync(manual: true, CancellationToken.None);
        Assert.Contains(Drain().OfType<DaemonAlertEvent>(), a => a.Key == AcquisitionCycle.ComicVineAlert && a.Title.Contains("API key"));
    }

    [Fact]
    public async Task PerCycleSearchCap_IsRespected()
    {
        Configure();
        AddWanted(Enumerable.Repeat((DateTime?)null, AcquisitionCycle.MaxSearchesPerCycle + 5).ToArray());

        await NewCycle().RunAsync(manual: true, CancellationToken.None);

        using var context = NewContext();
        Assert.Equal(AcquisitionCycle.MaxSearchesPerCycle, context.WantedIssues.Count(w => w.LastSearchedAt != null));
    }
}

public class AcquisitionServiceTickTests : CycleTestBase
{
    private DateTime _clock;

    private AcquisitionService NewService(out AcquisitionCycle cycle)
    {
        _clock = Now;
        cycle = new AcquisitionCycle(NewContext, (_, _) => Indexer, _ => ComicVine, Events, () => _clock);
        return new AcquisitionService(cycle, NewContext, () => _clock);
    }

    [Fact]
    public async Task TheFirstTickRuns_ThenTheIntervalGatesLaterOnes()
    {
        Configure();
        AddWanted((DateTime?)null);
        var service = NewService(out _);

        await service.TickAsync(CancellationToken.None);
        int afterFirst = Indexer.Queries.Count;
        Assert.True(afterFirst > 0);

        _clock = _clock.AddMinutes(10);
        await service.TickAsync(CancellationToken.None);
        Assert.Equal(afterFirst, Indexer.Queries.Count);            // poll interval (60 min) hasn't elapsed

        _clock = _clock.AddMinutes(60);
        AddNewWantedForSecondRound();
        await service.TickAsync(CancellationToken.None);
        Assert.True(Indexer.Queries.Count > afterFirst);            // interval elapsed, and the earlier issue is due for a re-check
    }

    private void AddNewWantedForSecondRound()
    {
        using var context = NewContext();
        var watched = context.WatchedSeries.Single();
        WantedService.Request(context, watched, new ComicVineIssue(5000, "999", null, null, null, null, 100));
    }

    [Fact]
    public async Task RunNow_BypassesTheInterval_OnTheNextTick()
    {
        Configure();
        AddWanted((DateTime?)null);
        var service = NewService(out _);
        await service.TickAsync(CancellationToken.None);
        Indexer.Queries.Clear();

        _clock = _clock.AddMinutes(1);
        service.RunNow();
        await service.TickAsync(CancellationToken.None);

        Assert.NotEmpty(Indexer.Queries);
    }

    [Fact]
    public async Task ADisabledService_NeverRuns_EvenWhenTheIntervalHasElapsed()
    {
        Configure(enabled: false);
        AddWanted((DateTime?)null);
        var service = NewService(out _);

        await service.TickAsync(CancellationToken.None);

        Assert.Empty(Indexer.Queries);
    }

    [Fact]
    public async Task AfterFailures_TheWaitBacksOff()
    {
        Configure();
        AddWanted((DateTime?)null);
        Indexer.Throw = new IndexerException("down");
        var service = NewService(out var cycle);

        await service.TickAsync(CancellationToken.None);          // fails: 1 failure -> next wait is 2x the interval
        Assert.Equal(1, cycle.ConsecutiveIndexerFailures);
        Indexer.Queries.Clear();

        _clock = _clock.AddMinutes(70);                            // past 1x (60) but not 2x (120)
        await service.TickAsync(CancellationToken.None);
        Assert.Empty(Indexer.Queries);

        _clock = _clock.AddMinutes(60);                            // now past 2x
        await service.TickAsync(CancellationToken.None);
        Assert.NotEmpty(Indexer.Queries);
    }

    [Fact]
    public async Task StoppingTheService_EndsTheLoopCleanly()
    {
        Configure(enabled: false);
        var service = NewService(out _);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
    }
}
