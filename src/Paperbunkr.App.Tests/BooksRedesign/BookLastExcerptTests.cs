using Avalonia;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests.BooksRedesign;

/// <summary>The reader saves the words at the saved position as <see cref="Book.LastExcerpt"/> for the Books spread's pull quote
/// (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §6.1).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookLastExcerptTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_excerpt_test_{Guid.NewGuid():N}.db");
    private readonly string _epubPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_excerpt_test_{Guid.NewGuid():N}.epub");
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public BookLastExcerptTests()
    {
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        PaperbunkrDbContext.DatabasePathOverride = null;
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_epubPath)) File.Delete(_epubPath);
        }
        catch (IOException)
        {
        }
    }

    private (BookReaderScreenViewModel Vm, int BookId) Open()
    {
        EpubFixture.Create(_epubPath, firstChapterEmpty: false);
        int bookId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var book = new Book { Title = "Test", FilePath = _epubPath, Format = BookFormat.Epub, AddedTime = DateTime.UtcNow };
            context.Books.Add(book);
            context.SaveChanges();
            bookId = book.Id;
        }

        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        var vm = new BookReaderScreenViewModel(() => { });
        vm.LoadBook(bookId);
        vm.UpdateViewportSize(new Size(700, 800));
        return (vm, bookId);
    }

    private string? StoredExcerpt(int bookId)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        return context.Books.Single(b => b.Id == bookId).LastExcerpt;
    }

    [Fact]
    public void PositionReport_SavesTheCutExcerpt()
    {
        var (vm, bookId) = Open();
        string text = "By the ninth winter the trees no longer rang when the wind came through; they only remembered how. "
            + "Ilse walked the rows with her chalk and her string, measuring the distance between silences.";

        vm.OnPositionCaptured("pb-p2", 0.2, text);

        string? stored = StoredExcerpt(bookId);
        Assert.NotNull(stored);
        Assert.True(stored!.Length <= 161, "cut to about 160 characters plus the ellipsis");
        Assert.StartsWith("By the ninth winter", stored);
        Assert.EndsWith("…", stored);
    }

    [Fact]
    public void EmptyExcerpt_KeepsThePreviousQuote()
    {
        var (vm, bookId) = Open();
        vm.OnPositionCaptured("pb-p2", 0.2, "They only remembered how.");

        vm.OnPositionCaptured("pb-p3", 0.3, "   ");

        Assert.Equal("They only remembered how.", StoredExcerpt(bookId));
    }
}
