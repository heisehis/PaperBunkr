using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Drop-off watch, Up Next and the Wanted affinity score (docs/superpowers/specs/2026-10-06-smart-features-design.md §4.3-§4.5): all
/// three are pure over per-series progress, so they are tested on in-memory series with no database.
/// </summary>
public class ReadingBehaviourResolverTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
    private static int _nextId = 1;

    /// <summary>A series with <paramref name="read"/> read issues then <paramref name="unread"/> unread ones, last opened <paramref name="daysAgo"/> days ago.</summary>
    private static Series Series(string name, int read, int unread, int? daysAgo, ReadingStatus status = ReadingStatus.Reading, int inProgress = 0)
    {
        var series = new Series { Id = _nextId++, Name = name, ReadingStatus = status };
        int number = 1;
        for (int i = 0; i < read; i++)
        {
            series.Issues.Add(Issue(series, number++, lastPage: 19, opened: daysAgo is int d ? Now.AddDays(-d) : null));
        }

        for (int i = 0; i < inProgress; i++)
        {
            series.Issues.Add(Issue(series, number++, lastPage: 5, opened: daysAgo is int d ? Now.AddDays(-d) : null));
        }

        for (int i = 0; i < unread; i++)
        {
            series.Issues.Add(Issue(series, number++));
        }

        return series;
    }

    private static Issue Issue(Series series, int number, int? lastPage = null, DateTime? opened = null) => new()
    {
        Id = _nextId++,
        Series = series,
        SeriesId = series.Id,
        Number = number.ToString(),
        PageCount = 20,
        LastPageRead = lastPage,
        OpenedTime = opened,
        FilePath = $"C:\\x\\{series.Name}-{number}.cbz",
    };

    private static DateTime? NoEvents(int seriesId) => null;

    // ----- Drop-off -----

    [Fact]
    public void DropOff_FindsTheMostCommonStallPoint_AndTheActiveSeriesOneIssueShortOfIt()
    {
        var series = new List<Series>
        {
            Series("Stalled A", 3, 5, 40), Series("Stalled B", 3, 5, 60), Series("Stalled C", 3, 2, 90),
            Series("Stalled D", 1, 9, 30), Series("Stalled E", 6, 1, 200),
            Series("At risk", 2, 8, 2), Series("Past it", 5, 8, 2), Series("Just started", 1, 8, 2),
        };

        var watch = DropOffResolver.Build(series, NoEvents, Now);

        Assert.NotNull(watch);
        Assert.Equal(3, watch!.Cliff);
        Assert.Equal(5, watch.StalledSeriesCount);
        var atRisk = Assert.Single(watch.AtRisk);
        Assert.Equal("At risk", atRisk.SeriesName);
        Assert.Equal(2, atRisk.IssuesRead);
        Assert.Equal("You often stop after about 3 issues. 1 series is at issue 2.", watch.Headline);
    }

    [Fact]
    public void DropOff_WithFewerThanFiveStalledSeries_SaysNothing()
    {
        var series = new List<Series>
        {
            Series("Stalled A", 3, 5, 40), Series("Stalled B", 3, 5, 60), Series("Stalled C", 3, 2, 90), Series("Stalled D", 3, 9, 30),
            Series("At risk", 2, 8, 2),
        };

        Assert.Null(DropOffResolver.Build(series, NoEvents, Now));
    }

    [Fact]
    public void DropOff_WithNothingNearTheCliff_SaysNothing()
    {
        var series = Enumerable.Range(0, 5).Select(i => Series($"Stalled {i}", 3, 5, 40)).Append(Series("Far past", 9, 8, 2)).ToList();

        Assert.Null(DropOffResolver.Build(series, NoEvents, Now));
    }

    [Fact]
    public void DropOff_IgnoresDroppedAndCompletedSeries_AndPausedOnesAreNotAtRisk()
    {
        var series = Enumerable.Range(0, 5).Select(i => Series($"Stalled {i}", 3, 5, 40)).ToList();
        series.Add(Series("Dropped on purpose", 1, 5, 40, ReadingStatus.Dropped));     // would otherwise pull the cliff
        series.Add(Series("Paused", 2, 8, 2, ReadingStatus.Paused));
        series.Add(Series("Reading", 2, 8, 2));

        var watch = DropOffResolver.Build(series, NoEvents, Now);

        Assert.Equal(3, watch!.Cliff);
        Assert.Equal("Reading", Assert.Single(watch.AtRisk).SeriesName);
    }

    [Fact]
    public void DropOff_ATie_GoesToTheEarlierStallPoint()
    {
        var series = new List<Series>
        {
            Series("A", 2, 5, 40), Series("B", 2, 5, 40), Series("C", 2, 5, 40),
            Series("D", 4, 5, 40), Series("E", 4, 5, 40), Series("F", 4, 5, 40),
            Series("First issue done", 1, 8, 1),
        };

        var watch = DropOffResolver.Build(series, NoEvents, Now);

        Assert.Equal(2, watch!.Cliff);
        Assert.Equal("First issue done", Assert.Single(watch.AtRisk).SeriesName);
    }

    // ----- Up Next -----

    [Fact]
    public void UpNext_PicksTheNextUnreadIssueInOrder_ForSeriesYouHaveStarted()
    {
        var saga = Series("Saga", 2, 6, 3);
        var untouched = Series("Never opened", 0, 5, null);

        var item = Assert.Single(UpNextResolver.Build([saga, untouched], [], NoEvents, Now));

        Assert.Equal("Saga", item.SeriesName);
        Assert.Equal("#3", item.IssueLabel);
        Assert.Equal(saga.Issues[2].Id, item.IssueId);
        Assert.Equal(20, item.PagesLeft);
    }

    [Fact]
    public void UpNext_LeavesOutSeriesWithAnIssueInProgress_DroppedAndPausedSeries_AndMissingFiles()
    {
        var inProgress = Series("In Continue Reading", 2, 4, 1, inProgress: 1);
        var dropped = Series("Dropped", 2, 4, 1, ReadingStatus.Dropped);
        var paused = Series("Paused", 2, 4, 1, ReadingStatus.Paused);
        var missing = Series("Next file missing", 2, 1, 1);
        missing.Issues[2].FileIsMissing = true;
        var good = Series("Good", 2, 4, 1);

        var items = UpNextResolver.Build([inProgress, dropped, paused, missing, good], [], NoEvents, Now);

        Assert.Equal("Good", Assert.Single(items).SeriesName);
    }

    [Fact]
    public void UpNext_Ranks_AlmostDone_Above_ListNext_Above_PlainRecent_AndNamesTheStrongestReason()
    {
        var almost = Series("Almost done", 5, 2, 10);
        var listed = Series("In a list", 1, 8, 10);
        var recent = Series("Recent", 1, 8, 10);
        var list = new ReadingList { Name = "Event" };
        list.Items.Add(new ReadingListItem { IssueId = listed.Issues[0].Id, SortOrder = 0 });
        list.Items.Add(new ReadingListItem { IssueId = listed.Issues[1].Id, SortOrder = 1 });

        var items = UpNextResolver.Build([recent, listed, almost], [list], NoEvents, Now);

        Assert.Equal(["Almost done", "In a list", "Recent"], items.Select(i => i.SeriesName));
        Assert.Equal([UpNextReason.AlmostDone, UpNextReason.NextInList, UpNextReason.Recent], items.Select(i => i.Reason));
        Assert.Equal("Almost done: 2 issues left", items[0].ReasonText);
        Assert.Equal("Next in your reading list", items[1].ReasonText);
    }

    [Fact]
    public void UpNext_AStalledSeries_IsResurfaced_WithHowLongItHasBeen()
    {
        var stalled = Series("Stalled", 2, 8, 50);
        var longGone = Series("Long gone", 2, 8, 400);

        var items = UpNextResolver.Build([longGone, stalled], [], NoEvents, Now);

        Assert.Equal(["Stalled", "Long gone"], items.Select(i => i.SeriesName));
        Assert.All(items, i => Assert.Equal(UpNextReason.Stalled, i.Reason));
        Assert.Equal("Stalled 50 days", items[0].ReasonText);
    }

    [Fact]
    public void UpNext_AStartedReadingList_BringsInASeriesYouHaveNotStarted()
    {
        var started = Series("Started", 1, 0, 2);
        var crossover = Series("Crossover", 0, 3, null);
        var list = new ReadingList { Name = "Event" };
        list.Items.Add(new ReadingListItem { IssueId = started.Issues[0].Id, SortOrder = 0 });
        list.Items.Add(new ReadingListItem { IssueId = crossover.Issues[1].Id, SortOrder = 1 });

        var item = Assert.Single(UpNextResolver.Build([started, crossover], [list], NoEvents, Now));

        Assert.Equal("Crossover", item.SeriesName);
        Assert.Equal("#2", item.IssueLabel);     // the list's entry, not the series' first issue
        Assert.Equal(UpNextReason.NextInList, item.Reason);
    }

    [Fact]
    public void UpNext_ShowsAtMostFive_AndAReadingEventCountsAsTheLastRead()
    {
        var series = Enumerable.Range(0, 8).Select(i => Series($"S{i}", 1, 8, 30)).ToList();
        int boosted = series[6].Id;

        var items = UpNextResolver.Build(series, [], id => id == boosted ? Now.AddDays(-1) : null, Now);

        Assert.Equal(5, items.Count);
        Assert.Equal("S6", items[0].SeriesName);
    }

    // ----- Wanted affinity -----

    [Fact]
    public void Affinity_CombinesFinishRatioRecencyAndPublisherShare_AndExplainsItself()
    {
        var progress = new SeriesProgress(ReadCount: 8, UnreadCount: 2, LastReadUtc: Now.AddDays(-9));
        string[] finished = ["Image", "Image", "Image", "Image", "Marvel", "Marvel", "DC", "DC", "DC", "DC"];

        var affinity = WantedAffinityScorer.Score("Image", progress, finished, Now);

        // 50 * 0.8 + 30 * (1 - 9/90) + 20 * 0.4
        Assert.Equal(75, affinity.Score);
        Assert.Equal("You finished 80% of the issues you own · read 9 days ago · Image is 40% of what you finish", affinity.Why);
    }

    [Fact]
    public void Affinity_AnUnlinkedSeries_HasOnlyThePublisherSignal()
    {
        string[] finished = ["Image", "Marvel"];

        var affinity = WantedAffinityScorer.Score("Image", local: null, finished, Now);

        Assert.Equal(10, affinity.Score);
    }

    [Fact]
    public void Affinity_WithNoHistoryAtAll_IsZero_AndSaysSo()
    {
        var affinity = WantedAffinityScorer.Score("Image", new SeriesProgress(0, 5, null), [], Now);

        Assert.Equal(0, affinity.Score);
        Assert.Equal("Nothing read from this series or its publisher yet", affinity.Why);
    }

    [Fact]
    public void Affinity_RecencyFadesToNothingByNinetyDays()
    {
        var recent = WantedAffinityScorer.Score(null, new SeriesProgress(5, 5, Now), [], Now);
        var old = WantedAffinityScorer.Score(null, new SeriesProgress(5, 5, Now.AddDays(-200)), [], Now);

        Assert.Equal(55, recent.Score);
        Assert.Equal(25, old.Score);
    }
}
