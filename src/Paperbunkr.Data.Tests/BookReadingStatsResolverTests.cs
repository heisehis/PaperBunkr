using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

public class BookReadingStatsResolverTests
{
    private static ReadingEvent Opened(int day, int? seconds = null, int? pages = null, ReadingItemType type = ReadingItemType.Novel) => new()
    {
        ItemType = type, ItemId = 1, Kind = ReadingEventKind.Opened, TimestampUtc = new DateTime(2026, 10, day, 12, 0, 0, DateTimeKind.Utc),
        ActiveSeconds = seconds, PagesRead = pages,
    };

    [Fact]
    public void NoEvents_IsEmpty()
    {
        var stats = BookReadingStatsResolver.Compute(Array.Empty<ReadingEvent>(), 500_000, 0.2, finished: false);

        Assert.False(stats.HasAny);
        Assert.Null(stats.TimeLeft);
    }

    [Fact]
    public void CountsSessionsTimeAndDates_FromOpenedRowsOnly()
    {
        var events = new[]
        {
            Opened(3, 1200, 30), Opened(9, 600, 10),
            new ReadingEvent { ItemType = ReadingItemType.Novel, Kind = ReadingEventKind.Finished, TimestampUtc = new DateTime(2026, 10, 20), PagesRead = 5, ActiveSeconds = 999 },
            Opened(5, 900, 12, ReadingItemType.Comic),
        };

        var stats = BookReadingStatsResolver.Compute(events, null, 0.5, finished: false);

        Assert.Equal(2, stats.Sessions);
        Assert.Equal(TimeSpan.FromSeconds(1800), stats.ActiveTime);
        Assert.Equal(3, stats.FirstReadUtc!.Value.Day);
        Assert.Equal(9, stats.LastReadUtc!.Value.Day);
    }

    [Fact]
    public void Estimate_NeedsTwoQualifyingSessions_AndALength()
    {
        // 1800 chars/page: 40 pages in 40 minutes = 1800 chars/min.
        var two = new[] { Opened(1, 1200, 20), Opened(2, 1200, 20) };

        var stats = BookReadingStatsResolver.Compute(two, characterCount: 360_000, progress: 0.5, finished: false);

        Assert.Equal(1800d, stats.CharsPerMinute!.Value, 3);
        Assert.Equal(TimeSpan.FromMinutes(100), stats.TimeLeft); // 180,000 left / 1,800 per minute

        Assert.Null(BookReadingStatsResolver.Compute(new[] { Opened(1, 1200, 20) }, 360_000, 0.5, false).TimeLeft);
        Assert.Null(BookReadingStatsResolver.Compute(two, null, 0.5, false).TimeLeft);
        Assert.Null(BookReadingStatsResolver.Compute(two, 0, 0.5, false).TimeLeft);
    }

    [Fact]
    public void ShortUntimedOrPagelessSessions_CountAsSessions_ButNotTowardPace()
    {
        var events = new[] { Opened(1, 1200, 20), Opened(2, 30, 5), Opened(3, null, 8), Opened(4, 900, null), Opened(5, 600, 10) };

        var stats = BookReadingStatsResolver.Compute(events, 360_000, 0.0, finished: false);

        Assert.Equal(5, stats.Sessions);
        // Qualifying: day 1 (20 min, 20 pages) and day 5 (10 min, 10 pages): 30 pages * 1800 / 30 min = 1800 chars/min.
        Assert.Equal(1800d, stats.CharsPerMinute!.Value, 3);
        Assert.Equal(TimeSpan.FromMinutes(200), stats.TimeLeft);
    }

    [Fact]
    public void EstimateRoundsToFiveMinutes_AndVanishesWhenFinishedOrDone()
    {
        var two = new[] { Opened(1, 1200, 20), Opened(2, 1200, 20) };

        // 3,600 chars left / 1,800 per minute = 2 minutes: rounds to 0 -> hidden.
        Assert.Null(BookReadingStatsResolver.Compute(two, 360_000, 0.99, false).TimeLeft);
        // 15,300 chars left = 8.5 minutes -> 10.
        Assert.Equal(TimeSpan.FromMinutes(10), BookReadingStatsResolver.Compute(two, 360_000, 1d - (15_300d / 360_000d), false).TimeLeft);
        Assert.Null(BookReadingStatsResolver.Compute(two, 360_000, 0.5, finished: true).TimeLeft);
        Assert.Null(BookReadingStatsResolver.Compute(two, 360_000, 1.0, false).TimeLeft);
    }
}
