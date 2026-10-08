using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary>
/// <see cref="MetronAccountSync"/> against a real SQLite database and a scripted account (docs/superpowers/specs/
/// 2026-10-05-metron-account-sync-design.md). In the quota collection because a run consults the process-wide
/// <see cref="MetronQuota"/>.
/// </summary>
[Collection("MetronQuota")]
public sealed class MetronAccountSyncTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_metron_sync_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly FakeAccount _account = new();
    private readonly FakeLookup _lookup = new();

    public MetronAccountSyncTests()
    {
        MetronQuota.Reset();
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = Db();
        context.Database.EnsureCreated();
        var settings = context.GetOrCreateAppSettings();
        settings.MetronSyncEnabled = true;
        context.SaveChanges();
    }

    public void Dispose()
    {
        MetronQuota.Reset();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext Db() => new(_options);

    private MetronAccountSync Sync(int budget = MetronAccountSync.DefaultRequestBudget) => new(Db, _account, _lookup, () => Now, budget);

    private void Enable(Action<AppSettings> configure)
    {
        using var context = Db();
        configure(context.GetOrCreateAppSettings());
        context.SaveChanges();
    }

    private sealed class FakeLookup : IComicIssueLookup
    {
        public Dictionary<int, int> MetronIdByComicVineId { get; } = new();
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ComicIssueHit>> FindIssuesByComicVineIdAsync(int comicVineIssueId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<ComicIssueHit>>(MetronIdByComicVineId.TryGetValue(comicVineIssueId, out int id)
                ? new[] { new ComicIssueHit(id, 1) } : Array.Empty<ComicIssueHit>());
        }

        public Task<IReadOnlyList<ComicIssueHit>> FindIssuesByUpcAsync(string upc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ComicIssueHit>>(Array.Empty<ComicIssueHit>());
    }

    /// <summary>An in-memory Metron account: it behaves like the documented endpoints and keeps a log of every call.</summary>
    private sealed class FakeAccount : IMetronAccount
    {
        public List<MetronPullListSeries> PullList { get; } = new();
        public Dictionary<int, MetronCollectionItem> Collection { get; } = new();   // by item id
        public Dictionary<int, MetronWishListItem> WishList { get; } = new();        // by item id
        public List<(int IssueId, DateTime ReadAt, int? Rating)> Reads { get; } = new();
        public List<string> Calls { get; } = new();
        public HashSet<int> UnknownIssues { get; } = new();
        public Exception? Throw { get; set; }

        /// <summary>When set, the pull-list read waits on it - lets a test hold one run open while it starts another.</summary>
        public Task? HoldPullList { get; set; }
        private int _nextItemId = 500;

        private void Log(string call)
        {
            if (Throw is not null) throw Throw;
            Calls.Add(call);
        }

        private void RejectUnknown(int issueId)
        {
            if (UnknownIssues.Contains(issueId)) throw new ComicVineException("Metron returned HTTP 400.") { HttpStatus = 400 };
        }

        public async Task<IReadOnlyList<MetronPullListSeries>> GetPullListAsync(CancellationToken cancellationToken)
        {
            Log("pull:list");
            if (HoldPullList is not null)
            {
                await HoldPullList;
            }

            return PullList.ToList();
        }

        public Task AddToPullListAsync(int seriesId, CancellationToken cancellationToken)
        {
            Log($"pull:add:{seriesId}");
            PullList.Add(new MetronPullListSeries(seriesId, $"Series {seriesId}", 2020));
            return Task.CompletedTask;
        }

        public Task RemoveFromPullListAsync(int seriesId, CancellationToken cancellationToken)
        {
            Log($"pull:remove:{seriesId}");
            PullList.RemoveAll(s => s.SeriesId == seriesId);
            return Task.CompletedTask;
        }

        public Task<MetronCollectionItem> ScrobbleAsync(int issueId, DateTime readAtUtc, int? rating, CancellationToken cancellationToken)
        {
            Log($"scrobble:{issueId}");
            RejectUnknown(issueId);
            Reads.Add((issueId, readAtUtc, rating));
            var existing = Collection.Values.FirstOrDefault(i => i.IssueId == issueId);
            var item = new MetronCollectionItem(existing?.ItemId ?? _nextItemId++, issueId, true, readAtUtc, rating ?? existing?.Rating);
            Collection[item.ItemId] = item;
            return Task.FromResult(item);
        }

        public Task<MetronCollectionItem> AddToCollectionAsync(int issueId, CancellationToken cancellationToken)
        {
            Log($"collection:add:{issueId}");
            RejectUnknown(issueId);
            var item = Collection.Values.FirstOrDefault(i => i.IssueId == issueId) ?? new MetronCollectionItem(_nextItemId++, issueId, false, null, null);
            Collection[item.ItemId] = item;
            return Task.FromResult(item);
        }

        public Task SetCollectionRatingAsync(int itemId, int? rating, CancellationToken cancellationToken)
        {
            Log($"collection:rate:{itemId}:{rating?.ToString() ?? "none"}");
            if (!Collection.TryGetValue(itemId, out var item)) throw new ComicVineException("Metron has no such record.", 101);
            Collection[itemId] = item with { Rating = rating };
            return Task.CompletedTask;
        }

        public Task<MetronCollectionPage> GetCollectionPageAsync(string? pageUrl, CancellationToken cancellationToken)
        {
            Log("collection:page");
            return Task.FromResult(new MetronCollectionPage(Collection.Values.ToList(), null));
        }

        public Task<MetronWishListItem> AddToWishListAsync(int issueId, CancellationToken cancellationToken)
        {
            Log($"wish:add:{issueId}");
            RejectUnknown(issueId);
            var item = new MetronWishListItem(_nextItemId++, issueId, "Wanted");
            WishList[item.ItemId] = item;
            return Task.FromResult(item);
        }

        public Task<int?> AcquireWishListItemAsync(int itemId, CancellationToken cancellationToken)
        {
            Log($"wish:acquire:{itemId}");
            WishList[itemId] = WishList[itemId] with { Status = "Acquired" };
            return Task.FromResult<int?>(_nextItemId++);
        }

        public Task RemoveFromWishListAsync(int itemId, CancellationToken cancellationToken)
        {
            Log($"wish:remove:{itemId}");
            WishList.Remove(itemId);
            return Task.CompletedTask;
        }
    }

    private int Follow(int metronSeriesId, bool following = true, ComicProvider provider = ComicProvider.Metron)
    {
        using var context = Db();
        return WantedService.TrackVolume(context, new ComicVineVolume(metronSeriesId, $"Series {metronSeriesId}", "Image", 2020, 10, null), null, following, provider).Id;
    }

    /// <summary>A library issue with a file, linked to the given provider ids.</summary>
    private int AddIssue(int? metronId = null, int? comicVineId = null, float? rating = null, bool hasFile = true, int? pageCount = 20)
    {
        using var context = Db();
        var series = context.Series.FirstOrDefault() ?? context.Series.Add(new Series { Name = "Kilo Station" }).Entity;
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", FilePath = hasFile ? $"book-{Guid.NewGuid():N}.cbz" : null, Rating = rating, PageCount = pageCount };
        context.Issues.Add(issue);
        context.SaveChanges();
        if (metronId is int m)
        {
            context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Issue, EntityId = issue.Id, Provider = ComicProvider.Metron, ExternalId = m.ToString() });
        }

        if (comicVineId is int cv)
        {
            context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Issue, EntityId = issue.Id, Provider = ComicProvider.ComicVine, ExternalId = cv.ToString() });
        }

        context.SaveChanges();
        return issue.Id;
    }

    private int Finish(int issueId, DateTime? at = null)
    {
        using var context = Db();
        var read = new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = issueId, Kind = ReadingEventKind.Finished, TimestampUtc = at ?? Now.AddHours(-1) };
        context.ReadingEvents.Add(read);
        context.SaveChanges();
        return read.Id;
    }

    private int Want(int metronIssueId, WantedIssueStatus status = WantedIssueStatus.Wanted, int? issueId = null)
    {
        using var context = Db();
        var watched = context.WatchedSeries.FirstOrDefault(w => w.Provider == ComicProvider.Metron)
            ?? WantedService.TrackVolume(context, new ComicVineVolume(900, "Series 900", "Image", 2020, 10, null), null, false, ComicProvider.Metron);
        var want = new WantedIssue { WatchedSeriesId = watched.Id, Provider = ComicProvider.Metron, ExternalIssueId = metronIssueId, IssueNumber = "1", Status = status, IssueId = issueId };
        context.WantedIssues.Add(want);
        context.SaveChanges();
        return want.Id;
    }

    // ---- switches ----

    [Fact]
    public async Task WithTheMasterSwitchOff_NothingIsSent_WhateverTheAreasSay()
    {
        Enable(s => { s.MetronSyncEnabled = false; s.MetronSyncPullList = s.MetronSyncReading = s.MetronSyncCollection = s.MetronSyncWishList = true; });
        Follow(100);
        Finish(AddIssue(metronId: 7001));

        var report = await Sync().RunAsync(CancellationToken.None);

        Assert.Empty(_account.Calls);
        Assert.Equal(0, report.Requests);
    }

    [Fact]
    public async Task OnlyTheAreasSwitchedOn_Run()
    {
        Enable(s => s.MetronSyncPullList = true);
        Follow(100);
        Finish(AddIssue(metronId: 7001));

        await Sync().RunAsync(CancellationToken.None);

        Assert.Equal(new[] { "pull:list", "pull:add:100" }, _account.Calls);
    }

    // ---- pull list ----

    [Fact]
    public async Task PullList_AdditionsGoBothWays()
    {
        Enable(s => s.MetronSyncPullList = true);
        Follow(100);
        Follow(101, following: false);                                  // tracked, not followed: not sent
        Follow(300, provider: ComicProvider.ComicVine);                 // another database's series: not Metron's business
        _account.PullList.Add(new MetronPullListSeries(200, "Remote Only", 1999));

        var report = await Sync().RunAsync(CancellationToken.None);

        Assert.Equal(new[] { 100, 200 }, _account.PullList.Select(s => s.SeriesId).OrderBy(i => i));
        Assert.Equal((1, 1), (report.PullListAdded, report.SeriesFollowed));
        using var context = Db();
        var followedHere = context.WatchedSeries.Single(w => w.ExternalVolumeId == 200);
        Assert.True(followedHere.WatchFutureReleases);
        Assert.Equal(ComicProvider.Metron, followedHere.Provider);
        Assert.Equal("Remote Only", followedHere.Name);

        // A second run finds nothing to do, and asks only for the list.
        _account.Calls.Clear();
        await Sync().RunAsync(CancellationToken.None);
        Assert.Equal(new[] { "pull:list" }, _account.Calls);
    }

    [Fact]
    public async Task PullList_UnfollowingHere_RemovesItThere()
    {
        Enable(s => s.MetronSyncPullList = true);
        int watchedId = Follow(100);
        await Sync().RunAsync(CancellationToken.None);

        using (var context = Db())
        {
            WantedService.SetWatchFutureReleases(context, watchedId, watch: false);
        }

        var report = await Sync().RunAsync(CancellationToken.None);

        Assert.Empty(_account.PullList);
        Assert.Equal(1, report.PullListRemoved);
        using var check = Db();
        Assert.False(check.WatchedSeries.Single().WatchFutureReleases);            // and it is not followed again from the (now empty) list
        Assert.Empty(check.MetronSyncLinks);
    }

    [Fact]
    public async Task PullList_ARemovalMadeOnMetron_NeitherUnfollowsHere_NorIsPushedBack()
    {
        Enable(s => s.MetronSyncPullList = true);
        Follow(100);
        await Sync().RunAsync(CancellationToken.None);
        _account.PullList.Clear();                                       // the user took it off on metron.cloud

        await Sync().RunAsync(CancellationToken.None);
        _account.Calls.Clear();
        await Sync().RunAsync(CancellationToken.None);

        Assert.Empty(_account.PullList);
        Assert.Equal(new[] { "pull:list" }, _account.Calls);
        using var context = Db();
        Assert.True(context.WatchedSeries.Single().WatchFutureReleases);
        Assert.Equal(MetronSyncState.RemovedRemotely, context.MetronSyncLinks.Single().State);
    }

    // ---- reading ----

    [Fact]
    public async Task Reading_EachFinishedReadIsSentOnce_WithItsDateAndRating_AndAReReadIsSentAgain()
    {
        Enable(s => s.MetronSyncReading = true);
        int issueId = AddIssue(metronId: 7001, rating: 4.4f);
        Finish(issueId, Now.AddDays(-2));

        var first = await Sync().RunAsync(CancellationToken.None);
        await Sync().RunAsync(CancellationToken.None);                   // nothing new: nothing sent
        Finish(issueId, Now.AddHours(-3));                               // a re-read
        await Sync().RunAsync(CancellationToken.None);

        Assert.Equal(1, first.ReadsSent);
        Assert.Equal(new[] { (7001, Now.AddDays(-2), (int?)4), (7001, Now.AddHours(-3), (int?)4) }, _account.Reads);
    }

    [Fact]
    public async Task Reading_OpensNovelsAndBooksWithNoId_AreSteppedOver()
    {
        Enable(s => s.MetronSyncReading = true);
        int noId = AddIssue();
        using (var context = Db())
        {
            context.ReadingEvents.AddRange(
                new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = noId, Kind = ReadingEventKind.Opened, TimestampUtc = Now },
                new ReadingEvent { ItemType = ReadingItemType.Novel, ItemId = 1, Kind = ReadingEventKind.Finished, TimestampUtc = Now });
            context.SaveChanges();
        }

        int last = Finish(noId);

        var report = await Sync().RunAsync(CancellationToken.None);

        Assert.Empty(_account.Reads);
        Assert.Equal(0, report.Requests);
        using var check = Db();
        Assert.Equal(last, check.GetOrCreateAppSettings().MetronSyncReadingEventId);
    }

    [Fact]
    public async Task Reading_TurningItOnStartsFromNow_AndSendMyHistoryGoesBackToTheStart()
    {
        int issueId = AddIssue(metronId: 7001);
        Finish(issueId, Now.AddDays(-30));
        using (var context = Db())
        {
            MetronAccountSync.StartReadingFromNow(context);
        }

        Enable(s => s.MetronSyncReading = true);
        await Sync().RunAsync(CancellationToken.None);
        Assert.Empty(_account.Reads);

        using (var context = Db())
        {
            MetronAccountSync.SendReadingHistory(context);
        }

        await Sync().RunAsync(CancellationToken.None);
        Assert.Single(_account.Reads);
    }

    [Fact]
    public async Task ABookKnownOnlyByItsComicVineId_IsLookedUpOnce_AndTheLinkIsKept()
    {
        Enable(s => s.MetronSyncReading = true);
        int issueId = AddIssue(comicVineId: 555);
        _lookup.MetronIdByComicVineId[555] = 7001;
        Finish(issueId);

        await Sync().RunAsync(CancellationToken.None);
        Finish(issueId);
        await Sync().RunAsync(CancellationToken.None);

        Assert.Equal(2, _account.Reads.Count);
        Assert.Equal(1, _lookup.Calls);
        using var context = Db();
        Assert.Equal("7001", context.ComicMetadataExternalIds.Single(e => e.Provider == ComicProvider.Metron).ExternalId);
    }

    // ---- wish list ----

    [Fact]
    public async Task WishList_WantsAreAdded_RemovedWantsAreRemoved_AndArrivalsAreMarkedAcquired()
    {
        Enable(s => s.MetronSyncWishList = true);
        int keep = Want(8001);
        int drop = Want(8002);
        int arrives = Want(8003);
        Want(8004, WantedIssueStatus.Ignored);                           // never sent

        var first = await Sync().RunAsync(CancellationToken.None);
        Assert.Equal(3, first.WishListAdded);

        int libraryIssue = AddIssue(metronId: 8003);
        using (var context = Db())
        {
            context.WantedIssues.Remove(context.WantedIssues.Single(w => w.Id == drop));
            var arrived = context.WantedIssues.Single(w => w.Id == arrives);
            arrived.Status = WantedIssueStatus.Imported;
            arrived.IssueId = libraryIssue;
            context.SaveChanges();
        }

        var second = await Sync().RunAsync(CancellationToken.None);

        Assert.Equal((1, 1), (second.WishListRemoved, second.WishListAcquired));
        Assert.Equal(new[] { "Acquired", "Wanted" }, _account.WishList.Values.Select(i => i.Status).OrderBy(s => s));
        using var check = Db();
        Assert.Equal(MetronSyncState.Synced, check.MetronSyncLinks.Single(l => l.Kind == MetronSyncKind.WishListItem && l.LocalId == keep).State);
        // The acquire made a collection item on Metron; it is recorded so the collection step doesn't add it again.
        Assert.Contains(check.MetronSyncLinks, l => l.Kind == MetronSyncKind.CollectionItem && l.LocalId == libraryIssue);
    }

    // ---- collection ----

    [Fact]
    public async Task Collection_EveryIssueWithAFileIsAddedOnce_AndOnlyRatingChangesAreSent()
    {
        Enable(s => s.MetronSyncCollection = true);
        int rated = AddIssue(metronId: 7001, rating: 3f);
        AddIssue(metronId: 7002);
        AddIssue(metronId: 7003, hasFile: false);                        // no file: not owned
        AddIssue();                                                      // no id at all: can't be sent

        var first = await Sync().RunAsync(CancellationToken.None);
        Assert.Equal(2, first.CollectionAdded);
        Assert.Equal(0, first.RatingsSent);                               // sent on the next run, once the item exists

        var second = await Sync().RunAsync(CancellationToken.None);
        Assert.Equal((0, 1), (second.CollectionAdded, second.RatingsSent));
        Assert.Equal(3, _account.Collection.Values.Single(i => i.IssueId == 7001).Rating);

        _account.Calls.Clear();
        await Sync().RunAsync(CancellationToken.None);
        Assert.Empty(_account.Calls);                                     // settled: not a single request

        using (var context = Db())
        {
            context.Issues.Single(i => i.Id == rated).Rating = null;      // the user cleared it
            context.SaveChanges();
        }

        await Sync().RunAsync(CancellationToken.None);
        Assert.Null(_account.Collection.Values.Single(i => i.IssueId == 7001).Rating);
    }

    [Fact]
    public async Task Collection_NothingIsEverDeleted_AndWhatMetronRefusesIsNotAskedAgain()
    {
        Enable(s => s.MetronSyncCollection = true);
        int gone = AddIssue(metronId: 7001);
        AddIssue(metronId: 6666);
        _account.UnknownIssues.Add(6666);
        await Sync().RunAsync(CancellationToken.None);

        using (var context = Db())
        {
            context.Issues.Remove(context.Issues.Single(i => i.Id == gone));    // removed from the library
            context.SaveChanges();
        }

        _account.Calls.Clear();
        await Sync().RunAsync(CancellationToken.None);

        Assert.Empty(_account.Calls);
        Assert.Single(_account.Collection);                                // still on Metron
    }

    // ---- one run at a time ----

    /// <summary>What the first real use hit: the hourly task and the Sync now button together, both recording the same ledger row.</summary>
    [Fact]
    public async Task ASecondRunWhileOneIsGoing_IsTurnedAway_AndSendsNothing()
    {
        Enable(s => s.MetronSyncPullList = true);
        Follow(100);
        var release = new TaskCompletionSource();
        _account.HoldPullList = release.Task;

        var first = Sync().RunAsync(CancellationToken.None);          // parked inside its first request
        var second = await Sync().RunAsync(CancellationToken.None);
        var import = await Sync().ImportAsync(CancellationToken.None);
        release.SetResult();
        var finished = await first;

        Assert.Equal(MetronAccountSync.AlreadyRunningMessage, second.StoppedBecause);
        Assert.False(second.Failed);
        Assert.Equal(MetronAccountSync.AlreadyRunningMessage, import.StoppedBecause);
        Assert.Equal(1, finished.PullListAdded);
        Assert.Equal(new[] { "pull:list", "pull:add:100" }, _account.Calls);     // one run's worth, not two

        _account.HoldPullList = null;
        Assert.Null((await Sync().RunAsync(CancellationToken.None)).StoppedBecause);   // and the gate is free again afterwards
    }

    // ---- budget, limits, failures ----

    [Fact]
    public async Task ARunStopsAtItsBudget_AndTheNextOneCarriesOn()
    {
        Enable(s => s.MetronSyncCollection = true);
        for (int i = 0; i < 5; i++)
        {
            AddIssue(metronId: 7000 + i);
        }

        var first = await Sync(budget: 3).RunAsync(CancellationToken.None);
        var second = await Sync(budget: 3).RunAsync(CancellationToken.None);

        Assert.Equal((3, 3), (first.CollectionAdded, first.Requests));
        Assert.NotNull(first.StoppedBecause);
        Assert.False(first.Failed);
        Assert.Equal(2, second.CollectionAdded);
        Assert.Null(second.StoppedBecause);
        Assert.Equal(5, _account.Collection.Count);
    }

    [Fact]
    public async Task ARateLimitAnswer_StopsTheRunQuietly_AndLosesNothing()
    {
        Enable(s => s.MetronSyncReading = true);
        Finish(AddIssue(metronId: 7001));
        _account.Throw = new ComicVineException("Metron's rate limit was reached; try again in a minute.", 107);

        var limited = await Sync().RunAsync(CancellationToken.None);
        _account.Throw = null;
        var after = await Sync().RunAsync(CancellationToken.None);

        Assert.NotNull(limited.StoppedBecause);
        Assert.False(limited.Failed);
        Assert.Equal(1, after.ReadsSent);
    }

    [Fact]
    public async Task ARefusedLogin_IsAFailureTheUserIsTold()
    {
        Enable(s => s.MetronSyncPullList = true);
        _account.Throw = new ComicVineException("Metron rejected your login. Check it under Preferences → Connections.", 100);

        var report = await Sync().RunAsync(CancellationToken.None);

        Assert.True(report.Failed);
        Assert.Contains("rejected your login", report.Summary);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0f, null)]
    [InlineData(0.4f, 1)]
    [InlineData(2.5f, 3)]
    [InlineData(4.4f, 4)]
    [InlineData(5f, 5)]
    public void Stars_RoundsToMetronsOneToFive(float? rating, int? expected) => Assert.Equal(expected, MetronAccountSync.Stars(rating));

    // ---- import ----

    [Fact]
    public async Task Import_FillsWhatIsMissing_NeverOverwrites_AndItsReadsAreNotSentBack()
    {
        Enable(s => s.MetronSyncReading = s.MetronSyncCollection = true);
        int unrated = AddIssue(metronId: 7001);
        int alreadyRated = AddIssue(metronId: 7002, rating: 2f);
        int alreadyRead = AddIssue(metronId: 7003);
        using (var context = Db())
        {
            IssueReadStateResolver.MarkAsRead(context.Issues.Single(i => i.Id == alreadyRead));
            context.SaveChanges();
        }

        _account.Collection[1] = new MetronCollectionItem(1, 7001, true, Now.AddDays(-10), 5);
        _account.Collection[2] = new MetronCollectionItem(2, 7002, false, null, 5);
        _account.Collection[3] = new MetronCollectionItem(3, 7003, true, Now.AddDays(-5), null);
        _account.Collection[4] = new MetronCollectionItem(4, 9999, true, Now, 4);            // not in this library

        var report = await Sync().ImportAsync(CancellationToken.None);

        Assert.Equal(new MetronImportReport(3, 1, 1, null), report);
        using (var context = Db())
        {
            Assert.Equal(5f, context.Issues.Single(i => i.Id == unrated).Rating);
            Assert.True(context.Issues.Single(i => i.Id == unrated).HasBeenRead());
            Assert.Equal(2f, context.Issues.Single(i => i.Id == alreadyRated).Rating);       // ours stands
            Assert.Equal(Now.AddDays(-10), context.ReadingEvents.Single().TimestampUtc);
        }

        _account.Calls.Clear();
        var sync = await Sync().RunAsync(CancellationToken.None);

        Assert.Equal(0, sync.ReadsSent);                                                     // the imported read stays where it came from
        Assert.Equal(0, sync.CollectionAdded);                                               // all three are known to be there
        Assert.Equal(new[] { "collection:rate:2:2" }, _account.Calls);                       // our differing rating is the one change to send
    }
}
