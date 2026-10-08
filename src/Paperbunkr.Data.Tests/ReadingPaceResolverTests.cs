using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// The reader's pace and the time-left estimates built on it (docs/superpowers/specs/2026-10-06-smart-features-design.md §4.2): a
/// median over recent real sessions, nothing at all below five of them, and estimates worded to the nearest five minutes.
/// </summary>
public class ReadingPaceResolverTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private static ReadingEvent Session(int pages, int? seconds, int daysAgo = 0, ReadingItemType type = ReadingItemType.Comic) => new()
    {
        ItemType = type,
        Kind = ReadingEventKind.Opened,
        TimestampUtc = Now.AddDays(-daysAgo),
        PagesRead = pages,
        ActiveSeconds = seconds,
    };

    [Fact]
    public void Median_IsTheMiddleRate_SoOneSlowSessionDoesNotSkewIt()
    {
        var events = new[] { Session(10, 300), Session(10, 310), Session(10, 320), Session(10, 330), Session(10, 6000) };

        Assert.Equal(32, ReadingPaceResolver.MedianSecondsPerPage(events, ReadingItemType.Comic));
    }

    [Fact]
    public void Median_OfAnEvenCount_IsTheMeanOfTheTwoMiddleRates()
    {
        var events = new[] { Session(10, 100), Session(10, 200), Session(10, 300), Session(10, 400), Session(10, 500), Session(10, 600) };

        Assert.Equal(35, ReadingPaceResolver.MedianSecondsPerPage(events, ReadingItemType.Comic));
    }

    [Fact]
    public void FewerThanFiveQualifyingSessions_GiveNoPace()
    {
        var events = new[] { Session(10, 300), Session(10, 300), Session(10, 300), Session(10, 300) };

        Assert.Null(ReadingPaceResolver.MedianSecondsPerPage(events, ReadingItemType.Comic));
    }

    [Fact]
    public void ShortSessions_AndRowsWithNoActiveTime_DoNotCount()
    {
        var events = new[]
        {
            Session(10, 300), Session(10, 300), Session(10, 300), Session(10, 300),
            Session(2, 300),      // under three pages
            Session(10, 20),      // under thirty seconds
            Session(10, null),    // written before active time was recorded
        };

        Assert.Null(ReadingPaceResolver.MedianSecondsPerPage(events, ReadingItemType.Comic));
    }

    [Fact]
    public void ComicsAndNovels_HaveSeparatePaces()
    {
        var events = Enumerable.Range(0, 5).Select(_ => Session(10, 300))
            .Concat(Enumerable.Range(0, 5).Select(_ => Session(10, 900, type: ReadingItemType.Novel)))
            .ToList();

        Assert.Equal(30, ReadingPaceResolver.MedianSecondsPerPage(events, ReadingItemType.Comic));
        Assert.Equal(90, ReadingPaceResolver.MedianSecondsPerPage(events, ReadingItemType.Novel));
    }

    [Fact]
    public void OnlyTheNewestThirtySessions_SetThePace()
    {
        var events = Enumerable.Range(0, 30).Select(i => Session(10, 300, daysAgo: i))
            .Concat(Enumerable.Range(0, 40).Select(i => Session(10, 3000, daysAgo: 100 + i)))
            .ToList();

        Assert.Equal(30, ReadingPaceResolver.MedianSecondsPerPage(events, ReadingItemType.Comic));
    }

    [Fact]
    public void TimeLeftInIssue_CountsFromTheCurrentPage_AndIsNullWhenReadOrPaceless()
    {
        var pace = new ReadingPace(ComicSecondsPerPage: 60, NovelSecondsPerPage: null);

        Assert.Equal(TimeSpan.FromMinutes(30), pace.TimeLeftInIssue(new Issue { PageCount = 30 }));
        Assert.Equal(TimeSpan.FromMinutes(20), pace.TimeLeftInIssue(new Issue { PageCount = 30, LastPageRead = 10 }));
        Assert.Null(pace.TimeLeftInIssue(new Issue { PageCount = 30, LastPageRead = 29 }));     // read
        Assert.Null(pace.TimeLeftInIssue(new Issue()));                                         // page count unknown
        Assert.Null(ReadingPace.Unknown.TimeLeftInIssue(new Issue { PageCount = 30 }));
    }

    [Fact]
    public void TimeToFinishSeries_NeedsThreeUnreadIssues_AndSkipsPlaceholdersAndReadOnes()
    {
        var pace = new ReadingPace(60, null);
        var read = new Issue { PageCount = 20, LastPageRead = 19 };
        var placeholder = new Issue { PageCount = 20, IsPlaceholder = true };
        Issue Unread() => new() { PageCount = 20 };

        Assert.Null(pace.TimeToFinishSeries([read, Unread(), Unread(), placeholder]));
        Assert.Equal(TimeSpan.FromMinutes(70), pace.TimeToFinishSeries([read, Unread(), Unread(), Unread(), new Issue { PageCount = 20, LastPageRead = 10 }, placeholder]));
    }

    [Theory]
    [InlineData(2, "<5 min")]
    [InlineData(4.9, "<5 min")]
    [InlineData(5, "~5 min")]
    [InlineData(23, "~25 min")]
    [InlineData(57, "~55 min")]
    [InlineData(58, "~1 h")]
    [InlineData(80, "~1 h 20 min")]
    [InlineData(181, "~3 h")]
    public void Approximate_RoundsToFiveMinutes(double minutes, string expected)
    {
        Assert.Equal(expected, TimeLeftFormatter.Approximate(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void Left_AndToFinishSeries_AddTheirWords_OrSayNothing()
    {
        Assert.Equal("~25 min left", TimeLeftFormatter.Left(TimeSpan.FromMinutes(25)));
        Assert.Equal("~3 h to finish the series", TimeLeftFormatter.ToFinishSeries(TimeSpan.FromHours(3)));
        Assert.Null(TimeLeftFormatter.Left(null));
        Assert.Null(TimeLeftFormatter.ToFinishSeries(null));
    }
}
