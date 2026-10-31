using Paperbunkr.App.Models;
using Paperbunkr.App.Services.Reader;
using Xunit;

namespace Paperbunkr.App.Tests;

public class BookJumpTrackerTests
{
    [Fact]
    public void DifferentChapterIsLarge() =>
        Assert.True(BookJumpTracker.IsLargeJump(new BookPosition(1, "pb-p3"), new BookPosition(2, "pb-p4")));

    [Fact]
    public void SameChapterNearbyBlocksIsSmall() =>
        Assert.False(BookJumpTracker.IsLargeJump(new BookPosition(1, "pb-p3"), new BookPosition(1, "pb-p12")));

    [Fact]
    public void SameChapterTenBlocksApartIsLarge() =>
        Assert.True(BookJumpTracker.IsLargeJump(new BookPosition(1, "pb-p3"), new BookPosition(1, "pb-p13")));

    [Fact]
    public void WithoutBlockIdsUsesFraction()
    {
        Assert.True(BookJumpTracker.IsLargeJump(new BookPosition(1, null, 0.1), new BookPosition(1, null, 0.4)));
        Assert.False(BookJumpTracker.IsLargeJump(new BookPosition(1, null, 0.1), new BookPosition(1, null, 0.2)));
    }

    [Fact]
    public void NonNumericBlockIdsFallBackToFraction() =>
        Assert.True(BookJumpTracker.IsLargeJump(new BookPosition(1, "x", 0.0), new BookPosition(1, "y", 0.5)));
}

public class BookSessionSummaryTests
{
    [Fact]
    public void UnderFiveMinutesIsNull() =>
        Assert.Null(BookSessionSummary.Format(299, 10, null));

    [Fact]
    public void MinutesAndPages() =>
        Assert.Equal("32 min · 41 pages", BookSessionSummary.Format(32 * 60, 41, null));

    [Fact]
    public void PagesOmittedWhenZero() =>
        Assert.Equal("5 min", BookSessionSummary.Format(300, 0, null));

    [Fact]
    public void SinglePage() =>
        Assert.Equal("6 min · 1 page", BookSessionSummary.Format(360, 1, null));

    [Fact]
    public void HoursFormat()
    {
        Assert.Equal("1 hr 5 min", BookSessionSummary.Format(65 * 60, 0, null));
        Assert.Equal("2 hr", BookSessionSummary.Format(120 * 60, 0, null));
    }

    [Fact]
    public void GoalFragmentAppended() =>
        Assert.Equal("10 min · Daily pages: 62 of 50", BookSessionSummary.Format(600, 0, "Daily pages: 62 of 50"));
}
