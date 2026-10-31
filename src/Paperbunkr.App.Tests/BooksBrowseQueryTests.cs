using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>The Books screen's filter / sort / group as pure functions (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §2-3).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BooksBrowseQueryTests
{
    private static int _id;

    private static BookCardSample Card(
        string title, string? author = null, string? series = null, BookReadingStatus status = BookReadingStatus.Unread,
        float? rating = null, double progress = 0, string? publisher = null, string? language = null, DateTime? published = null,
        float? index = null, string? genre = null, DateTime? added = null, DateTime? opened = null, int? seriesId = null, bool ghost = false) =>
        new()
        {
            BookId = ++_id, Title = title, Author = author, SeriesName = series, BookSeriesId = seriesId, Status = status, Rating = rating,
            Progress = progress, Publisher = publisher, Language = language, PublishedDate = published, SeriesIndex = index,
            FirstGenre = genre, AddedTime = added ?? new DateTime(2026, 1, 1), LastOpenedTime = opened, IsGhost = ghost,
        };

    private static string[] Titles(IEnumerable<BookCardSample> cards) => cards.Select(c => c.Title).ToArray();

    // ---- tabs ----

    [Fact]
    public void TabCounts_CountEachStatus_AndIgnoreGhosts()
    {
        var cards = new[]
        {
            Card("a", status: BookReadingStatus.Reading), Card("b", status: BookReadingStatus.Reading),
            Card("c", status: BookReadingStatus.Finished), Card("d"), Card("ghost", ghost: true),
        };

        var counts = BooksBrowseQuery.TabCounts(cards);

        Assert.Equal(4, counts[BooksStatusTab.All]);
        Assert.Equal(2, counts[BooksStatusTab.Reading]);
        Assert.Equal(1, counts[BooksStatusTab.Finished]);
        Assert.Equal(1, counts[BooksStatusTab.Unread]);
        Assert.Equal(0, counts[BooksStatusTab.Abandoned]);
    }

    [Fact]
    public void Filter_ComposesTabSearchShelfAndAuthor_AndDropsGhosts()
    {
        var a = Card("Dune", "Frank Herbert", "Dune", BookReadingStatus.Reading);
        var b = Card("Dune Messiah", "Frank Herbert", "Dune", BookReadingStatus.Finished);
        var c = Card("Emma", "Jane Austen", status: BookReadingStatus.Reading);
        var ghost = Card("#3 missing", ghost: true);
        var all = new[] { a, b, c, ghost };

        Assert.Equal(new[] { "Dune", "Emma" }, Titles(BooksBrowseQuery.Filter(all, new BooksBrowseOptions(Tab: BooksStatusTab.Reading))));
        Assert.Equal(new[] { "Dune", "Dune Messiah" }, Titles(BooksBrowseQuery.Filter(all, new BooksBrowseOptions(Search: "dune"))));
        Assert.Equal(new[] { "Dune", "Dune Messiah" }, Titles(BooksBrowseQuery.Filter(all, new BooksBrowseOptions(Author: " frank herbert "))));
        Assert.Equal(new[] { "Emma" }, Titles(BooksBrowseQuery.Filter(all, new BooksBrowseOptions(ShelfIds: new HashSet<int> { c.BookId, b.BookId }, Tab: BooksStatusTab.Reading))));
        Assert.Empty(BooksBrowseQuery.Filter(all, new BooksBrowseOptions(Tab: BooksStatusTab.Abandoned)));
        Assert.Equal(new[] { "Dune Messiah" }, Titles(BooksBrowseQuery.Filter(all, new BooksBrowseOptions(Search: "Herbert", Tab: BooksStatusTab.Finished))));
    }

    // ---- original axes behave as before ----

    [Fact]
    public void Sort_OriginalAxes_KeepTheirBehaviour()
    {
        var a = Card("Beta", "Zed", added: new DateTime(2026, 1, 3), opened: new DateTime(2026, 2, 1));
        var b = Card("Alpha", null, added: new DateTime(2026, 1, 1));
        var c = Card("Gamma", "Amy", added: new DateTime(2026, 1, 2), opened: new DateTime(2026, 3, 1));
        var all = new[] { a, b, c };

        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, Titles(BooksBrowseQuery.Sort(all, BooksSortField.Title, SortDirection.Ascending)));
        Assert.Equal(new[] { "Gamma", "Beta", "Alpha" }, Titles(BooksBrowseQuery.Sort(all, BooksSortField.Title, SortDirection.Descending)));
        Assert.Equal(new[] { "Alpha", "Gamma", "Beta" }, Titles(BooksBrowseQuery.Sort(all, BooksSortField.Author, SortDirection.Ascending))); // blank author first, as before
        Assert.Equal(new[] { "Alpha", "Gamma", "Beta" }, Titles(BooksBrowseQuery.Sort(all, BooksSortField.RecentlyAdded, SortDirection.Ascending)));
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, Titles(BooksBrowseQuery.Sort(all, BooksSortField.LastOpened, SortDirection.Ascending)));
    }

    // ---- new axes: missing values last in both directions ----

    [Theory]
    [InlineData(SortDirection.Ascending, new[] { "Low", "High", "None1", "None2" })]
    [InlineData(SortDirection.Descending, new[] { "High", "Low", "None1", "None2" })]
    public void Sort_Rating_PutsUnratedLast_EitherWay(SortDirection direction, string[] expected)
    {
        var all = new[] { Card("None2"), Card("High", rating: 5f), Card("None1"), Card("Low", rating: 1.5f) };

        Assert.Equal(expected, Titles(BooksBrowseQuery.Sort(all, BooksSortField.Rating, direction)));
    }

    [Theory]
    [InlineData(SortDirection.Ascending, new[] { "Quarter", "Most", "Fresh" })]
    [InlineData(SortDirection.Descending, new[] { "Most", "Quarter", "Fresh" })]
    public void Sort_Progress_PutsUnstartedLast(SortDirection direction, string[] expected)
    {
        var all = new[] { Card("Most", progress: 0.9), Card("Fresh"), Card("Quarter", progress: 0.25) };

        Assert.Equal(expected, Titles(BooksBrowseQuery.Sort(all, BooksSortField.Progress, direction)));
    }

    [Fact]
    public void Sort_PublisherPublished_AndSeriesOrder()
    {
        var pub = new[] { Card("B", publisher: "tor"), Card("A", publisher: "Ace"), Card("C") };
        Assert.Equal(new[] { "A", "B", "C" }, Titles(BooksBrowseQuery.Sort(pub, BooksSortField.Publisher, SortDirection.Ascending)));
        Assert.Equal(new[] { "B", "A", "C" }, Titles(BooksBrowseQuery.Sort(pub, BooksSortField.Publisher, SortDirection.Descending)));

        var dates = new[] { Card("Old", published: new DateTime(1965, 1, 1)), Card("New", published: new DateTime(2020, 1, 1)), Card("Undated") };
        Assert.Equal(new[] { "Old", "New", "Undated" }, Titles(BooksBrowseQuery.Sort(dates, BooksSortField.Published, SortDirection.Ascending)));

        var series = new[]
        {
            Card("Z2", series: "Zed", index: 2), Card("A3", series: "Alpha", index: 3), Card("Z1", series: "Zed", index: 1),
            Card("A1", series: "Alpha", index: 1), Card("Loose"),
        };
        Assert.Equal(new[] { "A1", "A3", "Z1", "Z2", "Loose" }, Titles(BooksBrowseQuery.Sort(series, BooksSortField.SeriesOrder, SortDirection.Ascending)));
        Assert.Equal(new[] { "A3", "A1", "Z2", "Z1", "Loose" }, Titles(BooksBrowseQuery.Sort(series, BooksSortField.SeriesOrder, SortDirection.Descending)));
    }

    // ---- grouping ----

    [Fact]
    public void Group_SeriesAndAuthor_KeepFallbackBucketsLast_AndOnlySeriesCarriesAnId()
    {
        var all = BooksBrowseQuery.Sort(new[]
        {
            Card("Solo", "Bob"), Card("Dune 1", "Frank", "Dune", seriesId: 7), Card("Dune 2", "Frank", "Dune", seriesId: 7), Card("Nameless"),
        }, BooksSortField.Title, SortDirection.Ascending);

        var bySeries = BooksBrowseQuery.Group(all, BooksGroupField.Series, BooksSortField.Title);
        Assert.Equal(new[] { "Dune", "Standalone" }, bySeries.Select(g => g.Header));
        Assert.Equal(7, bySeries[0].BookSeriesId);
        Assert.Null(bySeries[1].BookSeriesId);

        var byAuthor = BooksBrowseQuery.Group(all, BooksGroupField.Author, BooksSortField.Title);
        Assert.Equal(new[] { "Bob", "Frank", "Unknown author" }, byAuthor.Select(g => g.Header));
        Assert.All(byAuthor, g => Assert.Null(g.BookSeriesId));
    }

    [Fact]
    public void Group_Status_UsesAFixedOrder_AndSkipsEmptyOnes()
    {
        var all = new[]
        {
            Card("a", status: BookReadingStatus.Finished), Card("b", status: BookReadingStatus.Reading),
            Card("c", status: BookReadingStatus.Abandoned), Card("d", status: BookReadingStatus.WantToRead),
        };

        var groups = BooksBrowseQuery.Group(all, BooksGroupField.Status, BooksSortField.Title);

        Assert.Equal(new[] { "Reading", "Want to read", "Finished", "Abandoned" }, groups.Select(g => g.Header));
    }

    [Fact]
    public void Group_PublisherLanguageGenre_UseFallbackNames_AndFirstGenreOnly()
    {
        var all = new[]
        {
            Card("a", publisher: "Tor", language: "en", genre: "Fantasy"), Card("b", publisher: " Tor ", language: "fr", genre: "Fantasy"),
            Card("c"), Card("d", publisher: "Ace", genre: "SF"),
        };

        Assert.Equal(new[] { "Ace", "Tor", "Unknown publisher" }, BooksBrowseQuery.Group(all, BooksGroupField.Publisher, BooksSortField.Title).Select(g => g.Header));
        Assert.Equal(new[] { "English", "French", "Unknown language" }, BooksBrowseQuery.Group(all, BooksGroupField.Language, BooksSortField.Title).Select(g => g.Header));
        var genres = BooksBrowseQuery.Group(all, BooksGroupField.Genre, BooksSortField.Title);
        Assert.Equal(new[] { "Fantasy", "SF", "No genre" }, genres.Select(g => g.Header));
        Assert.Equal(2, genres[0].Items.Count);
    }

    [Fact]
    public void Group_TimeSorts_OrderGroupsByNewestBook()
    {
        var all = new[]
        {
            Card("a1", "Old", added: new DateTime(2026, 1, 1)), Card("b1", "New", added: new DateTime(2026, 5, 1)), Card("c1", added: new DateTime(2026, 9, 1)),
        };
        var sorted = BooksBrowseQuery.Sort(all, BooksSortField.RecentlyAdded, SortDirection.Descending);

        var groups = BooksBrowseQuery.Group(sorted, BooksGroupField.Author, BooksSortField.RecentlyAdded);

        Assert.Equal(new[] { "New", "Old", "Unknown author" }, groups.Select(g => g.Header));
    }

    // ---- series gaps ----

    [Fact]
    public void AddGhosts_FillsTheHoles_InSlotUnderSeriesOrder_AndAtTheEndOtherwise()
    {
        var all = new[]
        {
            Card("Dune 1", series: "Dune", seriesId: 7, index: 1), Card("Dune 2", series: "Dune", seriesId: 7, index: 2),
            Card("Dune 4", series: "Dune", seriesId: 7, index: 4), Card("Standalone"),
        };

        var ordered = BooksBrowseQuery.Group(BooksBrowseQuery.Sort(all, BooksSortField.SeriesOrder, SortDirection.Ascending), BooksGroupField.Series, BooksSortField.SeriesOrder);
        BooksBrowseQuery.AddGhosts(ordered, all, BooksSortField.SeriesOrder, SortDirection.Ascending);
        var dune = ordered.Single(g => g.Header == "Dune").Items;
        Assert.Equal(new[] { "Dune 1", "Dune 2", "#3 missing", "Dune 4" }, dune.Select(c => c.Title));
        Assert.True(dune[2].IsGhost);
        Assert.Equal(7, dune[2].BookSeriesId);
        Assert.DoesNotContain(ordered.Single(g => g.Header == "Standalone").Items, c => c.IsGhost);

        var byTitle = BooksBrowseQuery.Group(BooksBrowseQuery.Sort(all, BooksSortField.Title, SortDirection.Ascending), BooksGroupField.Series, BooksSortField.Title);
        BooksBrowseQuery.AddGhosts(byTitle, all, BooksSortField.Title, SortDirection.Ascending);
        Assert.Equal(new[] { "Dune 1", "Dune 2", "Dune 4", "#3 missing" }, byTitle.Single(g => g.Header == "Dune").Items.Select(c => c.Title));
    }

    [Fact]
    public void AddGhosts_UsesEveryOwnedBook_NotJustTheVisibleOnes_AndSkipsUnnumberedSeries()
    {
        var one = Card("A1", series: "S", seriesId: 1, index: 1, status: BookReadingStatus.Finished);
        var three = Card("A3", series: "S", seriesId: 1, index: 3, status: BookReadingStatus.Unread);
        var loose = Card("B1", series: "T", seriesId: 2, index: 1);
        var unnumbered = Card("B?", series: "T", seriesId: 2);
        var all = new[] { one, three, loose, unnumbered };

        // Only the Finished book is visible here, but #2 is still the hole between 1 and 3 among what is owned.
        var groups = BooksBrowseQuery.Group(new[] { one }, BooksGroupField.Series, BooksSortField.Title);
        BooksBrowseQuery.AddGhosts(groups, all, BooksSortField.Title, SortDirection.Ascending);
        Assert.Contains(groups[0].Items, c => c.IsGhost && c.GhostLabel == "#2");

        var t = BooksBrowseQuery.Group(new[] { loose, unnumbered }, BooksGroupField.Series, BooksSortField.Title);
        BooksBrowseQuery.AddGhosts(t, all, BooksSortField.Title, SortDirection.Ascending);
        Assert.DoesNotContain(t[0].Items, c => c.IsGhost);
    }

    [Fact]
    public void Ghosts_AreNeverFilteredInOrCounted()
    {
        var ghost = BookCardSample.Ghost(1, "S", 3);
        Assert.Empty(BooksBrowseQuery.Filter(new[] { ghost }, new BooksBrowseOptions()));
        Assert.Equal(0, BooksBrowseQuery.TabCounts(new[] { ghost })[BooksStatusTab.All]);
        Assert.Equal(0, ghost.BookId);
        Assert.Equal("#3 missing", ghost.Title);
    }

    // ---- A-Z rail ----

    [Fact]
    public void Alphabet_FilesByTitleOrAuthor_SkipsArticles_AndFindsTheFirstItem()
    {
        var cards = new[] { Card("The Hobbit", "Tolkien"), Card("Emma", "Austen"), Card("1984", "Orwell") };
        var sortedByTitle = BooksBrowseQuery.Sort(cards, BooksSortField.Title, SortDirection.Ascending); // 1984, Emma, The Hobbit

        var index = BooksAlphabet.Build(sortedByTitle, Array.Empty<BookCardGroup>(), grouped: false, BooksSortField.Title).ToDictionary(e => e.Letter, e => e.HasItems);
        Assert.True(index["H"]);   // "The Hobbit" files under H
        Assert.True(index["E"]);
        Assert.True(index["#"]);   // 1984
        Assert.False(index["T"]);
        Assert.Equal(27, index.Count);

        Assert.Equal(2, BooksAlphabet.FirstBookIndex(sortedByTitle, BooksSortField.Title, "H"));
        Assert.Equal(0, BooksAlphabet.FirstBookIndex(sortedByTitle, BooksSortField.Title, "#"));
        Assert.Equal(-1, BooksAlphabet.FirstBookIndex(sortedByTitle, BooksSortField.Title, "Z"));

        var byAuthor = BooksBrowseQuery.Sort(cards, BooksSortField.Author, SortDirection.Ascending); // Austen, Orwell, Tolkien
        Assert.Equal(2, BooksAlphabet.FirstBookIndex(byAuthor, BooksSortField.Author, "T"));
    }

    [Fact]
    public void Alphabet_GroupedUsesHeaders_AndOnlyAppliesWhereItMakesSense()
    {
        var groups = BooksBrowseQuery.Group(BooksBrowseQuery.Sort(new[] { Card("a", "Zed"), Card("b", "Amy") }, BooksSortField.Title, SortDirection.Ascending),
            BooksGroupField.Author, BooksSortField.Title);

        Assert.Equal(0, BooksAlphabet.FirstGroupIndex(groups, "A"));
        Assert.Equal(1, BooksAlphabet.FirstGroupIndex(groups, "Z"));
        Assert.Equal(-1, BooksAlphabet.FirstGroupIndex(groups, "M"));

        Assert.True(BooksAlphabet.Applies(false, BooksGroupField.None, BooksSortField.Title));
        Assert.True(BooksAlphabet.Applies(false, BooksGroupField.None, BooksSortField.Author));
        Assert.False(BooksAlphabet.Applies(false, BooksGroupField.None, BooksSortField.Rating));
        Assert.True(BooksAlphabet.Applies(true, BooksGroupField.Series, BooksSortField.Rating));
        Assert.False(BooksAlphabet.Applies(true, BooksGroupField.Status, BooksSortField.Title)); // Status headers are not alphabetical
    }

    // ---- progress ----

    [Fact]
    public void BookProgress_CombinesChapterAndFraction_AndFinishedIsOne()
    {
        var book = new Book { ChapterCount = 10, LastChapterIndex = 4, LastProgressionFraction = 0.5, Status = BookReadingStatus.Reading };
        Assert.Equal(0.45, BookProgress.Compute(book), 3);

        book.Status = BookReadingStatus.Finished;
        Assert.Equal(1d, BookProgress.Compute(book));

        var pdf = new Book { ChapterCount = 0, LastProgressionFraction = 0.3 };
        Assert.Equal(0.3, BookProgress.Compute(pdf), 3);
        Assert.Equal(0d, BookProgress.Compute(new Book()));
    }
}
