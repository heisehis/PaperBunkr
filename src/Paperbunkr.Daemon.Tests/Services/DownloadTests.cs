using Paperbunkr.Daemon.Clients;
using Paperbunkr.Daemon.Events;
using Paperbunkr.Daemon.Import;
using Paperbunkr.Daemon.Indexers;
using Paperbunkr.Daemon.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Daemon.Tests.Services;

public class FakeDownloadClient : IDownloadClient
{
    public Dictionary<string, DownloadStatus> Statuses { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Added { get; } = new();
    public List<(string Hash, bool DeleteFiles)> Removed { get; } = new();
    public List<IReadOnlyCollection<string>?> StatusQueries { get; } = new();
    public Exception? Throw { get; set; }
    public IReadOnlyList<DownloadFile> Files { get; set; } = new[] { new DownloadFile("Spawn 261.cbz", 1000) };

    public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken) => Task.FromResult(ConnectionTestResult.Ok("ok"));

    public Task<string> AddAsync(string downloadUrl, CancellationToken cancellationToken)
    {
        if (Throw is not null) throw Throw;
        Added.Add(downloadUrl);
        return Task.FromResult(TorrentHash.FromMagnet(downloadUrl) ?? new string('e', 40));
    }

    public Task<IReadOnlyList<DownloadStatus>> GetStatusAsync(IReadOnlyCollection<string>? hashes, CancellationToken cancellationToken)
    {
        if (Throw is not null) throw Throw;
        StatusQueries.Add(hashes);
        return Task.FromResult<IReadOnlyList<DownloadStatus>>(Statuses.Values.Where(s => hashes is null || hashes.Contains(s.Hash, StringComparer.OrdinalIgnoreCase)).ToList());
    }

    public Task<IReadOnlyList<DownloadFile>> GetFilesAsync(string hash, CancellationToken cancellationToken) => Task.FromResult(Files);

    public Task<bool> RemoveAsync(string hash, bool deleteFiles, CancellationToken cancellationToken)
    {
        if (Throw is not null) throw Throw;
        Removed.Add((hash, deleteFiles));
        return Task.FromResult(true);
    }

    public static string Hash(char c) => new(c, 40);
    public static string Magnet(char c) => $"magnet:?xt=urn:btih:{Hash(c)}";

    public static DownloadStatus Status(char c, double progress, DownloadState state = DownloadState.Downloading) =>
        new(Hash(c), "torrent", progress, state, 1000, 100, TimeSpan.FromSeconds(30), "D:\\dl", "D:\\dl\\x");
}

public sealed class FakeImporter : IImportProcessor
{
    public Func<int, ImportOutcome> Respond { get; set; } = id => new ImportOutcome(new[] { id });
    public List<int> Calls { get; } = new();

    public Task<ImportOutcome> ImportAsync(int wantedIssueId, DownloadStatus download, IReadOnlyList<DownloadFile> files, CancellationToken cancellationToken)
    {
        Calls.Add(wantedIssueId);
        return Task.FromResult(Respond(wantedIssueId));
    }
}

public class BlocklistTests : CycleTestBase
{
    [Fact]
    public void Add_IsIdempotent_AndMatchesByNameOrHash_CaseInsensitively()
    {
        using var context = NewContext();
        BlocklistService.Add(context, "Spawn 261 (1992) cbr", null, BlocklistReason.UserRejected);
        BlocklistService.Add(context, "spawn 261 (1992) CBR", null, BlocklistReason.UserRejected);   // same name again
        BlocklistService.Add(context, "Some Release", FakeDownloadClient.Hash('a').ToUpperInvariant(), BlocklistReason.DownloadFailed, "boom");
        BlocklistService.Add(context, "Renamed Re-upload", FakeDownloadClient.Hash('a'), BlocklistReason.DownloadFailed);   // same hash again

        Assert.Equal(2, context.ReleaseBlocklist.Count());
        var snapshot = BlocklistService.Load(context);
        Assert.True(snapshot.IsBlocked("SPAWN 261 (1992) CBR", "magnet:?xt=urn:btih:" + new string('9', 40)));
        Assert.True(snapshot.IsBlocked("Anything else entirely", FakeDownloadClient.Magnet('a')));      // blocked through its hash, despite a new name
        Assert.False(snapshot.IsBlocked("Spawn 262", FakeDownloadClient.Magnet('b')));
        Assert.Equal(BlocklistReason.DownloadFailed, context.ReleaseBlocklist.Single(b => b.TorrentHash != null).Reason);
    }

    [Fact]
    public async Task ASearch_NeverStoresABlocklistedRelease()
    {
        Configure();
        AddWanted((DateTime?)null);
        using (var context = NewContext())
        {
            BlocklistService.Add(context, "Spawn 261 (1992) bad", null, BlocklistReason.Corrupt);
        }

        Indexer.Respond = _ => new[] { Release("Spawn 261 (1992) bad"), Release("Spawn 261 (1992) good") };

        await NewCycle().RunAsync(manual: true, CancellationToken.None);

        using var check = NewContext();
        Assert.Equal("Spawn 261 (1992) good", Assert.Single(check.ReleaseCandidates).Title);
    }
}

public class GrabServiceTests : CycleTestBase
{
    private readonly FakeDownloadClient _client = new();

    private GrabService Grab(bool configured = true) => new(NewContext, _ => configured ? _client : null, Events);

    private (int WantedId, int CandidateId) SeedCandidate(string title = "Spawn 261 (1992) cbz", char hash = 'a', bool pack = false)
    {
        var ids = AddWanted((DateTime?)null);
        using var context = NewContext();
        var candidate = new ReleaseCandidate { WantedIssueId = ids[0], Title = title, DownloadUrl = FakeDownloadClient.Magnet(hash), Score = 40, IsPack = pack, FoundAt = Now };
        context.ReleaseCandidates.Add(candidate);
        context.ReleaseCandidates.Add(new ReleaseCandidate { WantedIssueId = ids[0], Title = "Other", DownloadUrl = FakeDownloadClient.Magnet('c'), FoundAt = Now });
        context.SaveChanges();
        return (ids[0], candidate.Id);
    }

    [Fact]
    public async Task Grab_SendsTheReleaseToTheClient_AndMovesTheIssueToSnatched()
    {
        var (wantedId, candidateId) = SeedCandidate();

        var result = await Grab().GrabAsync(candidateId, automatic: false, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(new[] { FakeDownloadClient.Magnet('a') }, _client.Added);
        using var context = NewContext();
        var wanted = context.WantedIssues.Single(w => w.Id == wantedId);
        Assert.Equal(WantedIssueStatus.Snatched, wanted.Status);
        Assert.Equal(FakeDownloadClient.Hash('a'), wanted.TorrentHash);
        Assert.Equal("Spawn 261 (1992) cbz", wanted.GrabbedTitle);
        Assert.Equal(0, wanted.DownloadProgress);
        Assert.Empty(context.ReleaseCandidates);                 // the decision is made: the alternatives go
        var snatched = Assert.IsType<IssueSnatchedEvent>(Drain().Single(e => e is IssueSnatchedEvent));
        Assert.False(snatched.Automatic);
        Assert.Contains("Spawn #261", snatched.Label);
    }

    [Fact]
    public async Task Grab_ExplainsEachWayItCannotProceed_WithoutChangingAnything()
    {
        var (wantedId, candidateId) = SeedCandidate();

        Assert.Contains("Set up qBittorrent", (await Grab(configured: false).GrabAsync(candidateId, false, CancellationToken.None)).Message);

        _client.Throw = new DownloadClientException("qBittorrent did not respond in time.");
        var failed = await Grab().GrabAsync(candidateId, false, CancellationToken.None);
        Assert.False(failed.Success);
        Assert.Equal("qBittorrent did not respond in time.", failed.Message);
        _client.Throw = null;

        using (var context = NewContext())
        {
            BlocklistService.Add(context, "Spawn 261 (1992) cbz", null, BlocklistReason.UserRejected);
        }

        Assert.Contains("blocklist", (await Grab().GrabAsync(candidateId, false, CancellationToken.None)).Message);
        Assert.Contains("no longer available", (await Grab().GrabAsync(candidateId + 999, false, CancellationToken.None)).Message);

        using var check = NewContext();
        Assert.Equal(WantedIssueStatus.Wanted, check.WantedIssues.Single(w => w.Id == wantedId).Status);
        Assert.Equal(2, check.ReleaseCandidates.Count());        // nothing was lost
        Assert.Empty(_client.Added);
    }

    [Fact]
    public async Task Grab_RefusesAnIssueThatIsAlreadyInFlight()
    {
        var (wantedId, candidateId) = SeedCandidate();
        Assert.True((await Grab().GrabAsync(candidateId, false, CancellationToken.None)).Success);
        using (var context = NewContext())
        {
            context.ReleaseCandidates.Add(new ReleaseCandidate { WantedIssueId = wantedId, Title = "Late", DownloadUrl = FakeDownloadClient.Magnet('d'), FoundAt = Now });
            context.SaveChanges();
        }

        using var lookup = NewContext();
        var lateId = lookup.ReleaseCandidates.Single().Id;
        var second = await Grab().GrabAsync(lateId, false, CancellationToken.None);

        Assert.False(second.Success);
        Assert.Contains("already snatched", second.Message);
        Assert.Single(_client.Added);
    }

    [Fact]
    public void Reject_BlocklistsTheRelease_AndDropsItFromTheList()
    {
        var (_, candidateId) = SeedCandidate();

        Grab().Reject(candidateId);

        using var context = NewContext();
        Assert.DoesNotContain(context.ReleaseCandidates, c => c.Id == candidateId);
        var row = Assert.Single(context.ReleaseBlocklist);
        Assert.Equal("Spawn 261 (1992) cbz", row.ReleaseName);
        Assert.Equal(BlocklistReason.UserRejected, row.Reason);
        Grab().Reject(candidateId);                                // rejecting it again is harmless
    }

    [Fact]
    public void Retry_PutsAFailedIssueBackToWanted_DueForAnImmediateSearch()
    {
        var ids = AddWanted((DateTime?)null);
        using (var context = NewContext())
        {
            var w = context.WantedIssues.Single();
            w.Status = WantedIssueStatus.Failed; w.FailureReason = "boom"; w.TorrentHash = "x"; w.LastSearchedAt = Now;
            context.SaveChanges();
        }

        Grab().Retry(ids[0]);

        using var check = NewContext();
        var wanted = check.WantedIssues.Single();
        Assert.Equal(WantedIssueStatus.Wanted, wanted.Status);
        Assert.Null(wanted.FailureReason);
        Assert.Null(wanted.TorrentHash);
        Assert.Null(wanted.LastSearchedAt);
    }

    [Fact]
    public async Task Cancel_RemovesTheTorrent_AndReturnsTheIssueToWanted()
    {
        var (wantedId, candidateId) = SeedCandidate();
        await Grab().GrabAsync(candidateId, false, CancellationToken.None);

        var result = await Grab().CancelAsync(wantedId, deleteFiles: true, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(new[] { (FakeDownloadClient.Hash('a'), true) }, _client.Removed);
        using var context = NewContext();
        var wanted = context.WantedIssues.Single();
        Assert.Equal(WantedIssueStatus.Wanted, wanted.Status);
        Assert.Null(wanted.TorrentHash);
        Assert.Null(wanted.LastSearchedAt);

        Assert.False((await Grab().CancelAsync(wantedId, false, CancellationToken.None)).Success);   // nothing in flight any more
    }
}

public class DownloadTrackerTests : CycleTestBase
{
    private readonly FakeDownloadClient _client = new();
    private readonly FakeImporter _importer = new();

    private DownloadTracker Tracker(bool withImporter = true, bool configured = true) =>
        new(NewContext, _ => configured ? _client : null, withImporter ? _importer : null, Events);

    private int Snatched(char hash = 'a', string title = "Spawn 261 (1992)", WantedIssueStatus status = WantedIssueStatus.Snatched, int index = 0)
    {
        var ids = AddWanted(Enumerable.Repeat((DateTime?)null, index + 1).ToArray());
        using var context = NewContext();
        var wanted = context.WantedIssues.Single(w => w.Id == ids[index]);
        wanted.Status = status; wanted.TorrentHash = FakeDownloadClient.Hash(hash); wanted.GrabbedTitle = title; wanted.DownloadProgress = 0;
        context.SaveChanges();
        return wanted.Id;
    }

    [Fact]
    public async Task ARunningDownload_UpdatesProgress_AndReportsAggregateStatus()
    {
        var id = Snatched();
        _client.Statuses[FakeDownloadClient.Hash('a')] = FakeDownloadClient.Status('a', 0.4);

        await Tracker().TickAsync(CancellationToken.None);

        using var context = NewContext();
        var wanted = context.WantedIssues.Single();
        Assert.Equal(WantedIssueStatus.Downloading, wanted.Status);
        Assert.Equal(0.4, wanted.DownloadProgress);
        var events = Drain();
        var progress = events.OfType<DownloadProgressEvent>().Single();
        Assert.Equal(id, progress.WantedIssueId);
        Assert.Equal(0.4, progress.Progress);
        Assert.Equal(TimeSpan.FromSeconds(30), progress.Eta);
        var changed = events.OfType<DownloadsChangedEvent>().Single();
        Assert.Equal(1, changed.Active);
        Assert.Equal(0.4, changed.AverageProgress);
    }

    [Fact]
    public async Task OnlyTheHashesPaperbunkrRecorded_AreEverAskedAbout()
    {
        Snatched('a');
        AddWanted((DateTime?)null);                              // a plain Wanted issue: not a download
        _client.Statuses[FakeDownloadClient.Hash('a')] = FakeDownloadClient.Status('a', 0.1);
        _client.Statuses[FakeDownloadClient.Hash('f')] = FakeDownloadClient.Status('f', 0.9);   // someone else's torrent

        await Tracker().TickAsync(CancellationToken.None);

        var query = Assert.Single(_client.StatusQueries);
        Assert.Equal(new[] { FakeDownloadClient.Hash('a') }, query);
    }

    [Fact]
    public async Task ATorrentThatVanished_FailsTheIssue_AndBlocklistsTheRelease()
    {
        Snatched('a', "Spawn 261 (1992) cbz");
        // nothing in the client's list any more

        await Tracker().TickAsync(CancellationToken.None);

        using var context = NewContext();
        var wanted = context.WantedIssues.Single();
        Assert.Equal(WantedIssueStatus.Failed, wanted.Status);
        Assert.Contains("removed from qBittorrent", wanted.FailureReason);
        var blocked = Assert.Single(context.ReleaseBlocklist);
        Assert.Equal("Spawn 261 (1992) cbz", blocked.ReleaseName);
        Assert.Equal(FakeDownloadClient.Hash('a'), blocked.TorrentHash);
        Assert.IsType<IssueFailedEvent>(Drain().Single(e => e is IssueFailedEvent));
    }

    [Theory]
    [InlineData(DownloadState.Error, "reported an error")]
    [InlineData(DownloadState.Missing, "lost the downloaded files")]
    public async Task AClientError_FailsTheIssue(DownloadState state, string reasonFragment)
    {
        Snatched('a');
        _client.Statuses[FakeDownloadClient.Hash('a')] = FakeDownloadClient.Status('a', 0.3, state);

        await Tracker().TickAsync(CancellationToken.None);

        using var context = NewContext();
        var wanted = context.WantedIssues.Single();
        Assert.Equal(WantedIssueStatus.Failed, wanted.Status);
        Assert.Contains(reasonFragment, wanted.FailureReason);
        Assert.Single(context.ReleaseBlocklist);
    }

    [Fact]
    public async Task ACompletedDownload_IsHandedToTheImporterWithItsFiles()
    {
        var id = Snatched();
        _client.Statuses[FakeDownloadClient.Hash('a')] = FakeDownloadClient.Status('a', 1.0, DownloadState.Completed);

        await Tracker().TickAsync(CancellationToken.None);

        Assert.Equal(new[] { id }, _importer.Calls);
    }

    [Fact]
    public async Task AFailedImport_FailsTheIssue_AndBlocklistsWithTheImportersReason()
    {
        Snatched('a', "Spawn 261 (1992) cbz");
        _client.Statuses[FakeDownloadClient.Hash('a')] = FakeDownloadClient.Status('a', 1.0, DownloadState.Completed);
        _importer.Respond = _ => ImportOutcome.Failed("The archive is password protected.", BlocklistReason.PasswordProtected);

        await Tracker().TickAsync(CancellationToken.None);

        using var context = NewContext();
        Assert.Equal(WantedIssueStatus.Failed, context.WantedIssues.Single().Status);
        Assert.Equal("The archive is password protected.", context.WantedIssues.Single().FailureReason);
        Assert.Equal(BlocklistReason.PasswordProtected, Assert.Single(context.ReleaseBlocklist).Reason);
    }

    [Fact]
    public async Task ASuccessfulImport_LeavesTheIssueToTheImporter()
    {
        var id = Snatched();
        _client.Statuses[FakeDownloadClient.Hash('a')] = FakeDownloadClient.Status('a', 1.0, DownloadState.Completed);
        _importer.Respond = wantedId =>
        {
            using var context = NewContext();
            var w = context.WantedIssues.Single(x => x.Id == wantedId);
            w.Status = WantedIssueStatus.Imported;
            context.SaveChanges();
            return new ImportOutcome(new[] { wantedId });
        };

        await Tracker().TickAsync(CancellationToken.None);

        using var check = NewContext();
        Assert.Equal(WantedIssueStatus.Imported, check.WantedIssues.Single(w => w.Id == id).Status);
        Assert.Empty(check.ReleaseBlocklist);
    }

    [Fact]
    public async Task APackImport_SettlesTheOtherIssuesItCovered_WithoutReprocessingThem()
    {
        var first = Snatched('a', "Spawn v1-2", index: 0);
        var second = Snatched('a', "Spawn v1-2", index: 1);      // both rows point at the same torrent
        _client.Statuses[FakeDownloadClient.Hash('a')] = FakeDownloadClient.Status('a', 1.0, DownloadState.Completed);
        _importer.Respond = _ =>
        {
            using var context = NewContext();
            foreach (var w in context.WantedIssues) w.Status = WantedIssueStatus.Imported;
            context.SaveChanges();
            return new ImportOutcome(new[] { first, second });
        };

        await Tracker().TickAsync(CancellationToken.None);

        Assert.Equal(new[] { first }, _importer.Calls);           // the second row was already settled: not imported twice
        using var check = NewContext();
        Assert.All(check.WantedIssues, w => Assert.Equal(WantedIssueStatus.Imported, w.Status));
    }

    [Fact]
    public async Task WithoutAnImporter_AFinishedDownloadWaits_ShowingAsComplete()
    {
        Snatched();
        _client.Statuses[FakeDownloadClient.Hash('a')] = FakeDownloadClient.Status('a', 1.0, DownloadState.Completed);

        await Tracker(withImporter: false).TickAsync(CancellationToken.None);

        using var context = NewContext();
        var wanted = context.WantedIssues.Single();
        Assert.Equal(WantedIssueStatus.Downloading, wanted.Status);
        Assert.Equal(1.0, wanted.DownloadProgress);
    }

    [Fact]
    public async Task AnUnreachableClient_LeavesTheDownloadsAlone_AndAlertsOnce_ThenClears()
    {
        Snatched();
        var tracker = Tracker();
        _client.Throw = new DownloadClientException("connection refused");

        await tracker.TickAsync(CancellationToken.None);

        using (var context = NewContext())
        {
            Assert.Equal(WantedIssueStatus.Snatched, context.WantedIssues.Single().Status);   // not failed: it isn't the download's fault
        }

        Assert.Contains(Drain().OfType<DaemonAlertEvent>(), a => a.Key == DownloadTracker.ClientAlert);

        _client.Throw = null;
        _client.Statuses[FakeDownloadClient.Hash('a')] = FakeDownloadClient.Status('a', 0.5);
        await tracker.TickAsync(CancellationToken.None);
        Assert.Contains(Drain().OfType<DaemonAlertClearedEvent>(), c => c.Key == DownloadTracker.ClientAlert);
    }

    [Fact]
    public async Task WhenNoClientIsConfigured_DownloadsAreLeftAlone()
    {
        Snatched();

        await Tracker(configured: false).TickAsync(CancellationToken.None);

        using var context = NewContext();
        Assert.Equal(WantedIssueStatus.Snatched, context.WantedIssues.Single().Status);
    }

    [Fact]
    public async Task TheAggregateEvent_IsPublishedWhenTheLastDownloadSettles_ThenNoMore()
    {
        Snatched();
        var tracker = Tracker();
        _client.Statuses[FakeDownloadClient.Hash('a')] = FakeDownloadClient.Status('a', 0.5);
        await tracker.TickAsync(CancellationToken.None);
        Assert.True(tracker.HasPendingReport);
        Drain();

        _client.Statuses[FakeDownloadClient.Hash('a')] = FakeDownloadClient.Status('a', 1.0, DownloadState.Completed);
        _importer.Respond = id =>
        {
            using var context = NewContext();
            context.WantedIssues.Single(w => w.Id == id).Status = WantedIssueStatus.Imported;
            context.SaveChanges();
            return new ImportOutcome(new[] { id });
        };
        await tracker.TickAsync(CancellationToken.None);

        Assert.Equal(0, Drain().OfType<DownloadsChangedEvent>().Single().Active);      // the host can now close its job
        Assert.False(tracker.HasPendingReport);
        await tracker.TickAsync(CancellationToken.None);
        Assert.Empty(Drain().OfType<DownloadsChangedEvent>());                        // and stays quiet afterwards
    }
}

public class AutoGrabTests : CycleTestBase
{
    private readonly FakeDownloadClient _client = new();

    private AcquisitionCycle Cycle() => new(NewContext, (_, _) => Indexer, _ => ComicVine, Events, () => Now, new GrabService(NewContext, _ => _client, Events));

    private void SetAutoGrab(bool on, int minScore = 20)
    {
        using var context = NewContext();
        var s = context.GetOrCreateAcquisitionSettings();
        s.AutoGrab = on; s.AutoGrabMinScore = minScore;
        context.SaveChanges();
    }

    private static IndexerRelease Magnet(string title, char hash, int seeders) => new()
    {
        Title = title, DownloadUrl = FakeDownloadClient.Magnet(hash), Guid = title, SizeBytes = 50 * 1024 * 1024, Seeders = seeders, Indexer = "IdxA",
    };

    [Fact]
    public async Task WithAutoGrabOn_TheBestQualifyingCandidateIsSent_AndNoCandidatesAreLeftToReview()
    {
        Configure();
        SetAutoGrab(true, minScore: 20);
        AddWanted((DateTime?)null);
        Indexer.Respond = _ => new[] { Magnet("Spawn 261 (1992) cbz", 'a', 40), Magnet("Spawn 261 (1992) cbr", 'b', 5) };

        await Cycle().RunAsync(manual: true, CancellationToken.None);

        Assert.Equal(new[] { FakeDownloadClient.Magnet('a') }, _client.Added);
        using var context = NewContext();
        Assert.Equal(WantedIssueStatus.Snatched, context.WantedIssues.Single().Status);
        var events = Drain();
        Assert.True(Assert.Single(events.OfType<IssueSnatchedEvent>()).Automatic);
        Assert.DoesNotContain(events, e => e is CandidatesFoundEvent);
    }

    [Fact]
    public async Task AutoGrab_NeverTakesAPack_OrAnythingBelowTheBar()
    {
        Configure();
        SetAutoGrab(true, minScore: 60);
        AddWanted((DateTime?)null);
        Indexer.Respond = _ => new[] { Magnet("Spawn Complete Series (1992)", 'c', 500), Magnet("Spawn 261 (1992) cbz", 'a', 10) };   // the pack scores very low; the single (10 + 25 for cbz = 35) is under 60

        await Cycle().RunAsync(manual: true, CancellationToken.None);

        Assert.Empty(_client.Added);
        using var context = NewContext();
        Assert.Equal(WantedIssueStatus.Wanted, context.WantedIssues.Single().Status);
        Assert.Equal(2, context.ReleaseCandidates.Count());             // left for the user to decide
        Assert.Contains(Drain(), e => e is CandidatesFoundEvent);
    }

    [Fact]
    public async Task WithAutoGrabOff_NothingIsSent()
    {
        Configure();
        AddWanted((DateTime?)null);
        Indexer.Respond = _ => new[] { Magnet("Spawn 261 (1992) cbz", 'a', 100) };

        await Cycle().RunAsync(manual: true, CancellationToken.None);

        Assert.Empty(_client.Added);
    }

    [Fact]
    public async Task AFailedAutoGrab_LeavesTheCandidatesForTheUser()
    {
        Configure();
        SetAutoGrab(true, minScore: 0);
        AddWanted((DateTime?)null);
        _client.Throw = new DownloadClientException("qBittorrent did not respond in time.");
        Indexer.Respond = _ => new[] { Magnet("Spawn 261 (1992) cbz", 'a', 40) };

        await Cycle().RunAsync(manual: true, CancellationToken.None);

        using var context = NewContext();
        Assert.Equal(WantedIssueStatus.Wanted, context.WantedIssues.Single().Status);
        Assert.Single(context.ReleaseCandidates);
    }
}

public class DownloadLoopTests : CycleTestBase
{
    [Fact]
    public async Task TheDownloadTick_OnlyRunsTheTracker_WhenSomethingIsDownloading()
    {
        var client = new FakeDownloadClient();
        var tracker = new DownloadTracker(NewContext, _ => client, null, Events);
        var service = new AcquisitionService(NewCycle(), NewContext, () => Now, tracker);

        await service.DownloadTickAsync(CancellationToken.None);
        Assert.Empty(client.StatusQueries);                        // idle: no calls to qBittorrent at all

        var ids = AddWanted((DateTime?)null);
        using (var context = NewContext())
        {
            var w = context.WantedIssues.Single();
            w.Status = WantedIssueStatus.Snatched; w.TorrentHash = FakeDownloadClient.Hash('a');
            context.SaveChanges();
        }

        client.Statuses[FakeDownloadClient.Hash('a')] = FakeDownloadClient.Status('a', 0.2);
        await service.DownloadTickAsync(CancellationToken.None);
        Assert.Single(client.StatusQueries);
    }

    [Fact]
    public async Task WithoutATracker_TheDownloadTickIsANoOp()
    {
        var service = new AcquisitionService(NewCycle(), NewContext, () => Now);

        await service.DownloadTickAsync(CancellationToken.None);
    }
}
