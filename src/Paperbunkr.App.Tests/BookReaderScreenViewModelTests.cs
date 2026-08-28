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
    public void LoadBook_WithStartPosition_OpensThereInsteadOfResuming()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var book = context.Books.Single(b => b.Id == bookId);
            book.LastChapterIndex = 0;
            context.SaveChanges();
        }

        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        var vm = new BookReaderScreenViewModel(() => { });
        vm.LoadBook(bookId, new Paperbunkr.App.Models.BookPosition(1, 0));
        vm.UpdateViewportSize(new Size(700, 800));

        Assert.Equal("The End", vm.ChapterTitle);
    }

    [Fact]
    public void LoadBook_WithOutOfRangeStartChapter_ClampsWithoutThrowing()
    {
        int bookId = AddBook(firstChapterEmpty: false);

        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        var vm = new BookReaderScreenViewModel(() => { });
        vm.LoadBook(bookId, new Paperbunkr.App.Models.BookPosition(99, 0));
        vm.UpdateViewportSize(new Size(700, 800));

        Assert.Equal("The End", vm.ChapterTitle); // clamped to the last chapter (index 1)
    }

    [Fact]
    public void LoadBook_NoStartPosition_StillResumesFromSavedChapter()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var book = context.Books.Single(b => b.Id == bookId);
            book.LastChapterIndex = 1;
            context.SaveChanges();
        }

        var vm = CreateViewModel(bookId);

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

    [Fact]
    public void GoToChapter_PersistsPosition_SoReopeningTheBookResumesThere()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(bookId);

        vm.GoToChapterCommand.Execute(vm.TableOfContents[1]);

        // A fresh view model (as if the book was closed and reopened) should land back on
        // "The End" instead of the book's actual first chapter - design spec §6.
        var reopened = CreateViewModel(bookId);
        Assert.Equal("The End", reopened.ChapterTitle);
    }

    [Fact]
    public void LoadBook_NeverOpened_StartsAtTheBeginningNotAnArbitraryOffset()
    {
        int bookId = AddBook(firstChapterEmpty: false);

        var vm = CreateViewModel(bookId);

        Assert.Equal("The Beginning", vm.ChapterTitle);
    }

    [Fact]
    public void ToggleBookmark_AddsThenRemoves_ReflectedInBookmarksListAndFlag()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(bookId);

        vm.ToggleBookmarkCommand.Execute(null);

        Assert.True(vm.IsCurrentPositionBookmarked);
        Assert.Single(vm.Bookmarks);
        Assert.Equal("The Beginning", vm.Bookmarks[0].ChapterTitle);
        Assert.NotEmpty(vm.Bookmarks[0].Excerpt);

        vm.ToggleBookmarkCommand.Execute(null);

        Assert.False(vm.IsCurrentPositionBookmarked);
        Assert.Empty(vm.Bookmarks);
    }

    [Fact]
    public void Bookmark_SurvivesReopeningTheBook()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(bookId);
        vm.ToggleBookmarkCommand.Execute(null);

        var reopened = CreateViewModel(bookId);

        Assert.Single(reopened.Bookmarks);
    }

    [Fact]
    public void GoToBookmark_NavigatesToItsChapterAndClosesTheDrawer()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(bookId);
        vm.GoToChapterCommand.Execute(vm.TableOfContents[1]); // move off chapter 0 first
        vm.ToggleBookmarkCommand.Execute(null); // bookmark "The End"
        vm.GoToChapterCommand.Execute(vm.TableOfContents[0]); // move away again
        vm.OpenBookmarksCommand.Execute(null);

        vm.GoToBookmarkCommand.Execute(vm.Bookmarks[0]);

        Assert.Equal("The End", vm.ChapterTitle);
        Assert.False(vm.IsBookmarksOpen);
    }

    [Fact]
    public void DeleteBookmark_RemovesItAndClearsTheFlagIfAtThatPosition()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(bookId);
        vm.ToggleBookmarkCommand.Execute(null);
        var bookmark = vm.Bookmarks[0];

        vm.DeleteBookmarkCommand.Execute(bookmark);

        Assert.Empty(vm.Bookmarks);
        Assert.False(vm.IsCurrentPositionBookmarked);
    }

    [Fact]
    public void Search_FindsMatchInAnotherChapter_AndCanNavigateToIt()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(bookId);

        vm.SearchQuery = "quietly";

        Assert.Single(vm.SearchResults);
        Assert.Equal("The End", vm.SearchResults[0].ChapterTitle);
        Assert.False(vm.HasNoSearchResults);

        vm.GoToSearchResultCommand.Execute(vm.SearchResults[0]);

        Assert.Equal("The End", vm.ChapterTitle);
        Assert.False(vm.IsSearchOpen);
    }

    [Fact]
    public void Search_NoMatches_SetsEmptyStateFlag()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(bookId);

        vm.SearchQuery = "xyzzy";

        Assert.Empty(vm.SearchResults);
        Assert.True(vm.HasNoSearchResults);
    }

    [Fact]
    public void Search_ShortQuery_DoesNotSearchOrShowEmptyState()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(bookId);

        vm.SearchQuery = "a";

        Assert.Empty(vm.SearchResults);
        Assert.False(vm.HasNoSearchResults);
    }

    // --- Finished / ChapterCount (docs/superpowers/specs/2026-08-27-books-screen-chrome-and-home-
    // strip-design.md) ---

    private Book ReadBook(int id)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        return context.Books.Single(b => b.Id == id);
    }

    [Fact]
    public void LoadBook_PopulatesChapterCount_FromTheSource()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        CreateViewModel(bookId);

        // The fixture EPUB has two chapters.
        Assert.Equal(2, ReadBook(bookId).ChapterCount);
    }

    [Fact]
    public void LoadBook_ClearsFinished_WhenReopeningForReading()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Books.Single(b => b.Id == bookId).Finished = true;
            context.SaveChanges();
        }

        CreateViewModel(bookId);

        Assert.False(ReadBook(bookId).Finished);
    }

    [Fact]
    public void NextPage_PastTheEndOfTheLastChapter_MarksFinished()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(bookId);

        // Page well past the end - the terminal branch is a safe no-op once there.
        for (int i = 0; i < 12; i++)
        {
            vm.NextPageCommand.Execute(null);
        }

        Assert.True(ReadBook(bookId).Finished);
    }
}
