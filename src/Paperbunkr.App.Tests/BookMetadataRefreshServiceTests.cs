using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>"Refresh metadata from files" (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §4.1): fills empties, never overwrites.</summary>
public class BookMetadataRefreshServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _root;

    public BookMetadataRefreshServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_book_refresh_db_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_book_refresh_root_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private BookMetadataRefreshService Service() => new(() => new PaperbunkrDbContext(_dbOptions));

    private int AddBook(string fileName, Action<Book>? configure = null, string? publisherXml = "<dc:publisher>Ace Books</dc:publisher>")
    {
        string path = Path.Combine(_root, fileName);
        EpubFixture.Create(path, title: Path.GetFileNameWithoutExtension(fileName), seriesName: "Dune Chronicles", extraMetadataXml: publisherXml is null
            ? null
            : "    " + publisherXml + "\n    <dc:subject>Science Fiction</dc:subject>\n    <meta name=\"calibre:series_index\" content=\"2\"/>\n");

        var book = new Book { Title = Path.GetFileNameWithoutExtension(fileName), Format = BookFormat.Epub, FilePath = path, AddedTime = DateTime.UtcNow };
        configure?.Invoke(book);
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    [Fact]
    public async Task FillsEmptyFields_AndSetsTheBackfillFlag()
    {
        int id = AddBook("a.epub");

        var result = await Service().RunAsync(null);

        Assert.Equal(1, result.Scanned);
        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Failed);
        using var context = new PaperbunkrDbContext(_dbOptions);
        var book = context.Books.Include(b => b.BookSeries).Include(b => b.Tags).Include(b => b.FieldSources).Single(b => b.Id == id);
        Assert.Equal("Ace Books", book.Publisher);
        Assert.Equal("Dune Chronicles", book.BookSeries!.Name);
        Assert.Equal(2f, book.SeriesIndex);
        Assert.Contains(book.Tags, t => t.Value == "Science Fiction");
        Assert.All(book.FieldSources, s => Assert.Equal(BookFieldSourceKind.File, s.Source));
        Assert.True(context.GetOrCreateAppSettings().BookMetadataBackfillDone);
    }

    [Fact]
    public async Task NeverOverwritesAUserValue_OrReAddsAClearedField()
    {
        int id = AddBook("b.epub", configure: b =>
        {
            b.Publisher = "My Publisher";
            b.BookSeries = new BookSeries { Name = "My Own Series" };
            b.FieldSources.Add(new BookFieldSource { Field = BookFields.Genre, Source = BookFieldSourceKind.Manual, FetchedUtc = DateTime.UtcNow });
        });

        await Service().RunAsync(null);

        using var context = new PaperbunkrDbContext(_dbOptions);
        var book = context.Books.Include(b => b.BookSeries).Include(b => b.Tags).Single(b => b.Id == id);
        Assert.Equal("My Publisher", book.Publisher);
        Assert.Equal("My Own Series", book.BookSeries!.Name);
        Assert.DoesNotContain(book.Tags, t => t.Field == BookTagField.Genre);
    }

    [Fact]
    public async Task IsIdempotent_AndCountsMissingFilesAsFailed()
    {
        AddBook("c.epub");
        AddBook("gone.epub", configure: b => b.FilePath = Path.Combine(_root, "does-not-exist.epub"));

        var first = await Service().RunAsync(null);
        var second = await Service().RunAsync(null);

        Assert.Equal(1, first.Updated);
        Assert.Equal(1, first.Failed);
        Assert.Equal(0, second.Updated);
        Assert.Equal(0, second.FieldsFilled);
        Assert.Contains("unreadable", first.Describe());
    }

    [Fact]
    public async Task Cancellation_StopsTheRun()
    {
        AddBook("d.epub");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().RunAsync(null, cts.Token));
    }
}
