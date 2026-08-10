using Avalonia;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="BookReaderScreenViewModel"/> (docs/superpowers/specs/
/// 2026-08-09-novels-epub-pdf-support-design.md §5, Phase 2). Real regression coverage for a bug
/// found via manual testing against a real e-book library: a chapter with zero paragraphs (a real
/// EPUB's cover/title-page spine item, confirmed against an actual file's own &lt;guide&gt;
/// metadata) left the reader permanently blank instead of skipping to readable content.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookReaderScreenViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _epubPath;

    public BookReaderScreenViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_vm_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        _epubPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_vm_test_{Guid.NewGuid():N}.epub");
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

    private int AddBook(bool firstChapterEmpty)
    {
        EpubFixture.Create(_epubPath, firstChapterEmpty: firstChapterEmpty);
        using var context = new PaperbunkrDbContext(_dbOptions);
        var book = new Book { Title = "Test", FilePath = _epubPath, Format = BookFormat.Epub, AddedTime = DateTime.UtcNow };
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private BookReaderScreenViewModel CreateViewModel(int bookId)
    {
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        var vm = new BookReaderScreenViewModel(() => { });
        vm.LoadBook(bookId);
        vm.UpdateViewportSize(new Size(700, 800));
        return vm;
    }

    [Fact]
    public void LoadBook_NormalFirstChapter_ShowsItsParagraphs()
    {
        int bookId = AddBook(firstChapterEmpty: false);

        var vm = CreateViewModel(bookId);

        Assert.NotEmpty(vm.CurrentPageParagraphs);
        Assert.Equal("The Beginning", vm.ChapterTitle);
    }

    [Fact]
    public void LoadBook_EmptyFirstChapter_SkipsToFirstChapterWithContent()
    {
        int bookId = AddBook(firstChapterEmpty: true);

        var vm = CreateViewModel(bookId);

        // Chapter 1 (index 0, the empty cover page) is skipped in favor of chapter 2
        // (index 1, "The End" - EpubFixture's second chapter), which has real content.
        Assert.NotEmpty(vm.CurrentPageParagraphs);
        Assert.Equal("The End", vm.ChapterTitle);
    }

    [Fact]
    public void GoToChapter_TableOfContents_MarksSelectedChapterActive()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(bookId);

        vm.GoToChapterCommand.Execute(vm.TableOfContents[1]);

        Assert.True(vm.TableOfContents[1].IsActive);
        Assert.False(vm.TableOfContents[0].IsActive);
        Assert.Equal("The End", vm.ChapterTitle);
    }
}
