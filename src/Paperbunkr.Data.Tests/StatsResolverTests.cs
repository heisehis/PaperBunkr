using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="StatsResolver"/> (docs/superpowers/specs/2026-09-08-stats-v2-mangabaka-
/// design.md §6-7). Reuses <see cref="InsightsResolverTests"/>'s seed helpers - same fixture, two
/// resolvers over it now that Insights and Stats are separate screens.
/// </summary>
public class StatsResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    public StatsResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_stats_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext NewContext() => new(_dbOptions);

    [Fact]
    public void EmptyLibrary_ProducesZeroedSnapshot_WithoutThrowing()
    {
        using var ctx = NewContext();
        var snap = StatsResolver.Build(ctx, InsightsRange.Days90, Now);

        Assert.Equal(0, snap.Lifetime.ItemsRead);
        Assert.Equal(0, snap.ReadingDayStreak.Current);
        Assert.Equal(0, snap.FinishStreak.Current);
        Assert.Empty(snap.Breakdown.ByReadingStatus);
        Assert.Empty(snap.Breakdown.ByMediaType);
        Assert.Null(snap.Highlights.HighestRated);
        Assert.Null(snap.Highlights.LongestJourney);
        Assert.Equal(0, snap.Highlights.PlanToReadCount);
        Assert.Empty(snap.Heatmap);
        Assert.Empty(snap.LibraryGrowth.Points);
        Assert.Empty(snap.TopAuthors);
        Assert.Empty(snap.TopArtists);
    }

    // --- moved from InsightsResolverTests, unchanged behavior --------------------------------

    [Fact]
    public void Lifetime_CountsDistinctFinishedItems_RereadsDoNotInflate()
    {
        using var ctx = NewContext();
        var s = InsightsResolverTests.SeedSeries(ctx, "S");
        var i = InsightsResolverTests.SeedIssue(ctx, s.Id, pageCount: 20);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, i.Id, ReadingEventKind.Finished, Now.AddDays(-10));
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, i.Id, ReadingEventKind.Finished, Now.AddDays(-1)); // re-read

        var snap = StatsResolver.Build(ctx, InsightsRange.Days90, Now);
        Assert.Equal(1, snap.Lifetime.ItemsRead);
        Assert.Equal(20, snap.Lifetime.PagesRead);
    }

    [Fact]
    public void Lifetime_KeepsCountingWhenTheItemRowIsGone()
    {
        using var ctx = NewContext();
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, itemId: 9999, ReadingEventKind.Finished, Now.AddDays(-3));

        var snap = StatsResolver.Build(ctx, InsightsRange.Days90, Now);
        Assert.Equal(1, snap.Lifetime.ItemsRead);
    }

    [Fact]
    public void ReadingDayStreak_CountsConsecutiveLocalDays_FinishStreakIsFinishOnly()
    {
        using var ctx = NewContext();
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, 1, ReadingEventKind.Opened, Now);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, 1, ReadingEventKind.Opened, Now.AddDays(-1));
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, 1, ReadingEventKind.Opened, Now.AddDays(-2));
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, 1, ReadingEventKind.Finished, Now);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, 1, ReadingEventKind.Finished, Now.AddDays(-1));

        var snap = StatsResolver.Build(ctx, InsightsRange.Days90, Now);
        Assert.Equal(3, snap.ReadingDayStreak.Current);
        Assert.Equal(2, snap.FinishStreak.Current);
    }

    [Fact]
    public void Pace_WeeklyBucketsForNinetyDays_MonthlyForTwelveMonths()
    {
        using var ctx = NewContext();
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, 1, ReadingEventKind.Finished, Now.AddDays(-3), pages: 40);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, 2, ReadingEventKind.Finished, Now.AddDays(-40), pages: 10);

        var weekly = StatsResolver.Build(ctx, InsightsRange.Days90, Now).Pace;
        Assert.Equal(13, weekly.Count);
        Assert.Equal(2, weekly.Sum(b => b.Finished));
        Assert.Equal(50, weekly.Sum(b => b.Pages));

        var monthly = StatsResolver.Build(ctx, InsightsRange.Months12, Now).Pace;
        Assert.Equal(12, monthly.Count);
    }

    [Fact]
    public void FinishedInRange_ExcludesEventsOutsideTheWindow()
    {
        using var ctx = NewContext();
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, 1, ReadingEventKind.Finished, Now.AddDays(-10), pages: 30);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, 2, ReadingEventKind.Finished, Now.AddDays(-200), pages: 30);

        var snap = StatsResolver.Build(ctx, InsightsRange.Days90, Now);
        Assert.Equal(1, snap.FinishedInRange.Items);
        Assert.Equal(30, snap.FinishedInRange.Pages);
    }

    [Fact]
    public void Ratings_BucketsRoundedStarsAndExcludesUnrated()
    {
        using var ctx = NewContext();
        var s = InsightsResolverTests.SeedSeries(ctx, "S");
        InsightsResolverTests.SeedIssue(ctx, s.Id, rating: 4.4f);
        InsightsResolverTests.SeedIssue(ctx, s.Id, rating: 4.5f);
        InsightsResolverTests.SeedIssue(ctx, s.Id, rating: null);

        var ratings = StatsResolver.Build(ctx, InsightsRange.Days90, Now).Ratings;
        Assert.Equal(5, ratings.Count);
        Assert.Equal(1, ratings.Single(r => r.Stars == 4).Count);
        Assert.Equal(1, ratings.Single(r => r.Stars == 5).Count);
    }

    // --- Breakdown (replaces the old 3-bucket Completion donut) ------------------------------

    [Fact]
    public void Breakdown_CountsSeriesByRealReadingStatusAndContentType_IncludingUnknown()
    {
        using var ctx = NewContext();
        var reading = InsightsResolverTests.SeedSeries(ctx, "A", ReadingStatus.Reading);
        InsightsResolverTests.SeedIssue(ctx, reading.Id);
        var unknown = InsightsResolverTests.SeedSeries(ctx, "B"); // default ReadingStatus.Unknown
        InsightsResolverTests.SeedIssue(ctx, unknown.Id);

        var breakdown = StatsResolver.Build(ctx, InsightsRange.Days90, Now).Breakdown;

        Assert.Equal(1, breakdown.ByReadingStatus.Single(s => s.Label == nameof(ReadingStatus.Reading)).Count);
        Assert.Equal(1, breakdown.ByReadingStatus.Single(s => s.Label == nameof(ReadingStatus.Unknown)).Count);
    }

    // --- Highlights: the backfill-exclusion logic is the important new behavior --------------

    [Fact]
    public void LongestJourney_ExcludesBackfilledPairs_IncludesRealOnes()
    {
        using var ctx = NewContext();
        var s = InsightsResolverTests.SeedSeries(ctx, "S");
        var backfilled = InsightsResolverTests.SeedIssue(ctx, s.Id);
        var real = InsightsResolverTests.SeedIssue(ctx, s.Id);

        // Backfilled: Opened and Finished at the exact same instant (ReadingEventBackfill's signature).
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, backfilled.Id, ReadingEventKind.Opened, Now.AddDays(-100));
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, backfilled.Id, ReadingEventKind.Finished, Now.AddDays(-100));

        // Real: Opened then Finished 5 days later.
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, real.Id, ReadingEventKind.Opened, Now.AddDays(-10));
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, real.Id, ReadingEventKind.Finished, Now.AddDays(-5));

        var highlights = StatsResolver.Build(ctx, InsightsRange.Days90, Now).Highlights;

        Assert.NotNull(highlights.LongestJourney);
        Assert.Equal(5, double.Parse(highlights.LongestJourney!.Detail.Split(' ')[0]));
    }

    [Fact]
    public void MostReread_CountsFinishedEventsPerItem_NoBackfillExclusion()
    {
        using var ctx = NewContext();
        var s = InsightsResolverTests.SeedSeries(ctx, "S");
        var i = InsightsResolverTests.SeedIssue(ctx, s.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, i.Id, ReadingEventKind.Finished, Now.AddDays(-30));
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, i.Id, ReadingEventKind.Finished, Now.AddDays(-1));

        var highlights = StatsResolver.Build(ctx, InsightsRange.Days90, Now).Highlights;

        Assert.NotNull(highlights.MostReread);
        Assert.Contains(s.Name, highlights.MostReread!.Titles);
    }

    [Fact]
    public void Highlights_TiesListAllTitles()
    {
        using var ctx = NewContext();
        var a = InsightsResolverTests.SeedSeries(ctx, "A");
        var b = InsightsResolverTests.SeedSeries(ctx, "B");
        InsightsResolverTests.SeedIssue(ctx, a.Id, rating: 5f);
        InsightsResolverTests.SeedIssue(ctx, b.Id, rating: 5f);

        var highest = StatsResolver.Build(ctx, InsightsRange.Days90, Now).Highlights.HighestRated;

        Assert.NotNull(highest);
        Assert.Equal(2, highest!.Titles.Count);
    }

    [Fact]
    public void PlanToRead_And_ZeroProgress_ExcludeEachOther()
    {
        using var ctx = NewContext();
        var planned = InsightsResolverTests.SeedSeries(ctx, "Planned", ReadingStatus.Planned);
        InsightsResolverTests.SeedIssue(ctx, planned.Id); // never opened, but Planned - counts as PlanToRead, not ZeroProgress

        var untouched = InsightsResolverTests.SeedSeries(ctx, "Untouched", ReadingStatus.Unknown);
        InsightsResolverTests.SeedIssue(ctx, untouched.Id); // never opened, not Planned - ZeroProgress

        var opened = InsightsResolverTests.SeedSeries(ctx, "Opened", ReadingStatus.Unknown);
        var openedIssue = InsightsResolverTests.SeedIssue(ctx, opened.Id, openedTime: Now.AddDays(-1));

        var highlights = StatsResolver.Build(ctx, InsightsRange.Days90, Now).Highlights;

        Assert.Equal(1, highlights.PlanToReadCount);
        Assert.Equal(1, highlights.ZeroProgressCount);
    }

    // --- Library Growth / Heatmap / Publication Year / Content Rating ------------------------

    [Fact]
    public void LibraryGrowth_BucketsByAddedTime()
    {
        using var ctx = NewContext();
        var s = InsightsResolverTests.SeedSeries(ctx, "S", ReadingStatus.Reading);
        InsightsResolverTests.SeedIssue(ctx, s.Id, addedTime: Now.AddDays(-100));
        InsightsResolverTests.SeedIssue(ctx, s.Id, addedTime: Now.AddDays(-10));

        var growth = StatsResolver.Build(ctx, InsightsRange.Days90, Now).LibraryGrowth;

        Assert.Equal(2, growth.Points.Count);
        Assert.All(growth.Points, p => Assert.Equal(nameof(ReadingStatus.Reading), p.ReadingStatus));
    }

    [Fact]
    public void Heatmap_CountsEventsPerLocalDay()
    {
        using var ctx = NewContext();
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, 1, ReadingEventKind.Opened, Now);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, 1, ReadingEventKind.Finished, Now);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, 2, ReadingEventKind.Opened, Now.AddDays(-1));

        var heatmap = StatsResolver.Build(ctx, InsightsRange.Days90, Now).Heatmap;

        Assert.Equal(2, heatmap.Count); // two distinct local days
        Assert.Equal(2, heatmap[DateOnly.FromDateTime(Now.ToLocalTime().Date)]);
    }

    [Fact]
    public void PublicationYear_BucketsIssuesWithAYear_IgnoresNull()
    {
        using var ctx = NewContext();
        var s = InsightsResolverTests.SeedSeries(ctx, "S");
        var i1 = InsightsResolverTests.SeedIssue(ctx, s.Id);
        i1.Year = 2020;
        var i2 = InsightsResolverTests.SeedIssue(ctx, s.Id);
        i2.Year = 2020;
        InsightsResolverTests.SeedIssue(ctx, s.Id); // no year
        ctx.SaveChanges();

        var years = StatsResolver.Build(ctx, InsightsRange.Days90, Now).PublicationYear;

        Assert.Equal(2, Assert.Single(years).Count);
        Assert.Equal(2020, years[0].Year);
    }

    [Fact]
    public void ContentRating_GroupsRawStrings_UnknownForBlank()
    {
        using var ctx = NewContext();
        var s = InsightsResolverTests.SeedSeries(ctx, "S");
        var i1 = InsightsResolverTests.SeedIssue(ctx, s.Id);
        i1.AgeRating = "Teen";
        InsightsResolverTests.SeedIssue(ctx, s.Id); // blank -> Unknown
        ctx.SaveChanges();

        var byRating = StatsResolver.Build(ctx, InsightsRange.Days90, Now).ContentRating;

        Assert.Equal(1, byRating.Single(r => r.Label == "Teen").Count);
        Assert.Equal(1, byRating.Single(r => r.Label == "Unknown").Count);
    }

    // --- Top Authors / Top Artists ------------------------------------------------------------

    [Fact]
    public void TopAuthors_SplitsCommaSeparatedWriterField()
    {
        using var ctx = NewContext();
        var s = InsightsResolverTests.SeedSeries(ctx, "S");
        var i = InsightsResolverTests.SeedIssue(ctx, s.Id);
        i.Writer = "Alan Moore, Neil Gaiman";
        ctx.SaveChanges();

        var authors = StatsResolver.Build(ctx, InsightsRange.Days90, Now).TopAuthors;

        Assert.Equal(2, authors.Count);
        Assert.Contains(authors, a => a.Label == "Alan Moore");
        Assert.Contains(authors, a => a.Label == "Neil Gaiman");
    }

    [Fact]
    public void TopArtists_DedupesOnePersonAcrossMultipleRoles_OnTheSameIssue()
    {
        using var ctx = NewContext();
        var s = InsightsResolverTests.SeedSeries(ctx, "S");
        var i = InsightsResolverTests.SeedIssue(ctx, s.Id);
        i.Penciller = "Jim Lee";
        i.Inker = "Jim Lee"; // same person, same issue, two roles - should count once for this issue
        ctx.SaveChanges();

        var artists = StatsResolver.Build(ctx, InsightsRange.Days90, Now).TopArtists;

        Assert.Equal(1, Assert.Single(artists).Count);
    }
}
