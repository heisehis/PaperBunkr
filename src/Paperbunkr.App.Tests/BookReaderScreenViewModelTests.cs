using Avalonia;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
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

    /// <summary>
    /// docs/superpowers/specs/2026-09-01-books-reader-screen-reader-accessibility-design.md -
    /// <see cref="BookReaderScreenViewModel.ReadingPositionAnnouncement"/> is the live-region string
    /// BookReaderScreen.axaml's hidden LiveSetting="Polite" TextBlock renders.
    /// </summary>
    [Fact]
    public void RecomputeCurrentPage_OnLoad_SetsReadingPositionAnnouncementToChapterTitle()
    {
        int bookId = AddBook(firstChapterEmpty: false);

        var vm = CreateViewModel(bookId);

        Assert.Equal("The Beginning", vm.ReadingPositionAnnouncement);
    }

    [Fact]
    public void GoToChapter_UpdatesReadingPositionAnnouncement()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(bookId);

        vm.GoToChapterCommand.Execute(vm.TableOfContents[1]);

        Assert.Equal("The End", vm.ReadingPositionAnnouncement);
    }

    [Fact]
    public void AnnounceReadingPositionCommand_BuildsChapterOfTotalString()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(bookId);

        vm.AnnounceReadingPositionCommand.Execute(null);
        Assert.Equal("Chapter 1 of 2: The Beginning", vm.ReadingPositionAnnouncement);

        vm.GoToChapterCommand.Execute(vm.TableOfContents[1]);
        vm.AnnounceReadingPositionCommand.Execute(null);
        Assert.Equal("Chapter 2 of 2: The End", vm.ReadingPositionAnnouncement);
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

    // --- Reader ergonomics settings persistence (docs/superpowers/specs/2026-09-01-books-reader-
    // ergonomics-and-annotations-design.md) ---

    [Fact]
    public void LoadBook_WithNoOverride_SeedsSettingsFromGlobalAppSettingsDefaults()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var settings = context.GetOrCreateAppSettings();
            settings.BookReaderFontSize = 22;
            settings.BookReaderTheme = BookTheme.Sepia;
            context.SaveChanges();
        }

        int bookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(bookId);

        Assert.Equal(22, vm.Settings.FontSize);
        Assert.Equal(BookTheme.Sepia, vm.Settings.Theme);
    }

    [Fact]
    public void CloseFontSheet_PersistsCurrentSettingsAsBookOverride()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(bookId);

        vm.OpenFontSheetCommand.Execute(null);
        vm.Settings.FontSize = 24;
        vm.Settings.Theme = BookTheme.OledBlack;
        vm.CloseFontSheetCommand.Execute(null);

        var book = ReadBook(bookId);
        Assert.Equal(24, book.FontSizeOverride);
        Assert.Equal(BookTheme.OledBlack, book.ThemeOverride);
    }

    [Fact]
    public void LoadBook_WithAnOverride_TakesPriorityOverTheGlobalDefault()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().BookReaderFontSize = 17;
            var book = context.Books.Single(b => b.Id == bookId);
            book.FontSizeOverride = 20;
            context.SaveChanges();
        }

        var vm = CreateViewModel(bookId);

        Assert.Equal(20, vm.Settings.FontSize);
    }

    [Fact]
    public void SwitchingBooksInTheSameReader_DoesNotLeakOneBooksOverrideIntoAnother()
    {
        int firstBookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(firstBookId);
        vm.OpenFontSheetCommand.Execute(null);
        vm.Settings.FontSize = 26;
        vm.CloseFontSheetCommand.Execute(null);

        string secondEpubPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_vm_test_{Guid.NewGuid():N}.epub");
        EpubFixture.Create(secondEpubPath);
        int secondBookId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var second = new Book { Title = "Second", FilePath = secondEpubPath, Format = BookFormat.Epub, AddedTime = DateTime.UtcNow };
            context.Books.Add(second);
            context.SaveChanges();
            secondBookId = second.Id;
        }

        try
        {
            vm.LoadBook(secondBookId);

            // Second book has no override of its own - should resolve to the (unchanged) global
            // default, not the 26 the first book's override picked up.
            Assert.NotEqual(26, vm.Settings.FontSize);
            Assert.Null(ReadBook(secondBookId).FontSizeOverride);
            Assert.Equal(26, ReadBook(firstBookId).FontSizeOverride);
        }
        finally
        {
            File.Delete(secondEpubPath);
        }
    }

    /// <summary>
    /// Real bug found via manual testing 2026-09-02 (books-reader-ergonomics-and-annotations): the
    /// reflow reader rendered a permanently blank text area, with the toolbar/chrome still showing
    /// correct data, on any book load after the first one in a given reader-screen instance. Root
    /// cause - LoadBook's Settings-resolution block (8 property assignments) fired the constructor's
    /// Settings.PropertyChanged -&gt; RecomputeCurrentPage subscription, and _source?.Dispose() never
    /// nulled the field, so on a second LoadBook call RecomputeCurrentPage's own "_source is null"
    /// guard didn't actually stop it from running against a disposed source while _viewportSize was
    /// already valid (this app's screens attach eagerly at startup). Fixed via _isSeedingSettings
    /// (suppresses the recompute during that block) and explicitly nulling _source after Dispose.
    /// This test exercises the exact reused-VM, viewport-already-set, switch-books shape that
    /// triggered it, and asserts on the actually-visible symptom (CurrentPageParagraphs content),
    /// not just the override-resolution behavior the older sibling test above already covers.
    /// </summary>
    [Fact]
    public void SwitchingBooksInTheSameReader_PopulatesTheSecondBooksParagraphs_NotStaleOrEmpty()
    {
        int firstBookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(firstBookId);
        string firstBookFirstParagraphText = vm.CurrentPageParagraphs[0].Paragraph.Text;

        string secondEpubPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_vm_test_{Guid.NewGuid():N}.epub");
        EpubFixture.Create(secondEpubPath, title: "Second Book");
        int secondBookId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var second = new Book { Title = "Second Book", FilePath = secondEpubPath, Format = BookFormat.Epub, AddedTime = DateTime.UtcNow };
            context.Books.Add(second);
            context.SaveChanges();
            secondBookId = second.Id;
        }

        try
        {
            vm.LoadBook(secondBookId);

            Assert.NotEmpty(vm.CurrentPageParagraphs);
            Assert.Equal("The Beginning", vm.ChapterTitle); // EpubFixture's default first-chapter title - proves this is book 2's real content, not book 1's stale state
            Assert.False(string.IsNullOrWhiteSpace(vm.CurrentPageParagraphs[0].Paragraph.Text));
        }
        finally
        {
            File.Delete(secondEpubPath);
        }
    }

    [Fact]
    public void AutoHideChromeToggle_WritesThroughToGlobalAppSettings_Immediately()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(bookId);

        vm.AutoHideChromeToggle = false;

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.False(context.GetOrCreateAppSettings().BookReaderAutoHideChrome);
    }

    // --- Highlights (docs/superpowers/specs/2026-09-01-books-reader-ergonomics-and-annotations-
    // design.md §"Highlight selection UX") ---

    [Fact]
    public void PickHighlightColor_AfterASelection_CreatesAPersistedHighlight()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(bookId);
        var paragraph = vm.CurrentPageParagraphs[0];

        vm.OnParagraphSelectionCompleted(paragraph, 0, 5, new Avalonia.Rect(0, 0, 40, 20));
        vm.PickHighlightColorCommand.Execute(BookHighlightColor.Green);

        Assert.Single(vm.Highlights);
        Assert.Equal(BookHighlightColor.Green, vm.Highlights[0].Color);
        Assert.False(vm.IsHighlightPopupOpen);

        using var context = new PaperbunkrDbContext(_dbOptions);
        var entity = context.BookHighlights.Single();
        Assert.Equal(paragraph.GlobalOffset, entity.StartOffset);
        Assert.Equal(paragraph.GlobalOffset + 5, entity.EndOffset);
        Assert.Equal(BookHighlightColor.Green, entity.Color);
    }

    [Fact]
    public void DeleteHighlight_RemovesItFromCollectionAndDatabase()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(bookId);
        var paragraph = vm.CurrentPageParagraphs[0];
        vm.OnParagraphSelectionCompleted(paragraph, 0, 5, new Avalonia.Rect());
        vm.PickHighlightColorCommand.Execute(BookHighlightColor.Yellow);
        var highlight = vm.Highlights[0];

        vm.DeleteHighlightCommand.Execute(highlight);

        Assert.Empty(vm.Highlights);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Empty(context.BookHighlights);
    }

    [Fact]
    public void PickHighlightColor_WhileEditingAnExistingHighlight_UpdatesItInPlace()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        var vm = CreateViewModel(bookId);
        var paragraph = vm.CurrentPageParagraphs[0];
        vm.OnParagraphSelectionCompleted(paragraph, 0, 5, new Avalonia.Rect());
        vm.PickHighlightColorCommand.Execute(BookHighlightColor.Yellow);
        var created = vm.Highlights[0];

        var localHighlight = new ParagraphHighlight(0, 5, BookHighlightColor.Yellow);
        vm.OnParagraphHighlightTapped(localHighlight, new Avalonia.Rect());
        Assert.True(vm.IsEditingExistingHighlight);

        vm.PickHighlightColorCommand.Execute(BookHighlightColor.Blue);

        Assert.Single(vm.Highlights);
        Assert.Equal(created.Id, vm.Highlights[0].Id);
        Assert.Equal(BookHighlightColor.Blue, vm.Highlights[0].Color);
    }

    [Fact]
    public void LoadBook_ReflectsPersistedHighlightsInParagraphViewData()
    {
        int bookId = AddBook(firstChapterEmpty: false);
        var firstVm = CreateViewModel(bookId);
        var paragraph = firstVm.CurrentPageParagraphs[0];
        firstVm.OnParagraphSelectionCompleted(paragraph, 0, 5, new Avalonia.Rect());
        firstVm.PickHighlightColorCommand.Execute(BookHighlightColor.Pink);

        var reopened = CreateViewModel(bookId);

        Assert.Single(reopened.Highlights);
        var reopenedParagraph = reopened.CurrentPageParagraphs[0];
        Assert.Single(reopenedParagraph.Highlights);
        Assert.Equal(0, reopenedParagraph.Highlights[0].Start);
        Assert.Equal(5, reopenedParagraph.Highlights[0].End);
        Assert.Equal(BookHighlightColor.Pink, reopenedParagraph.Highlights[0].Color);
    }
}
