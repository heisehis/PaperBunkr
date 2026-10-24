using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

public class MilestoneResolverTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTime Day0 = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    private static ReadingEvent Finish(int item, DateTime utc, ReadingItemType type = ReadingItemType.Comic, string? title = "Series") => new()
    {
        ItemType = type, ItemId = item, Kind = ReadingEventKind.Finished, TimestampUtc = utc, SeriesTitle = title,
    };

    private static ReadingEvent Open(int item, DateTime utc) => new()
    {
        ItemType = ReadingItemType.Comic, ItemId = item, Kind = ReadingEventKind.Opened, TimestampUtc = utc,
    };

    private static readonly IReadOnlyDictionary<int, MilestoneIssue> NoIssues = new Dictionary<int, MilestoneIssue>();

    private static MilestoneData Run(IEnumerable<ReadingEvent> events, IReadOnlyDictionary<int, MilestoneIssue>? issues = null, params DateOnly[] goals)
        => MilestoneResolver.Compute(events.ToList(), issues ?? NoIssues, goals, Utc);

    [Fact]
    public void BackfillOnlyHistoryEarnsNothingAndSaysWhy()
    {
        var events = new List<ReadingEvent>();
        for (int i = 1; i <= 12; i++)
        {
            var t = Day0.AddDays(-i);
            events.Add(Open(i, t));
            events.Add(Finish(i, t));
        }

        var data = Run(events);

        Assert.Empty(data.Earned);
        Assert.True(data.OnlyBackfilledHistory);
    }

    [Fact]
    public void FinishedLadderUsesTheNthDistinctItemAndIgnoresRereads()
    {
        var events = new List<ReadingEvent>();
        for (int i = 1; i <= 10; i++)
        {
            events.Add(Open(i, Day0.AddDays(i).AddHours(-1)));
            events.Add(Finish(i, Day0.AddDays(i)));
        }

        // Re-reading item 1 later must not count as an 11th item or move the first-finish date.
        events.Add(Finish(1, Day0.AddDays(40)));

        var data = Run(events);

        var tenth = data.Earned.Single(m => m.Id == "finished-10");
        Assert.Equal(DateOnly.FromDateTime(Day0.AddDays(10)), tenth.Date);
        Assert.Equal(DateOnly.FromDateTime(Day0.AddDays(1)), data.Earned.Single(m => m.Id == "finished-1").Date);
        Assert.DoesNotContain(data.Earned, m => m.Id == "finished-50");
        var next = data.NextUp.Single(p => p.Id == "finished-50");
        Assert.Equal(10, next.Current);
    }

    [Fact]
    public void StreakMilestoneIsDatedAtTheDayTheRunReachedTheThreshold()
    {
        var events = new List<ReadingEvent>();
        for (int d = 0; d < 7; d++)
        {
            events.Add(Open(1, Day0.AddDays(d)));
        }

        // A gap, then a shorter run that must not earn anything.
        events.Add(Open(1, Day0.AddDays(20)));
        events.Add(Open(1, Day0.AddDays(21)));

        var data = Run(events);

        var streak = data.Earned.Single(m => m.Id == "streak-7");
        Assert.Equal(DateOnly.FromDateTime(Day0.AddDays(6)), streak.Date);
        var next = data.NextUp.Single(p => p.Id == "streak-30");
        Assert.Equal(7, next.Current);
    }

    [Fact]
    public void FirstCompleteSeriesIsWhenTheLastIssueFirstFinished()
    {
        var issues = new Dictionary<int, MilestoneIssue>
        {
            [1] = new(1, 10, "Saga", ContentType.Comic, false),
            [2] = new(2, 10, "Saga", ContentType.Comic, false),
            [3] = new(3, 10, "Saga", ContentType.Comic, true),   // placeholder: not owned, not required
            [4] = new(4, 20, "One-shot", ContentType.Comic, false), // single issue: not a "run"
        };
        var events = new[]
        {
            Open(1, Day0), Finish(1, Day0.AddDays(1)),
            Open(2, Day0.AddDays(2)), Finish(2, Day0.AddDays(3)),
            Open(4, Day0), Finish(4, Day0),
        };
        // Item 4 is an Open+Finish at one instant, i.e. backfill-shaped, so also exercise the real path with a distinct open.
        events = events.Where(e => e.ItemId != 4).Concat(new[] { Open(4, Day0.AddHours(-2)), Finish(4, Day0) }).ToArray();

        var data = Run(events, issues);

        var series = data.Earned.Single(m => m.Id == "first-complete-series");
        Assert.Equal("Saga", series.Detail);
        Assert.Equal(DateOnly.FromDateTime(Day0.AddDays(3)), series.Date);
    }

    [Fact]
    public void FirstMangaAndFirstBookAndFirstGoal()
    {
        var issues = new Dictionary<int, MilestoneIssue>
        {
            [1] = new(1, 10, "Berserk", ContentType.Manga, false),
            [2] = new(2, 11, "Batman", ContentType.Comic, false),
        };
        var events = new[]
        {
            Open(2, Day0.AddHours(-1)), Finish(2, Day0),
            Open(1, Day0.AddDays(2).AddHours(-1)), Finish(1, Day0.AddDays(2), title: "Berserk"),
            Finish(7, Day0.AddDays(5), ReadingItemType.Novel, "Dune"),
        };

        var data = Run(events, issues, new DateOnly(2026, 11, 1));

        Assert.Equal(DateOnly.FromDateTime(Day0.AddDays(2)), data.Earned.Single(m => m.Id == "first-manga-volume").Date);
        Assert.Equal("Dune", data.Earned.Single(m => m.Id == "first-book").Detail);
        Assert.Equal(new DateOnly(2026, 11, 1), data.Earned.Single(m => m.Id == "first-goal").Date);
        // Newest first.
        Assert.Equal("first-goal", data.Earned[0].Id);
    }

    [Fact]
    public void EmptyHistoryIsNotReportedAsBackfillOnly()
    {
        var data = Run(Array.Empty<ReadingEvent>());

        Assert.Empty(data.Earned);
        Assert.False(data.OnlyBackfilledHistory);
    }
}
