using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests.BooksRedesign;

/// <summary>The cover card's one-badge rule and its status caption (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §2).</summary>
public class BookCardStatusTests
{
    [Fact]
    public void ReadingWithProgress_ShowsTheBarAndNoBadge_CaptionHasTimeLeft()
    {
        var card = new BookCardSample { Status = BookReadingStatus.Reading, Progress = 0.37, TimeLeft = TimeSpan.FromMinutes(130) };

        Assert.True(card.ShowProgressBar);
        Assert.Equal(BookStatusBadge.None, card.StatusBadge);
        Assert.Equal("37% · 2h 10m left", card.StatusCaption);
    }

    [Fact]
    public void ReadingWithoutAPace_CaptionIsJustThePercentage() =>
        Assert.Equal("12%", new BookCardSample { Status = BookReadingStatus.Reading, Progress = 0.12 }.StatusCaption);

    [Fact]
    public void ReadingButNotStarted_HasNoBarAndSaysReading()
    {
        var card = new BookCardSample { Status = BookReadingStatus.Reading, Progress = 0 };

        Assert.False(card.ShowProgressBar);
        Assert.Equal("Reading", card.StatusCaption);
    }

    [Theory]
    [InlineData(BookReadingStatus.Finished, BookStatusBadge.Finished)]
    [InlineData(BookReadingStatus.WantToRead, BookStatusBadge.WantToRead)]
    [InlineData(BookReadingStatus.Abandoned, BookStatusBadge.Abandoned)]
    [InlineData(BookReadingStatus.Unread, BookStatusBadge.None)]
    public void OtherStatuses_GetOneBadge(BookReadingStatus status, BookStatusBadge badge) =>
        Assert.Equal(badge, new BookCardSample { Status = status }.StatusBadge);

    [Fact]
    public void FinishedAndRated_SaysSo() =>
        Assert.Equal("Finished · rated 4.5", new BookCardSample { Status = BookReadingStatus.Finished, Rating = 4.5f }.StatusCaption);

    [Fact]
    public void UnreadInASeries_CaptionIsTheSeries() =>
        Assert.Equal("The Orchard Cycle #3",
            new BookCardSample { Status = BookReadingStatus.Unread, SeriesName = "The Orchard Cycle", SeriesIndex = 3 }.StatusCaption);

    [Fact]
    public void UnreadStandalone_HasNoCaption()
    {
        var card = new BookCardSample { Status = BookReadingStatus.Unread };

        Assert.False(card.HasStatusCaption);
    }
}
