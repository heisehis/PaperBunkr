using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests.Acquisition;

public class PullListServiceTests : AcquisitionTestBase
{
    private static readonly DateTime Today = new(2026, 9, 23);   // a Wednesday

    private sealed class FakeSource : IPullListSource
    {
        public List<PullListEntry> Entries { get; } = new();
        public Dictionary<int, PullListSeriesInfo> Series { get; } = new();
        public List<int> SeriesLookups { get; } = new();
        public (DateTime From, DateTime To)? Window { get; private set; }
        public int? RateLimitAfterLookups { get; set; }

        public Task<IReadOnlyList<PullListEntry>> GetReleasesAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
        {
            Window = (from, to);
            return Task.FromResult<IReadOnlyList<PullListEntry>>(Entries.ToList());
        }

        public Task<PullListSeriesInfo?> GetSeriesInfoAsync(int seriesId, CancellationToken cancellationToken)
        {
            if (RateLimitAfterLookups is int limit && SeriesLookups.Count >= limit)
            {
                throw new ComicVineException("rate limited", 107);
            }

            SeriesLookups.Add(seriesId);
            return Task.FromResult(Series.TryGetValue(seriesId, out var info) ? info : null);
        }
    }

    private static PullListEntry Entry(int issueId, int seriesId, string series, string number, DateTime store) =>
        new(issueId, seriesId, series, number, store, null, null);

    private int TrackSpawn(ComicProvider provider, int volumeId, bool follow, out Series local)
    {
        local = new Series { Name = "Spawn" };
        Context.Series.Add(local);
        Context.SaveChanges();
        return WantedService.TrackVolume(Context, Volume(volumeId, "Spawn"), local.Id, follow, provider).Id;
    }

    [Fact]
    public async Task Refresh_FetchesTheWindowAroundToday_StoresIt_AndLooksUpEachSeriesOnce()
    {
        var source = new FakeSource();
        source.Entries.Add(Entry(1, 10, "Spawn", "350", Today.AddDays(7)));
        source.Entries.Add(Entry(2, 10, "Spawn", "351", Today.AddDays(14)));
        source.Entries.Add(Entry(3, 11, "Batman", "1", Today));
        source.Series[10] = new PullListSeriesInfo(10, "Spawn", "Image", 1992, 4321);
        source.Series[11] = new PullListSeriesInfo(11, "Batman", "DC Comics", 2016, null);

        int count = await PullListService.RefreshAsync(Context, source, Today, CancellationToken.None);

        Assert.Equal(3, count);
        Assert.Equal((Today.AddDays(-PullListService.DaysBack), Today.AddDays(PullListService.DaysAhead)), source.Window);
        Assert.Equal(3, Context.PullListReleases.Count());
        Assert.Equal("Image", Context.MetronSeries.Single(m => m.SeriesId == 10).Publisher);
        Assert.Equal(4321, Context.MetronSeries.Single(m => m.SeriesId == 10).ComicVineId);
        Assert.NotNull(Context.GetOrCreateAcquisitionSettings().PullListRefreshedAt);

        await PullListService.RefreshAsync(Context, source, Today, CancellationToken.None);
        Assert.Equal(2, source.SeriesLookups.Count);                 // cached: never asked again
        Assert.Equal(3, Context.PullListReleases.Count());           // upserted, not duplicated
    }

    [Fact]
    public async Task Refresh_LooksUpFollowedSeriesFirst_StopsAtTheCap_AndKeepsWhatItHasWhenRateLimited()
    {
        TrackSpawn(ComicProvider.ComicVine, 4321, follow: true, out _);
        var source = new FakeSource();
        source.Entries.Add(Entry(1, 20, "Aardvark", "1", Today.AddDays(7)));
        source.Entries.Add(Entry(2, 21, "Batman", "1", Today.AddDays(7)));
        source.Entries.Add(Entry(3, 22, "Spawn", "350", Today.AddDays(7)));      // matches the followed series by name: first in line

        await PullListService.RefreshAsync(Context, source, Today, CancellationToken.None, maxSeriesLookups: 1);
        Assert.Equal(new[] { 22 }, source.SeriesLookups);

        source.RateLimitAfterLookups = 2;                                         // one more lookup allowed this time, then the limit hits
        await PullListService.RefreshAsync(Context, source, Today, CancellationToken.None);
        Assert.Equal(2, source.SeriesLookups.Count);
        Assert.Equal(2, Context.MetronSeries.Count());                            // the limited lookup left no half-row behind
    }

    [Fact]
    public void Store_DropsReleasesTheSourceNoLongerReturns()
    {
        PullListService.Store(Context, new[] { Entry(1, 10, "Spawn", "350", Today), Entry(2, 10, "Spawn", "351", Today.AddDays(7)) }, DateTime.UtcNow);
        PullListService.Store(Context, new[] { Entry(2, 10, "Spawn", "351", Today.AddDays(8)) }, DateTime.UtcNow);

        var remaining = Assert.Single(Context.PullListReleases);
        Assert.Equal(2, remaining.ExternalIssueId);
        Assert.Equal(Today.AddDays(8), remaining.StoreDate);                      // moved by the publisher: updated
    }

    [Fact]
    public void AFollowedMetronSeries_GetsItsUpcomingReleasesAsWants_ButNotLastWeeks()
    {
        int watchedId = TrackSpawn(ComicProvider.Metron, 10, follow: true, out _);
        PullListService.Store(Context, new[]
        {
            Entry(1, 10, "Spawn", "349", Today.AddDays(-5)),      // already out: shown in the tab, never auto-requested
            Entry(2, 10, "Spawn", "350", Today.AddDays(7)),
            Entry(3, 99, "Batman", "1", Today.AddDays(7)),         // not followed
        }, DateTime.UtcNow);

        int requested = PullListService.PromoteFollowedReleases(Context, Today);

        Assert.Equal(1, requested);
        var wanted = Assert.Single(Context.WantedIssues);
        Assert.Equal(watchedId, wanted.WatchedSeriesId);
        Assert.Equal("350", wanted.IssueNumber);
        Assert.Equal(ComicProvider.Metron, wanted.Provider);
        Assert.Equal(2, wanted.ExternalIssueId);
        Assert.Equal(Today.AddDays(7), wanted.StoreDate);
        Assert.Single(Upcoming());                                                // it shows in Upcoming until its store date

        Assert.Equal(0, PullListService.PromoteFollowedReleases(Context, Today)); // idempotent
    }

    private List<WantedIssue> Upcoming() => WantedService.Upcoming(Context, Today).ToList();

    [Fact]
    public void AFollowedComicVineSeries_IsRecognisedThroughMetronsComicVineId()
    {
        int watchedId = TrackSpawn(ComicProvider.ComicVine, 4321, follow: true, out _);
        Context.MetronSeries.Add(new MetronSeriesInfo { SeriesId = 10, Name = "Spawn", ComicVineId = 4321, FetchedAt = DateTime.UtcNow });
        Context.MetronSeries.Add(new MetronSeriesInfo { SeriesId = 12, Name = "Spawn", ComicVineId = 9999, FetchedAt = DateTime.UtcNow });   // a different Spawn
        Context.SaveChanges();
        PullListService.Store(Context, new[] { Entry(1, 10, "Spawn", "350", Today.AddDays(7)), Entry(2, 12, "Spawn", "1", Today.AddDays(7)) }, DateTime.UtcNow);

        Assert.Equal(1, PullListService.PromoteFollowedReleases(Context, Today));

        var wanted = Assert.Single(Context.WantedIssues);
        Assert.Equal(watchedId, wanted.WatchedSeriesId);
        Assert.Equal(ComicProvider.Metron, wanted.Provider);                       // the want carries Metron's id even though the series is ComicVine's
        Assert.Equal("350", wanted.IssueNumber);
    }

    [Fact]
    public void ANumberTheLibraryOwnsOrTheSeriesAlreadyWants_IsNotRequestedAgain()
    {
        int watchedId = TrackSpawn(ComicProvider.Metron, 10, follow: true, out var local);
        Context.Issues.Add(new Issue { SeriesId = local.Id, Number = "350", FilePath = "C:\\x\\350.cbz" });
        Context.SaveChanges();
        var watched = Context.WatchedSeries.Single();
        WantedService.Request(Context, watched, new ComicVineIssue(777, "351", null, Today.AddDays(14), null, null, 10));
        PullListService.Store(Context, new[]
        {
            Entry(1, 10, "Spawn", "350", Today.AddDays(7)),        // owned
            Entry(2, 10, "Spawn", "351", Today.AddDays(14)),       // already wanted (under a different id)
            Entry(3, 10, "Spawn", "352", Today.AddDays(21)),
        }, DateTime.UtcNow);

        Assert.Equal(1, PullListService.PromoteFollowedReleases(Context, Today));
        Assert.Equal(new[] { "351", "352" }, Context.WantedIssues.Where(w => w.WatchedSeriesId == watchedId).AsEnumerable().Select(w => w.IssueNumber).OrderBy(n => n));
    }

    [Fact]
    public void AWantFromTheWeeklyList_HidesTheSameNumberFromAComicVineCatalogsMissingList()
    {
        TrackSpawn(ComicProvider.ComicVine, 4321, follow: true, out _);
        var watched = Context.WatchedSeries.Single();
        WantedService.RefreshCatalog(Context, watched, new[]
        {
            new ComicVineIssue(5001, "350", null, Today.AddDays(7), null, null, 4321),
            new ComicVineIssue(5002, "351", null, Today.AddDays(14), null, null, 4321),
        });
        var release = new PullListRelease { ExternalIssueId = 1, SeriesId = 10, SeriesName = "Spawn", IssueNumber = "350", StoreDate = Today.AddDays(7) };

        Assert.NotNull(WantedService.RequestFromRelease(Context, watched, release));

        var missing = WantedService.GetMissing(Context, watched);
        Assert.Equal("351", Assert.Single(missing).IssueNumber);                   // 350 is wanted (from the pull list), so it is no longer "missing"
        Assert.Equal(1, WantedService.PromoteFollowedUpcoming(Context, Today));      // and the catalog path only requests the remaining one
        Assert.Equal(2, Context.WantedIssues.Count());
    }

    [Fact]
    public void IsDue_RefetchesAboutTwiceADay_AndAnHourApartForManualRuns()
    {
        var now = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(PullListService.IsDue(Context, now, manual: false));           // never fetched

        Context.GetOrCreateAcquisitionSettings().PullListRefreshedAt = now.AddMinutes(-30);
        Context.SaveChanges();
        Assert.False(PullListService.IsDue(Context, now, manual: false));
        Assert.False(PullListService.IsDue(Context, now, manual: true));

        Context.GetOrCreateAcquisitionSettings().PullListRefreshedAt = now.AddHours(-2);
        Context.SaveChanges();
        Assert.False(PullListService.IsDue(Context, now, manual: false));
        Assert.True(PullListService.IsDue(Context, now, manual: true));

        Context.GetOrCreateAcquisitionSettings().PullListRefreshedAt = now.AddHours(-13);
        Context.SaveChanges();
        Assert.True(PullListService.IsDue(Context, now, manual: false));
    }

    [Fact]
    public void AHiddenRelease_IsNeverRequested_AndStaysHiddenThroughARefresh()
    {
        TrackSpawn(ComicProvider.Metron, 10, follow: true, out _);
        PullListService.Store(Context, new[] { Entry(1, 10, "Spawn", "350", Today.AddDays(7)), Entry(2, 10, "Spawn", "351", Today.AddDays(14)) }, DateTime.UtcNow);
        Context.PullListReleases.Single(r => r.ExternalIssueId == 1).IsHidden = true;
        Context.SaveChanges();

        var created = PullListService.PromoteFollowedReleasesDetailed(Context, Today);

        var wanted = Assert.Single(created);
        Assert.Equal("351", wanted.IssueNumber);
        Assert.Equal("Spawn", wanted.WatchedSeries!.Name);                          // loaded, so the caller can name it

        PullListService.Store(Context, new[] { Entry(1, 10, "Spawn", "350", Today.AddDays(9)), Entry(2, 10, "Spawn", "351", Today.AddDays(14)) }, DateTime.UtcNow);
        Assert.True(Context.PullListReleases.Single(r => r.ExternalIssueId == 1).IsHidden);   // a refresh (even a moved date) keeps the choice
    }

    [Fact]
    public void ASeriesWithoutACover_TakesTheFirstIssuesCover_ButNeverReplacesOneItHas()
    {
        var metron = WantedService.TrackVolume(Context, new ComicVineVolume(10, "Spawn", "Image", 1992, 300, null), null, false, ComicProvider.Metron);
        WantedService.RefreshCatalog(Context, metron, new[]
        {
            new ComicVineIssue(2, "2", null, Today.AddDays(-30), null, "https://x/second.jpg", 10),
            new ComicVineIssue(1, "1", null, Today.AddDays(-60), null, "https://x/first.jpg", 10),
            new ComicVineIssue(3, "3", null, Today.AddDays(-5), null, null, 10),
        });
        Assert.Equal("https://x/first.jpg", metron.CoverImageUrl);                 // the earliest issue stands in for the series

        var comicVine = WantedService.TrackVolume(Context, new ComicVineVolume(20, "Batman", "DC", 2016, 50, "https://cv/volume.jpg"), null, false);
        WantedService.RefreshCatalog(Context, comicVine, new[] { new ComicVineIssue(9, "1", null, Today, null, "https://cv/issue.jpg", 20) });
        Assert.Equal("https://cv/volume.jpg", comicVine.CoverImageUrl);            // ComicVine's own volume cover is kept
    }

    [Fact]
    public void CachedCovers_GivesTheNewestReleaseCoverPerSeries_AndNothingForSeriesNotInTheList()
    {
        Context.PullListReleases.AddRange(
            new PullListRelease { ExternalIssueId = 1, SeriesId = 10, SeriesName = "Spawn", IssueNumber = "349", StoreDate = Today.AddDays(-7), CoverImageUrl = "https://x/old.jpg" },
            new PullListRelease { ExternalIssueId = 2, SeriesId = 10, SeriesName = "Spawn", IssueNumber = "350", StoreDate = Today.AddDays(7), CoverImageUrl = "https://x/new.jpg" },
            new PullListRelease { ExternalIssueId = 3, SeriesId = 11, SeriesName = "Batman", IssueNumber = "1", StoreDate = Today, CoverImageUrl = null });
        Context.SaveChanges();

        var covers = PullListService.CachedCovers(Context, new[] { 10, 11, 12 });

        Assert.Equal("https://x/new.jpg", covers[10]);
        Assert.False(covers.ContainsKey(11));                                       // its release has no image
        Assert.False(covers.ContainsKey(12));                                       // not in the window
    }
}
