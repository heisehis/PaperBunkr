using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

public class RhythmResolverTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly TimeZoneInfo Plus10 = TimeZoneInfo.CreateCustomTimeZone("plus10", TimeSpan.FromHours(10), "plus10", "plus10");

    private static ReadingEvent Open(int item, DateTime utc, int? seconds = null) => new()
    {
        ItemType = ReadingItemType.Comic, ItemId = item, Kind = ReadingEventKind.Opened, TimestampUtc = utc, ActiveSeconds = seconds,
    };

    [Fact]
    public void BucketsByLocalWeekdayAndHour()
    {
        // 2026-10-06 is a Tuesday. 20:00 UTC is 06:00 Wednesday at +10.
        var e = new[] { Open(1, new DateTime(2026, 10, 6, 20, 0, 0, DateTimeKind.Utc)) };
        var utc = RhythmResolver.Compute(e, InsightsRange.AllTime, Now, Utc);
        var shifted = RhythmResolver.Compute(e, InsightsRange.AllTime, Now, Plus10);

        Assert.Equal(1, utc.SessionsByWeekdayHour[(int)DayOfWeek.Tuesday, 20]);
        Assert.Equal(1, shifted.SessionsByWeekdayHour[(int)DayOfWeek.Wednesday, 6]);
        Assert.Equal((int)DayOfWeek.Tuesday, utc.PeakWeekday);
        Assert.Equal(20, utc.PeakHour);
    }

    [Fact]
    public void BackfilledRowsAreExcluded()
    {
        var t = new DateTime(2026, 9, 1, 3, 0, 0, DateTimeKind.Utc);
        var events = new[]
        {
            Open(1, t),
            new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = 1, Kind = ReadingEventKind.Finished, TimestampUtc = t },
            Open(2, new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc)),
        };

        var data = RhythmResolver.Compute(events, InsightsRange.AllTime, Now, Utc);

        Assert.Equal(1, data.SessionCount);
    }

    [Fact]
    public void LengthStatsUseOnlyMeasuredSessions()
    {
        var day = new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
        var events = new[]
        {
            Open(1, day, 120),              // 2 min  -> <5
            Open(2, day.AddHours(1), 1200), // 20 min -> 15-30
            Open(3, day.AddHours(2)),       // unmeasured
        };

        var data = RhythmResolver.Compute(events, InsightsRange.AllTime, Now, Utc);

        Assert.Equal(3, data.SessionCount);
        Assert.Equal(2, data.MeasuredSessionCount);
        Assert.Equal(11.0, data.AverageSessionMinutes!.Value, 3);
        Assert.Equal(1, data.LengthBuckets.Single(b => b.Label == "<5 min").Count);
        Assert.Equal(1, data.LengthBuckets.Single(b => b.Label == "15-30").Count);
    }

    [Fact]
    public void RangeFiltersOldSessions()
    {
        var events = new[]
        {
            Open(1, Now.AddDays(-5)),
            Open(2, Now.AddDays(-200)),
        };

        Assert.Equal(1, RhythmResolver.Compute(events, InsightsRange.Days30, Now, Utc).SessionCount);
        Assert.Equal(2, RhythmResolver.Compute(events, InsightsRange.AllTime, Now, Utc).SessionCount);
    }

    [Fact]
    public void EmptyAndSmallHistoriesHaveNoChart()
    {
        var empty = RhythmResolver.Compute(Array.Empty<ReadingEvent>(), InsightsRange.AllTime, Now, Utc);
        Assert.False(empty.HasChart);
        Assert.Null(empty.AverageSessionMinutes);
        Assert.Null(empty.PeakHour);

        var few = RhythmResolver.Compute(new[] { Open(1, Now.AddDays(-1)) }, InsightsRange.AllTime, Now, Utc);
        Assert.False(few.HasChart);
    }
}
