using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="BookDetailScreenViewModel"/> (docs/superpowers/specs/2026-08-27-book-
/// details-screen-design.md, Piece B1). Temp SQLite file via
/// <see cref="PaperbunkrDbContext.DatabasePathOverride"/>, same pattern as
/// <see cref="BooksScreenViewModelTests"/>; runs under <see cref="AvaloniaTestCollection"/> since
/// <see cref="BookCardSample"/> / cover-brush construction needs Avalonia.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookDetailScreenViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly string _epubPath;

    public BookDetailScreenViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookdetail_vm_test_{Guid.NewGuid():N}.db");
        _epubPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookdetail_vm_test_{Guid.NewGuid():N}.epub");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _dbPath, _epubPath })
        {
            try { if (File.Exists(p)) File.Delete(p); } catch (IOException) { }
        }
    }

    private static BookDetailScreenViewModel CreateViewModel(
        Action? goBooks = null,
        Action<int, BookFormat, BookPosition?>? goReader = null,
        Action<int>? goEdit = null,
        Action<IReadOnlyList<int>>? goBulkEdit = null,
        Action<int>? goEditSeries = null) =>
        new(goBooks ?? (() => { }), goReader ?? ((_, _, _) => { }), goEdit, goBulkEdit, goEditSeries);

    private int AddBook(string title, string? author = null, string? summary = null,
        BookFormat format = BookFormat.Epub, bool realEpub = false, string? series = null,
        int chapterCount = 0, int lastChapterIndex = 0, int lastCharacterOffset = 0,
        bool finished = false, DateTime? lastOpened = null, DateTime? published = null)
    {
        string filePath = realEpub
            ? EpubFixture.Create(_epubPath, title: title, author: author ?? "Ada Author")
            : $@"C:\books\{title}.epub";

        using var context = PaperbunkrDb.CreateContext();
        int? seriesId = null;
        if (series is not null)
        {
            var bs = context.BookSeries.FirstOrDefault(s => s.Name == series) ?? new BookSeries { Name = series };
            if (bs.Id == 0) { context.BookSeries.Add(bs); context.SaveChanges(); }
            seriesId = bs.Id;
        }

        var book = new Book
        {
            Title = title,
            Author = author,
            Summary = summary,
            BookSeriesId = seriesId,
            Format = format,
            FilePath = filePath,
            AddedTime = DateTime.UtcNow,
            PublishedDate = published,
            ChapterCount = chapterCount,
            LastChapterIndex = lastChapterIndex,
            LastCharacterOffset = lastCharacterOffset,
            Finished = finished,
            LastOpenedTime = lastOpened,
        };
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private Book GetBook(int id)
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.Books.Single(b => b.Id == id);
    }

    [Fact]
    public void LoadBook_PopulatesHeaderFields()
    {
        int id = AddBook("Dune", author: "Frank Herbert", series: "Dune Chronicles", published: new DateTime(1965, 8, 1));
        var vm = CreateViewModel();

        vm.LoadBook(id);

        Assert.Equal(BookDetailMode.Book, vm.Mode);
        Assert.True(vm.IsBookMode);
        Assert.Equal("Dune", vm.Title);
        Assert.Equal("Frank Herbert", vm.Author);
        Assert.True(vm.HasAuthor);
        Assert.Equal("EPUB", vm.FormatBadge);
        Assert.True(vm.HasSeries);
        Assert.Equal("Part of Dune Chronicles ▸", vm.SeriesLinkLabel);
        Assert.True(vm.HasPublished);
        Assert.StartsWith("Added ", vm.AddedLabel);
        Assert.Equal("← Books", vm.BackLabel);
    }

    [Fact]
    public void LoadBook_MissingBook_NavigatesBackToBooks()
    {
        bool wentBack = false;
        var vm = CreateViewModel(goBooks: () => wentBack = true);

        vm.LoadBook(999);

        Assert.True(wentBack);
    }

    [Fact]
    public void LoadBook_ChapterProgress_ComputesFractionAndLabel()
    {
        int id = AddBook("Progressed", chapterCount: 10, lastChapterIndex: 3, lastOpened: DateTime.UtcNow);
        var vm = CreateViewModel();

        vm.LoadBook(id);

        Assert.True(vm.HasChapterProgress);
        Assert.Equal(3d / 9d, vm.ProgressFraction, 3);
        Assert.Equal("Chapter 4 of 10", vm.ProgressLabel);
        Assert.Equal("Continue — Chapter 4", vm.ContinueLabel);
    }

    [Fact]
    public void LoadBook_NeverOpened_ShowsNotStarted()
    {
        int id = AddBook("Fresh", chapterCount: 5);
        var vm = CreateViewModel();

        vm.LoadBook(id);

        Assert.Equal("Not started", vm.LastOpenedLabel);
        Assert.Equal("Start reading", vm.ContinueLabel);
    }

    [Fact]
    public void LoadBook_Finished_ShowsReadAgain()
    {
        int id = AddBook("Done", chapterCount: 4, lastChapterIndex: 3, finished: true, lastOpened: DateTime.UtcNow);
        var vm = CreateViewModel();

        vm.LoadBook(id);

        Assert.True(vm.IsFinished);
        Assert.Equal("Read again", vm.ContinueLabel);
        Assert.Equal("Mark as unread", vm.FinishedToggleLabel);
    }

    [Fact]
    public void ToggleFinished_FromUnfinished_SetsFinished()
    {
        int id = AddBook("ToFinish", chapterCount: 4, lastChapterIndex: 2, lastOpened: DateTime.UtcNow);
        var vm = CreateViewModel();
        vm.LoadBook(id);

        vm.ToggleFinishedCommand.Execute(null);

        Assert.True(GetBook(id).Finished);
        Assert.True(vm.IsFinished);
    }

    [Fact]
    public void ToggleFinished_FromFinished_ClearsFinishedAndResetsProgress()
    {
        int id = AddBook("Rewind", chapterCount: 4, lastChapterIndex: 3, lastCharacterOffset: 120, finished: true, lastOpened: DateTime.UtcNow);
        var vm = CreateViewModel();
        vm.LoadBook(id);

        vm.ToggleFinishedCommand.Execute(null);

        var book = GetBook(id);
        Assert.False(book.Finished);
        Assert.Equal(0, book.LastChapterIndex);
        Assert.Equal(0, book.LastCharacterOffset);
        Assert.NotNull(book.LastOpenedTime); // history kept
    }

    [Fact]
    public void LoadBook_Pdf_HidesChaptersProgressAndBookmarks()
    {
        int id = AddBook("Scanned", format: BookFormat.Pdf, chapterCount: 0, lastOpened: DateTime.UtcNow);
        var vm = CreateViewModel();

        vm.LoadBook(id);

        Assert.False(vm.HasChapterProgress);
        Assert.False(vm.HasChapters);
        Assert.False(vm.HasBookmarks);
        Assert.Equal("PDF", vm.FormatBadge);
    }

    [Fact]
    public void LoadBook_ShortSummary_HidesToggle_LongSummaryShowsIt()
    {
        int shortId = AddBook("Terse", summary: "A brief tale.");
        int longId = AddBook("Verbose", summary: new string('x', 400));
        var vm = CreateViewModel();

        vm.LoadBook(shortId);
        Assert.False(vm.IsSynopsisToggleVisible);

        vm.LoadBook(longId);
        Assert.True(vm.IsSynopsisToggleVisible);
        Assert.Equal("Show more ▼", vm.SynopsisToggleLabel);
        vm.ToggleSynopsisCommand.Execute(null);
        Assert.Equal("Show less ▲", vm.SynopsisToggleLabel);
    }

    [Fact]
    public void LoadBook_NoSummary_ShowsFallbackText()
    {
        int id = AddBook("Blank");
        var vm = CreateViewModel();

        vm.LoadBook(id);

        Assert.Equal("No summary available.", vm.Summary);
        Assert.False(vm.IsSynopsisToggleVisible);
    }

    [Fact]
    public void LoadBook_RealEpub_ListsChapters()
    {
        int id = AddBook("RealBook", realEpub: true, chapterCount: 2);
        var vm = CreateViewModel();

        vm.LoadBook(id);

        Assert.True(vm.HasChapters);
        Assert.Equal(2, vm.Chapters.Count);
        Assert.Equal("The Beginning", vm.Chapters[0].Title);
        Assert.Equal("The End", vm.Chapters[1].Title);
    }

    [Fact]
    public void OpenChapter_OpensReaderAtThatChapter()
    {
        int id = AddBook("RealBook", realEpub: true, chapterCount: 2);
        (int Id, BookFormat Fmt, BookPosition? Pos)? captured = null;
        var vm = CreateViewModel(goReader: (b, f, p) => captured = (b, f, p));
        vm.LoadBook(id);

        vm.OpenChapterCommand.Execute(vm.Chapters[1]);

        Assert.NotNull(captured);
        Assert.Equal(id, captured!.Value.Id);
        Assert.Equal(new BookPosition(1, 0), captured.Value.Pos);
    }

    [Fact]
    public void Continue_OpensReaderWithNullPosition()
    {
        int id = AddBook("ResumeMe", chapterCount: 3, lastChapterIndex: 1, lastOpened: DateTime.UtcNow);
        (int Id, BookFormat Fmt, BookPosition? Pos)? captured = null;
        var vm = CreateViewModel(goReader: (b, f, p) => captured = (b, f, p));
        vm.LoadBook(id);

        vm.ContinueCommand.Execute(null);

        Assert.NotNull(captured);
        Assert.Null(captured!.Value.Pos);
    }

    [Fact]
    public void Bookmarks_RenderNewestFirstWithChapterTitles_AndDeleteRemovesThem()
    {
        int id = AddBook("Marked", realEpub: true, chapterCount: 2);
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.BookBookmarks.Add(new BookBookmark { BookId = id, ChapterIndex = 0, CharacterOffset = 5, Excerpt = "older", CreatedTime = DateTime.UtcNow.AddHours(-2) });
            context.BookBookmarks.Add(new BookBookmark { BookId = id, ChapterIndex = 1, CharacterOffset = 9, Excerpt = "newer", CreatedTime = DateTime.UtcNow });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadBook(id);

        Assert.True(vm.HasBookmarks);
        Assert.Equal(2, vm.Bookmarks.Count);
        Assert.Equal("newer", vm.Bookmarks[0].Excerpt);
        Assert.Equal("The End", vm.Bookmarks[0].ChapterTitle);
        Assert.Equal("The Beginning", vm.Bookmarks[1].ChapterTitle);

        vm.DeleteBookmarkCommand.Execute(vm.Bookmarks[0]);

        Assert.Single(vm.Bookmarks);
        using var check = PaperbunkrDb.CreateContext();
        Assert.Single(check.BookBookmarks.Where(b => b.BookId == id));
    }

    [Fact]
    public void OpenBookmark_OpensReaderAtStoredPosition()
    {
        int id = AddBook("Marked", realEpub: true, chapterCount: 2);
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.BookBookmarks.Add(new BookBookmark { BookId = id, ChapterIndex = 1, CharacterOffset = 42, Excerpt = "x", CreatedTime = DateTime.UtcNow });
            context.SaveChanges();
        }
        (int Id, BookFormat Fmt, BookPosition? Pos)? captured = null;
        var vm = CreateViewModel(goReader: (b, f, p) => captured = (b, f, p));
        vm.LoadBook(id);

        vm.OpenBookmarkCommand.Execute(vm.Bookmarks[0]);

        Assert.Equal(new BookPosition(1, 42), captured!.Value.Pos);
    }

    [Fact]
    public void LoadSeries_ListsSeriesBooks_AndCount()
    {
        AddBook("Dune", series: "Dune Chronicles");
        AddBook("Dune Messiah", series: "Dune Chronicles");
        int seriesId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            seriesId = context.BookSeries.Single(s => s.Name == "Dune Chronicles").Id;
        }

        var vm = CreateViewModel();
        vm.LoadSeries(seriesId);

        Assert.Equal(BookDetailMode.Series, vm.Mode);
        Assert.True(vm.IsSeriesMode);
        Assert.Equal("Dune Chronicles", vm.SeriesName);
        Assert.Equal("2 books", vm.SeriesBookCountLabel);
        Assert.Equal(2, vm.SeriesBooks.Count);
        Assert.Equal("← Books", vm.BackLabel);
    }

    [Fact]
    public void SeriesLink_ThenOpenBook_BackLabelReturnsToSeries()
    {
        int id = AddBook("Dune", series: "Dune Chronicles");
        AddBook("Dune Messiah", series: "Dune Chronicles");
        var vm = CreateViewModel();
        vm.LoadBook(id);

        vm.OpenSeriesFromLinkCommand.Execute(null);
        Assert.True(vm.IsSeriesMode);

        var sibling = vm.SeriesBooks.First(c => c.Title == "Dune Messiah");
        vm.OpenBookFromSeriesCommand.Execute(sibling);

        Assert.True(vm.IsBookMode);
        Assert.Equal("← Dune Chronicles", vm.BackLabel);

        bool wentBackToBooks = false;
        vm = CreateViewModel(goBooks: () => wentBackToBooks = true);
        vm.LoadBook(id);
        vm.GoBackCommand.Execute(null);
        Assert.True(wentBackToBooks);
    }

    [Fact]
    public void DeleteBook_RemovesRowAndNavigatesBack()
    {
        int id = AddBook("Doomed");
        bool wentBack = false;
        var vm = CreateViewModel(goBooks: () => wentBack = true);
        vm.LoadBook(id);

        vm.DeleteBookCommand.Execute(null);

        Assert.True(wentBack);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.Books.Where(b => b.Id == id));
    }

    [Fact]
    public void DeleteBook_LastInSeries_PrunesTheSeries()
    {
        int id = AddBook("Only", series: "Vanishing Series");
        var vm = CreateViewModel();
        vm.LoadBook(id);

        vm.DeleteBookCommand.Execute(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.BookSeries.Where(s => s.Name == "Vanishing Series"));
    }

    [Fact]
    public void EditAllSeriesBooks_InvokesBulkCallback_WithEverySeriesBookId()
    {
        AddBook("Dune", series: "Dune Chronicles");
        AddBook("Dune Messiah", series: "Dune Chronicles");
        int seriesId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            seriesId = context.BookSeries.Single(s => s.Name == "Dune Chronicles").Id;
        }

        IReadOnlyList<int>? captured = null;
        var vm = CreateViewModel(goBulkEdit: ids => captured = ids);
        vm.LoadSeries(seriesId);

        vm.EditAllSeriesBooksCommand.Execute(null);

        Assert.NotNull(captured);
        Assert.Equal(2, captured!.Count);
    }

    [Fact]
    public void EditSeries_InvokesSeriesEditorCallback()
    {
        AddBook("Book", series: "Editable Series");
        int seriesId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            seriesId = context.BookSeries.Single(s => s.Name == "Editable Series").Id;
        }

        int? captured = null;
        var vm = CreateViewModel(goEditSeries: id => captured = id);
        vm.LoadSeries(seriesId);

        vm.EditSeriesCommand.Execute(null);

        Assert.Equal(seriesId, captured);
    }

    // --- Streaming redesign (docs/superpowers/specs/2026-08-28-detail-screens-streaming-redesign-design.md) ---

    [Fact]
    public void BookMode_BandHasNoMetadataGroups()
    {
        var vm = CreateViewModel();
        vm.LoadBook(AddBook("A Novel", author: "N. Novelist", summary: "A synopsis."));

        Assert.Empty(vm.Band.Groups);
        Assert.Equal("A synopsis.", vm.Band.Summary);
        Assert.Equal("EPUB", vm.Band.StatusText);
        Assert.Equal("N. Novelist", vm.Band.PublisherText);
    }

    [Fact]
    public void BookMode_HeroFields()
    {
        var vm = CreateViewModel();
        vm.LoadBook(AddBook("A Novel", author: "N. Novelist", format: BookFormat.Pdf, finished: true));

        var hero = (IDetailHeaderSource)vm;
        Assert.Equal("A Novel", hero.Title);
        Assert.Contains("N. Novelist", hero.MetaLine);
        Assert.Contains("PDF", hero.MetaLine);
        Assert.Contains("FINISHED", hero.MetaLine);
        Assert.Null(hero.SecondaryTitle);
        Assert.Null(hero.TrackerProgress);
        Assert.Contains(hero.Actions, a => a.IsPrimary);
    }

    [Fact]
    public void SeriesMode_HeroSwitchesTitleAndActions()
    {
        AddBook("Vol 1", series: "The Trilogy");
        AddBook("Vol 2", series: "The Trilogy");
        int seriesId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            seriesId = context.BookSeries.Single(s => s.Name == "The Trilogy").Id;
        }

        var vm = CreateViewModel();
        vm.LoadSeries(seriesId);

        var hero = (IDetailHeaderSource)vm;
        Assert.Equal("The Trilogy", hero.Title);
        Assert.Contains("2 books", hero.MetaLine);
        Assert.DoesNotContain(hero.Actions, a => a.IsPrimary);
        Assert.Contains(hero.Actions, a => a.Label == "Edit all books");
    }
}
