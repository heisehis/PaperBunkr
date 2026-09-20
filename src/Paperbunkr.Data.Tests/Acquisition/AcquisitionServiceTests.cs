using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.Data.Tests.Acquisition;

public abstract class AcquisitionTestBase : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_acq_svc_{Guid.NewGuid():N}.db");
    protected readonly PaperbunkrDbContext Context;

    /// <summary>A second, independent context on the same database (what a service under test gets from its context factory).</summary>
    protected PaperbunkrDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath};Foreign Keys=True").Options);

    protected AcquisitionTestBase()
    {
        Context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath};Foreign Keys=True").Options);
        Context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Context.Dispose();
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    protected static ComicVineVolume Volume(int id, string name, int? startYear = 2016, int count = 50) => new(id, name, "Image", startYear, count, null);

    protected static ComicVineIssue CvIssue(int id, string number, DateTime? storeDate = null, int volumeId = 100) =>
        new(id, number, null, storeDate, null, null, volumeId);

    protected sealed class FakeComicVine : IComicVineClient
    {
        public List<ComicVineVolume> SearchResults { get; } = new();
        public Dictionary<int, List<ComicVineIssue>> IssuesByVolume { get; } = new();
        public int SearchCalls { get; private set; }
        public int IssueCalls { get; private set; }
        public Exception? ThrowOnSearch { get; set; }

        public Task<IReadOnlyList<ComicVineVolume>> SearchVolumesAsync(string query, CancellationToken cancellationToken)
        {
            SearchCalls++;
            if (ThrowOnSearch is not null) throw ThrowOnSearch;
            return Task.FromResult<IReadOnlyList<ComicVineVolume>>(SearchResults.ToList());
        }

        public Task<ComicVineVolume?> GetVolumeAsync(int volumeId, CancellationToken cancellationToken) =>
            Task.FromResult(SearchResults.FirstOrDefault(v => v.Id == volumeId));

        public Task<IReadOnlyList<ComicVineIssue>> GetVolumeIssuesAsync(int volumeId, CancellationToken cancellationToken)
        {
            IssueCalls++;
            return Task.FromResult<IReadOnlyList<ComicVineIssue>>(IssuesByVolume.TryGetValue(volumeId, out var list) ? list : new List<ComicVineIssue>());
        }
    }
}

public class WantedServiceTests : AcquisitionTestBase
{
    private (WatchedSeries Watched, Series Series) Tracked(params ComicVineIssue[] catalog)
    {
        var series = new Series { Name = "Spawn" };
        Context.Series.Add(series);
        Context.SaveChanges();
        var watched = WantedService.TrackVolume(Context, Volume(100, "Spawn"), series.Id, watchFutureReleases: false);
        WantedService.RefreshCatalog(Context, watched, catalog);
        return (watched, series);
    }

    private void Own(Series series, string number, bool placeholder = false, bool missing = false)
    {
        Context.Issues.Add(new Issue { SeriesId = series.Id, Number = number, FilePath = $"C:\\x\\{number}.cbz", IsPlaceholder = placeholder, FileIsMissing = missing });
        Context.SaveChanges();
    }

    [Fact]
    public void TrackVolume_IsIdempotent_KeepsTheLink_AndNeverTurnsWatchOff()
    {
        var series = new Series { Name = "Spawn" };
        Context.Series.Add(series);
        Context.SaveChanges();

        var first = WantedService.TrackVolume(Context, Volume(100, "Spawn"), series.Id, watchFutureReleases: true);
        var second = WantedService.TrackVolume(Context, Volume(100, "Spawn (renamed)"), seriesId: null, watchFutureReleases: false);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, Context.WatchedSeries.Count());
        Assert.Equal(series.Id, second.SeriesId);          // link kept although none was passed the second time
        Assert.True(second.WatchFutureReleases);            // re-tracking can't undo the user's choice
        Assert.Equal("Spawn (renamed)", second.Name);
    }

    [Fact]
    public void RefreshCatalog_InsertsThenUpdates_WithoutDuplicating()
    {
        var (watched, _) = Tracked(CvIssue(1, "1"), CvIssue(2, "2"));

        WantedService.RefreshCatalog(Context, watched, new[] { CvIssue(1, "1"), CvIssue(2, "2b"), CvIssue(3, "3") });

        Assert.Equal(3, Context.CatalogIssues.Count());
        Assert.Equal("2b", Context.CatalogIssues.Single(c => c.ComicVineIssueId == 2).IssueNumber);
        Assert.NotNull(watched.LastRefreshedAt);
    }

    [Fact]
    public void GetMissing_ExcludesOwned_ByNumber_IgnoringPadding()
    {
        var (watched, series) = Tracked(CvIssue(1, "1"), CvIssue(2, "2"), CvIssue(3, "3"));
        Own(series, "02");

        var missing = WantedService.GetMissing(Context, watched);

        Assert.Equal(new[] { "1", "3" }, missing.Select(m => m.IssueNumber));
    }

    [Fact]
    public void GetMissing_TreatsPlaceholdersAndMissingFiles_AsNotOwned()
    {
        var (watched, series) = Tracked(CvIssue(1, "1"), CvIssue(2, "2"));
        Own(series, "1", placeholder: true);
        Own(series, "2", missing: true);

        Assert.Equal(2, WantedService.GetMissing(Context, watched).Count);
    }

    [Fact]
    public void GetMissing_ExcludesAnythingAlreadyWantedOrIgnored()
    {
        var (watched, _) = Tracked(CvIssue(1, "1"), CvIssue(2, "2"), CvIssue(3, "3"));
        WantedService.Request(Context, watched, Context.CatalogIssues.Single(c => c.ComicVineIssueId == 1));
        WantedService.Ignore(Context, watched, Context.CatalogIssues.Single(c => c.ComicVineIssueId == 2));

        Assert.Equal(new[] { "3" }, WantedService.GetMissing(Context, watched).Select(m => m.IssueNumber));
    }

    [Fact]
    public void Request_IsIdempotent_ReopensFailedAndIgnored_ButLeavesInFlightRowsAlone()
    {
        var (watched, _) = Tracked(CvIssue(1, "1"));
        var issue = CvIssue(1, "1");

        var first = WantedService.Request(Context, watched, issue);
        first.Status = WantedIssueStatus.Failed;
        Context.SaveChanges();
        var reopened = WantedService.Request(Context, watched, issue);
        Assert.Equal(first.Id, reopened.Id);
        Assert.Equal(WantedIssueStatus.Wanted, reopened.Status);

        reopened.Status = WantedIssueStatus.Downloading;
        Context.SaveChanges();
        Assert.Equal(WantedIssueStatus.Downloading, WantedService.Request(Context, watched, issue).Status);
        Assert.Equal(1, Context.WantedIssues.Count());
    }

    [Fact]
    public void RequestAllMissing_RequestsEachOnce_AndReturnsTheCount()
    {
        var (watched, series) = Tracked(CvIssue(1, "1"), CvIssue(2, "2"), CvIssue(3, "3"));
        Own(series, "2");

        Assert.Equal(2, WantedService.RequestAllMissing(Context, watched));
        Assert.Equal(0, WantedService.RequestAllMissing(Context, watched));
        Assert.Equal(2, Context.WantedIssues.Count(w => w.Status == WantedIssueStatus.Wanted));
    }

    [Fact]
    public void PromoteFollowedUpcoming_OnlyRequestsFutureIssues_OfFollowedUnpausedSeries()
    {
        var today = new DateTime(2026, 9, 19);
        var (watched, _) = Tracked(CvIssue(1, "1", storeDate: today.AddDays(-30)), CvIssue(2, "2", storeDate: today), CvIssue(3, "3", storeDate: today.AddDays(14)));
        WantedService.SetWatchFutureReleases(Context, watched.Id, true);

        var series2 = new Series { Name = "Other" };
        Context.Series.Add(series2);
        Context.SaveChanges();
        var notFollowed = WantedService.TrackVolume(Context, Volume(200, "Other"), series2.Id, false);
        WantedService.RefreshCatalog(Context, notFollowed, new[] { CvIssue(10, "1", storeDate: today.AddDays(7), volumeId: 200) });

        int requested = WantedService.PromoteFollowedUpcoming(Context, today);

        Assert.Equal(2, requested);                                                     // today's and next fortnight's, not the old gap
        Assert.DoesNotContain(Context.WantedIssues, w => w.ComicVineIssueId == 1);      // past gaps stay the user's "Request missing"
        Assert.DoesNotContain(Context.WantedIssues, w => w.ComicVineIssueId == 10);     // not followed
    }

    [Fact]
    public void PromoteFollowedUpcoming_SkipsPausedSeries()
    {
        var today = new DateTime(2026, 9, 19);
        var (watched, _) = Tracked(CvIssue(1, "1", storeDate: today.AddDays(3)));
        WantedService.SetWatchFutureReleases(Context, watched.Id, true);
        watched.IsPaused = true;
        Context.SaveChanges();

        Assert.Equal(0, WantedService.PromoteFollowedUpcoming(Context, today));
    }

    [Fact]
    public void UpcomingAndDue_SplitWantedRowsByStoreDate()
    {
        var today = new DateTime(2026, 9, 19);
        var (watched, _) = Tracked(CvIssue(1, "1", storeDate: today.AddDays(-1)), CvIssue(2, "2", storeDate: today.AddDays(5)), CvIssue(3, "3"));
        WantedService.RequestAllMissing(Context, watched);

        Assert.Equal(new[] { "2" }, WantedService.Upcoming(Context, today).Select(w => w.IssueNumber));
        Assert.Equal(new[] { "1", "3" }, WantedService.Due(Context, today).OrderBy(w => w.IssueNumber).Select(w => w.IssueNumber));
    }
}

public class IssueNumbersTests
{
    [Theory]
    [InlineData("5", "05", true)]
    [InlineData("#5", "005", true)]
    [InlineData("1.5", "1.5", true)]
    [InlineData("Annual 1", "annual 1", true)]
    [InlineData("5", "15", false)]
    [InlineData("1.5", "1", false)]
    [InlineData("", "", false)]
    [InlineData(null, "1", false)]
    public void Equal(string? a, string? b, bool expected) => Assert.Equal(expected, IssueNumbers.Equal(a, b));

    [Theory]
    [InlineData("The Boys", "Boys", true)]
    [InlineData("X-Men", "X Men", true)]
    [InlineData("Spider-Man", "Spider-Man 2099", false)]
    public void SeriesNamesSame(string a, string b, bool expected) => Assert.Equal(expected, SeriesNames.Same(a, b));
}

public class ArcRequestServiceTests : AcquisitionTestBase
{
    private ReadingList ArcList(params (string Series, string Number, int Year)[] entries)
    {
        var list = new ReadingList { Name = "Arc", Source = "ComicVine", ArcId = "77", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        int order = 0;
        foreach (var (series, number, year) in entries)
        {
            var placeholder = ReadingListMatcher.ResolveOrCreatePlaceholder(Context, series, number, volume: null, year: year, format: null);
            list.Items.Add(new ReadingListItem { IssueId = placeholder.Id, SortOrder = order++ });
        }

        Context.ReadingLists.Add(list);
        Context.SaveChanges();
        return list;
    }

    private static FakeComicVine CvWith(params (ComicVineVolume Volume, string[] Numbers)[] volumes)
    {
        var cv = new FakeComicVine();
        int issueId = 1000;
        foreach (var (volume, numbers) in volumes)
        {
            cv.SearchResults.Add(volume);
            cv.IssuesByVolume[volume.Id] = numbers.Select(n => CvIssue(issueId++, n, volumeId: volume.Id)).ToList();
        }

        return cv;
    }

    [Fact]
    public async Task RequestMissing_TurnsPlaceholdersIntoWantedRows_WithoutFollowingTheSeries()
    {
        var list = ArcList(("Spawn", "263", 2016), ("Spawn", "264", 2016), ("Batman", "5", 2016));
        var cv = CvWith((Volume(100, "Spawn", 1992), new[] { "263", "264", "265" }), (Volume(200, "Batman", 2016), new[] { "5" }));
        var result = await ArcRequestService.RequestMissingAsync(Context, list.Id, cv, CancellationToken.None);

        Assert.Equal(3, result.Requested);
        Assert.Empty(result.Unresolved);
        Assert.Equal(3, Context.WantedIssues.Count(w => w.Status == WantedIssueStatus.Wanted));
        Assert.All(Context.WatchedSeries, w => Assert.False(w.WatchFutureReleases));   // one crossover issue must not subscribe to the series
        Assert.All(Context.WatchedSeries, w => Assert.NotNull(w.SeriesId));            // linked to the placeholder's local series
    }

    [Fact]
    public async Task RequestMissing_IsIdempotent_AndReportsAlreadyTracked()
    {
        var list = ArcList(("Spawn", "263", 2016));
        var cv = CvWith((Volume(100, "Spawn", 1992), new[] { "263" }));

        var first = await ArcRequestService.RequestMissingAsync(Context, list.Id, cv, CancellationToken.None);
        var second = await ArcRequestService.RequestMissingAsync(Context, list.Id, cv, CancellationToken.None);

        Assert.Equal(1, first.Requested);
        Assert.Equal(0, second.Requested);
        Assert.Equal(1, second.AlreadyTracked);
        Assert.Equal(1, Context.WantedIssues.Count());
        Assert.Equal(1, cv.SearchCalls);      // the second run reused the existing WatchedSeries link instead of searching again
        Assert.Equal(1, cv.IssueCalls);       // ...and the cached catalog
    }

    [Fact]
    public async Task AmbiguousVolumes_AreReportedNotGuessed()
    {
        var list = ArcList(("Batman", "5", 0));   // no year to decide between the two Batman volumes
        var cv = CvWith((Volume(1, "Batman", 1940, 700), new[] { "5" }), (Volume(2, "Batman", 2016, 700), new[] { "5" }));

        var result = await ArcRequestService.RequestMissingAsync(Context, list.Id, cv, CancellationToken.None);

        Assert.Equal(0, result.Requested);
        var unresolved = Assert.Single(result.Unresolved);
        Assert.Contains("2 ComicVine volumes", unresolved.Reason);
        Assert.Empty(Context.WantedIssues);
        Assert.Empty(Context.WatchedSeries);
    }

    [Fact]
    public async Task UnknownSeries_AndMissingIssueNumber_AreReported()
    {
        var list = ArcList(("Nonexistent Comic", "1", 2016), ("Spawn", "999", 2016));
        var cv = CvWith((Volume(100, "Spawn", 1992), new[] { "1", "2" }));

        var result = await ArcRequestService.RequestMissingAsync(Context, list.Id, cv, CancellationToken.None);

        Assert.Equal(0, result.Requested);
        Assert.Equal(2, result.Unresolved.Count);
        Assert.Contains(result.Unresolved, u => u.Series == "Nonexistent Comic" && u.Reason.Contains("No ComicVine volume"));
        Assert.Contains(result.Unresolved, u => u.Series == "Spawn" && u.Number == "999" && u.Reason.Contains("not found"));
    }

    [Theory]
    [InlineData(2015, 2010, 2)]   // volumes start 1992 / 2010 / 2016: the newest one that had started by the arc's year
    [InlineData(2017, 2016, 3)]
    [InlineData(1995, 1992, 1)]
    public void ChooseVolume_UsesTheArcYear_ToPickTheNewestVolumeAlreadyStarted(int year, int expectedStart, int expectedId)
    {
        var volumes = new[] { Volume(1, "Batman", 1992), Volume(2, "Batman", 2010), Volume(3, "Batman", 2016) };

        var chosen = ArcRequestService.ChooseVolume(volumes, "Batman", year, out _);

        Assert.Equal(expectedId, chosen!.Id);
        Assert.Equal(expectedStart, chosen.StartYear);
    }

    [Fact]
    public void ChooseVolume_WithoutAYear_AcceptsOnlyAUniqueLargestRun()
    {
        var clear = new[] { Volume(1, "Saga", 2012, 66), Volume(2, "Saga", 2015, 5) };
        Assert.Equal(1, ArcRequestService.ChooseVolume(clear, "Saga", null, out _)!.Id);

        var tied = new[] { Volume(1, "Saga", 2012, 66), Volume(2, "Saga", 2015, 66) };
        Assert.Null(ArcRequestService.ChooseVolume(tied, "Saga", null, out var reason));
        Assert.Contains("2 ComicVine volumes", reason);
    }

    [Fact]
    public async Task BulkRequest_OnANonArcList_IsRefused_ButPerItemRequestsWork()
    {
        var plain = new ReadingList { Name = "Mine", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var placeholder = ReadingListMatcher.ResolveOrCreatePlaceholder(Context, "Spawn", "263", null, 2016, null);
        plain.Items.Add(new ReadingListItem { IssueId = placeholder.Id, SortOrder = 0 });
        Context.ReadingLists.Add(plain);
        Context.SaveChanges();
        var cv = CvWith((Volume(100, "Spawn", 1992), new[] { "263" }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ArcRequestService.RequestMissingAsync(Context, plain.Id, cv, CancellationToken.None));

        var result = await ArcRequestService.RequestPlaceholdersAsync(Context, new[] { placeholder }, cv, CancellationToken.None);
        Assert.Equal(1, result.Requested);
    }

    [Fact]
    public async Task RealIssues_AreNeverRequested()
    {
        var series = new Series { Name = "Spawn" };
        Context.Series.Add(series);
        Context.SaveChanges();
        var owned = new Issue { SeriesId = series.Id, Number = "263", FilePath = "C:\\x\\263.cbz" };
        Context.Issues.Add(owned);
        Context.SaveChanges();
        var cv = CvWith((Volume(100, "Spawn", 1992), new[] { "263" }));

        var result = await ArcRequestService.RequestPlaceholdersAsync(Context, new[] { owned }, cv, CancellationToken.None);

        Assert.Equal(0, result.Requested);
        Assert.Equal(0, cv.SearchCalls);
    }

    [Fact]
    public async Task ARateLimitOrBadKey_AbortsTheWholeRequest_ButOtherFailuresOnlyAffectThatSeries()
    {
        var list = ArcList(("Spawn", "1", 2016));
        var limited = new FakeComicVine { ThrowOnSearch = new ComicVineException("Rate limit", 107) };
        await Assert.ThrowsAsync<ComicVineException>(() => ArcRequestService.RequestMissingAsync(Context, list.Id, limited, CancellationToken.None));

        var flaky = new FakeComicVine { ThrowOnSearch = new ComicVineException("ComicVine request failed") };
        var result = await ArcRequestService.RequestMissingAsync(Context, list.Id, flaky, CancellationToken.None);
        Assert.Equal(0, result.Requested);
        Assert.Contains("request failed", Assert.Single(result.Unresolved).Reason);
    }
}
