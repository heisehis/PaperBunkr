using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>Home's "Next in your book series" and "New books" rows (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §9).</summary>
public class HomeBookRowsTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public HomeBookRowsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_home_books_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private BookSeries AddSeries(string name)
    {
        using var context = new PaperbunkrDbContext(_options);
        var series = new BookSeries { Name = name };
        context.BookSeries.Add(series);
        context.SaveChanges();
        return series;
    }

    private void AddBook(string title, BookSeries? series = null, float? index = null, BookReadingStatus status = BookReadingStatus.Unread,
        DateTime? added = null, DateTime? opened = null)
    {
        using var context = new PaperbunkrDbContext(_options);
        context.Books.Add(new Book
        {
            Title = title, BookSeriesId = series?.Id, SeriesIndex = index, Status = status, Format = BookFormat.Epub,
            FilePath = $@"C:\b\{title}.epub", AddedTime = added ?? Now.AddDays(-100), LastOpenedTime = opened,
        });
        context.SaveChanges();
    }

    private string[] UpNext()
    {
        using var context = new PaperbunkrDbContext(_options);
        return HomeFeedResolver.GetUpNextBooksInSeries(context).Select(b => b.Title).ToArray();
    }

    [Fact]
    public void UpNext_IsTheNextNumberAfterTheHighestFinished()
    {
        var dune = AddSeries("Dune");
        AddBook("Dune 1", dune, 1, BookReadingStatus.Finished, opened: Now.AddDays(-5));
        AddBook("Dune 2", dune, 2, BookReadingStatus.Finished, opened: Now.AddDays(-2));
        AddBook("Dune 3", dune, 3);
        AddBook("Dune 4", dune, 4);

        Assert.Equal(new[] { "Dune 3" }, UpNext());
    }

    [Fact]
    public void UpNext_SkipsAbandoned_FinishedAndLowerNumbers_AndPrefersAReadingBook()
    {
        var s = AddSeries("S");
        AddBook("S1", s, 1, BookReadingStatus.Finished, opened: Now.AddDays(-1));
        AddBook("S2", s, 2, BookReadingStatus.Abandoned);
        AddBook("S3", s, 3, BookReadingStatus.Reading);
        AddBook("S0", s, 0); // below the finished one: not "next"
        // S0 sorts first as a number but is not above the highest finished (1), so S3 wins over S2 (abandoned).
        Assert.Equal(new[] { "S3" }, UpNext());
    }

    [Fact]
    public void UpNext_NeedsAFinishedBook_AndFullNumbering_AndMostRecentSeriesComesFirst()
    {
        var noneFinished = AddSeries("Nothing read");
        AddBook("N1", noneFinished, 1);
        AddBook("N2", noneFinished, 2);

        var unnumbered = AddSeries("Unnumbered");
        AddBook("U1", unnumbered, 1, BookReadingStatus.Finished, opened: Now);
        AddBook("U?", unnumbered, null);
        AddBook("U2", unnumbered, 2);

        var older = AddSeries("Older");
        AddBook("O1", older, 1, BookReadingStatus.Finished, opened: Now.AddDays(-30));
        AddBook("O2", older, 2);
        var newer = AddSeries("Newer");
        AddBook("W1", newer, 1, BookReadingStatus.Finished, opened: Now.AddDays(-1));
        AddBook("W2", newer, 2);

        var done = AddSeries("Complete");
        AddBook("C1", done, 1, BookReadingStatus.Finished, opened: Now);

        Assert.Equal(new[] { "W2", "O2" }, UpNext());
    }

    [Fact]
    public void RecentlyAdded_IsNewUnopenedBooksOnly_NewestFirst_Capped()
    {
        AddBook("Fresh", added: Now.AddDays(-2));
        AddBook("Fresher", added: Now.AddDays(-1));
        AddBook("Old", added: Now.AddDays(-31));
        AddBook("Opened", added: Now.AddDays(-3), opened: Now.AddDays(-2));
        AddBook("Reading", added: Now.AddDays(-3), status: BookReadingStatus.Reading);
        AddBook("Edge", added: Now.AddDays(-30));

        using var context = new PaperbunkrDbContext(_options);
        Assert.Equal(new[] { "Fresher", "Fresh", "Edge" }, HomeFeedResolver.GetRecentlyAddedBooks(context, Now).Select(b => b.Title));
        Assert.Single(HomeFeedResolver.GetRecentlyAddedBooks(context, Now, limit: 1));
    }

    [Fact]
    public void NoBooks_GivesEmptyRows()
    {
        Assert.Empty(UpNext());
        using var context = new PaperbunkrDbContext(_options);
        Assert.Empty(HomeFeedResolver.GetRecentlyAddedBooks(context, Now));
    }
}
