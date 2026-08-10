using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="BookFolderScanService"/> (docs/superpowers/specs/
/// 2026-08-09-novels-epub-pdf-support-design.md §3) against real synthetic EPUB/PDF fixtures,
/// structurally parallel to <see cref="LibraryFolderScannerTests"/> for the comic library.
/// </summary>
public class BookFolderScanServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _scanRoot;

    public BookFolderScanServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_book_scan_db_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        _scanRoot = Path.Combine(Path.GetTempPath(), $"paperbunkr_book_scan_root_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scanRoot);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_scanRoot)) Directory.Delete(_scanRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private BookFolderScanService CreateScanner() => new(() => new PaperbunkrDbContext(_dbOptions));

    private void AddBookFolder(string path)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.BookFolders.Add(new BookFolder { Path = path });
        context.SaveChanges();
    }

    [Fact]
    public async Task ScanAllAsync_ImportsEpub_WithTitleAndAuthor()
    {
        EpubFixture.Create(Path.Combine(_scanRoot, "novel.epub"), title: "The Long Road", author: "Ada Author");
        AddBookFolder(_scanRoot);

        var result = await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(1, result.BooksAdded);
        using var context = new PaperbunkrDbContext(_dbOptions);
        var book = Assert.Single(context.Books);
        Assert.Equal("The Long Road", book.Title);
        Assert.Equal("Ada Author", book.Author);
        Assert.Equal(BookFormat.Epub, book.Format);
    }

    [Fact]
    public async Task ScanAllAsync_ImportsPdf_FallsBackToFileNameForTitle()
    {
        PdfFixture.Create(Path.Combine(_scanRoot, "Untitled Manuscript.pdf"), "Some page text.");
        AddBookFolder(_scanRoot);

        var result = await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(1, result.BooksAdded);
        using var context = new PaperbunkrDbContext(_dbOptions);
        var book = Assert.Single(context.Books);
        Assert.Equal("Untitled Manuscript", book.Title);
        Assert.Equal(BookFormat.Pdf, book.Format);
    }

    [Fact]
    public async Task ScanAllAsync_EpubWithCalibreSeries_CreatesBookSeries()
    {
        EpubFixture.Create(Path.Combine(_scanRoot, "novel.epub"), seriesName: "The Long Road Trilogy");
        AddBookFolder(_scanRoot);

        var result = await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(1, result.SeriesTouched);
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = Assert.Single(context.BookSeries);
        Assert.Equal("The Long Road Trilogy", series.Name);
        var book = Assert.Single(context.Books);
        Assert.Equal(series.Id, book.BookSeriesId);
    }

    [Fact]
    public async Task ScanAllAsync_MultipleBooksSameSeries_ShareOneSeriesRow()
    {
        EpubFixture.Create(Path.Combine(_scanRoot, "book1.epub"), title: "Book One", seriesName: "The Trilogy");
        EpubFixture.Create(Path.Combine(_scanRoot, "book2.epub"), title: "Book Two", seriesName: "The Trilogy");
        AddBookFolder(_scanRoot);

        var result = await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(2, result.BooksAdded);
        Assert.Equal(1, result.SeriesTouched);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Single(context.BookSeries);
        Assert.Equal(2, context.Books.Count());
    }

    [Fact]
    public async Task ScanAllAsync_ReScanning_IsIdempotent_NoDuplicates()
    {
        EpubFixture.Create(Path.Combine(_scanRoot, "novel.epub"));
        AddBookFolder(_scanRoot);
        var scanner = CreateScanner();
        await scanner.ScanAllAsync(new Progress<(int, int)>());

        var second = await scanner.ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(0, second.BooksAdded);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Single(context.Books);
    }

    [Fact]
    public async Task ScanAllAsync_IgnoresUnsupportedExtensions()
    {
        File.WriteAllText(Path.Combine(_scanRoot, "notes.txt"), "hello");
        AddBookFolder(_scanRoot);

        var result = await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(0, result.BooksAdded);
    }

    [Fact]
    public async Task ScanAllAsync_CorruptEpub_SkippedGracefully_DoesNotStopBatch()
    {
        File.WriteAllText(Path.Combine(_scanRoot, "corrupt.epub"), "not a real epub");
        EpubFixture.Create(Path.Combine(_scanRoot, "valid.epub"), title: "Valid Book");
        AddBookFolder(_scanRoot);

        var result = await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(1, result.BooksAdded);
        using var context = new PaperbunkrDbContext(_dbOptions);
        var book = Assert.Single(context.Books);
        Assert.Equal("Valid Book", book.Title);
    }
}
