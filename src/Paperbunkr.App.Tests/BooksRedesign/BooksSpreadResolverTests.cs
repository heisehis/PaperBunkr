using Paperbunkr.App.Models;
using Paperbunkr.App.Services.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests.BooksRedesign;

/// <summary>docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §3.1: the spread's book, the nightstand, the quote fallbacks and "This week".</summary>
public class BooksSpreadResolverTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    private static BookCardSample Card(int id, BookReadingStatus status, int? seriesId = null, float? index = null,
        DateTime? opened = null, DateTime? added = null) => new()
        {
            BookId = id,
            Title = $"Book {id}",
            Status = status,
            BookSeriesId = seriesId,
            SeriesIndex = index,
            LastOpenedTime = opened,
            AddedTime = added ?? Now.AddDays(-id),
        };

    [Fact]
    public void EmptyLibrary_HasNoSpread() => Assert.Null(BooksSpreadResolver.PickSpread([]));

    [Fact]
    public void MostRecentlyOpenedReadingBook_LeadsTheSpread()
    {
        var cards = new[]
        {
            Card(1, BookReadingStatus.Reading, opened: Now.AddDays(-3)),
            Card(2, BookReadingStatus.Reading, opened: Now.AddHours(-1)),
            Card(3, BookReadingStatus.WantToRead),
        };

        var spread = BooksSpreadResolver.PickSpread(cards);

        Assert.Equal(2, spread!.Value.Card.BookId);
        Assert.Equal(BookSpreadMode.NowReading, spread.Value.Mode);
    }

    [Fact]
    public void NothingInProgress_NextInAStartedSeriesComesFirst()
    {
        var cards = new[]
        {
            Card(1, BookReadingStatus.Finished, seriesId: 7, index: 1, opened: Now.AddDays(-2)),
            Card(2, BookReadingStatus.Unread, seriesId: 7, index: 3),
            Card(3, BookReadingStatus.WantToRead, seriesId: 7, index: 2),
            Card(4, BookReadingStatus.WantToRead),
        };

        var spread = BooksSpreadResolver.PickSpread(cards);

        Assert.Equal(3, spread!.Value.Card.BookId);
        Assert.Equal(BookSpreadMode.UpNext, spread.Value.Mode);
    }

    [Fact]
    public void NoStartedSeries_OldestWantToReadComesNext()
    {
        var cards = new[]
        {
            Card(1, BookReadingStatus.WantToRead, added: Now.AddDays(-1)),
            Card(2, BookReadingStatus.WantToRead, added: Now.AddDays(-9)),
            Card(3, BookReadingStatus.Unread, added: Now),
        };

        Assert.Equal(2, BooksSpreadResolver.PickSpread(cards)!.Value.Card.BookId);
    }

    [Fact]
    public void OnlyUnreadBooks_NewestAddedIsShown_FinishedAndAbandonedAreSkipped()
    {
        var cards = new[]
        {
            Card(1, BookReadingStatus.Unread, added: Now.AddDays(-5)),
            Card(2, BookReadingStatus.Finished, added: Now),
            Card(3, BookReadingStatus.Abandoned, added: Now.AddMinutes(-1)),
            Card(4, BookReadingStatus.Unread, added: Now.AddDays(-1)),
        };

        Assert.Equal(4, BooksSpreadResolver.PickSpread(cards)!.Value.Card.BookId);
    }

    [Fact]
    public void Ghosts_AreNeverPicked()
    {
        var ghost = BookCardSample.Ghost(7, "Orchard Cycle", 4);

        Assert.Null(BooksSpreadResolver.PickSpread([ghost]));
    }

    [Fact]
    public void Nightstand_IsOtherReadingBooksThenNextInSeries_CappedAtThree()
    {
        var cards = new[]
        {
            Card(1, BookReadingStatus.Reading, opened: Now.AddHours(-1)),
            Card(2, BookReadingStatus.Reading, opened: Now.AddDays(-1)),
            Card(3, BookReadingStatus.Reading, seriesId: 7, index: 1, opened: Now.AddDays(-2)),
            Card(4, BookReadingStatus.Unread, seriesId: 7, index: 2),
            Card(5, BookReadingStatus.Reading, opened: Now.AddDays(-9)),
        };

        var nightstand = BooksSpreadResolver.PickNightstand(cards, spreadBookId: 1);

        Assert.Equal([2, 3, 5], nightstand.Select(c => c.BookId));
    }

    [Fact]
    public void Nightstand_TopsUpWithNextInSeries()
    {
        var cards = new[]
        {
            Card(1, BookReadingStatus.Reading, seriesId: 7, index: 1, opened: Now),
            Card(2, BookReadingStatus.Unread, seriesId: 7, index: 2),
        };

        var nightstand = BooksSpreadResolver.PickNightstand(cards, spreadBookId: 1);

        Assert.Equal([2], nightstand.Select(c => c.BookId));
    }

    [Theory]
    [InlineData("Saved words.", "A highlight.", "A synopsis. More.", "Saved words.")]
    [InlineData(null, "A highlight.", "A synopsis. More.", "A highlight.")]
    [InlineData("  ", null, "A synopsis that runs on. And more after it.", "A synopsis that runs on.")]
    [InlineData(null, null, "No full stop at all", "No full stop at all")]
    [InlineData(null, " ", null, null)]
    public void Quote_FallsBackInOrder(string? excerpt, string? highlight, string? summary, string? expected) =>
        Assert.Equal(expected, BooksSpreadResolver.PickQuote(excerpt, highlight, summary));

    [Fact]
    public void MetaLine_HasChapterPercentAndTimeLeft() =>
        Assert.Equal("Ch. 9 of 24 · 37% · about 2h 10m left",
            BooksSpreadResolver.BuildMetaLine(9, 24, 0.37, TimeSpan.FromMinutes(130)));

    [Fact]
    public void MetaLine_DropsWhatIsUnknown() =>
        Assert.Equal("12%", BooksSpreadResolver.BuildMetaLine(0, 0, 0.12, null));

    [Fact]
    public void ThisWeek_SumsOpenedSessionsInTheLastSevenDays()
    {
        var events = new[]
        {
            new ReadingEvent { ItemType = ReadingItemType.Novel, Kind = ReadingEventKind.Opened, TimestampUtc = Now.AddDays(-1), ActiveSeconds = 3600, PagesRead = 40 },
            new ReadingEvent { ItemType = ReadingItemType.Novel, Kind = ReadingEventKind.Opened, TimestampUtc = Now.AddDays(-6), ActiveSeconds = 1200, PagesRead = 12 },
            new ReadingEvent { ItemType = ReadingItemType.Novel, Kind = ReadingEventKind.Finished, TimestampUtc = Now.AddDays(-1), PagesRead = 40 },
            new ReadingEvent { ItemType = ReadingItemType.Novel, Kind = ReadingEventKind.Opened, TimestampUtc = Now.AddDays(-8), ActiveSeconds = 9999, PagesRead = 99 },
            new ReadingEvent { ItemType = ReadingItemType.Comic, Kind = ReadingEventKind.Opened, TimestampUtc = Now.AddDays(-1), ActiveSeconds = 600, PagesRead = 22 },
        };

        Assert.Equal("This week · 1h 20m read · 52 pages · 1 finished", BooksSpreadResolver.BuildThisWeekLine(events, Now));
    }

    [Fact]
    public void ThisWeek_NothingRead_IsNull() =>
        Assert.Null(BooksSpreadResolver.BuildThisWeekLine([], Now));
}
