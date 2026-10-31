using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Book Detail's tabs, reading dashboard, chapter state, notes hub and author link (docs/superpowers/specs/2026-10-27-books-slice2b-detail-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookDetailTabsTests : IDisposable
{
    private readonly string? _originalOverride;
    private readonly string _dbPath;
    private readonly string _root;

    public BookDetailTabsTests()
    {
        _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_detailtabs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "db.sqlite");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static BookDetailScreenViewModel Create(Action<int, BookFormat, BookPosition?>? reader = null) =>
        new(() => { }, reader ?? ((_, _, _) => { }));

    private int AddEpub(string title, Action<Book>? configure = null, bool withParts = true)
    {
        string path = Path.Combine(_root, $"{Guid.NewGuid():N}.epub");
        EpubFixture.Create(path, title: title, withParts: withParts);
        var book = new Book { Title = title, Author = "Ada Author", Format = BookFormat.Epub, FilePath = path, AddedTime = DateTime.UtcNow };
        configure?.Invoke(book);
        using var context = PaperbunkrDb.CreateContext();
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private int AddPdf(string title)
    {
        var book = new Book { Title = title, Format = BookFormat.Pdf, FilePath = Path.Combine(_root, "x.pdf"), AddedTime = DateTime.UtcNow };
        using var context = PaperbunkrDb.CreateContext();
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    // ---- Read button ----

    [Fact]
    public void ReadLabel_ShowsChapterAndPercent_WhenInProgress()
    {
        int id = AddEpub("Half", b =>
        {
            b.Status = BookReadingStatus.Reading; b.LastOpenedTime = DateTime.UtcNow; b.ChapterCount = 4; b.LastChapterIndex = 1; b.LastProgressionFraction = 0.5;
        });
        var vm = Create();

        vm.LoadBook(id);

        Assert.Equal("Continue — Ch. 2 · 38%", vm.ContinueLabel); // (1 + 0.5) / 4
    }

    // ---- chapter state ----

    [Fact]
    public void Chapters_AreReadCurrentOrUnread_WithRelativeLengths()
    {
        int id = AddEpub("Three", b =>
        {
            b.Status = BookReadingStatus.Reading; b.LastOpenedTime = DateTime.UtcNow; b.ChapterCount = 3; b.LastChapterIndex = 1; b.LastProgressionFraction = 0.25;
        });
        var vm = Create();

        vm.LoadBook(id);

        Assert.Equal(3, vm.Chapters.Count);
        Assert.Equal(new[] { BookChapterState.Read, BookChapterState.Current, BookChapterState.Unread }, vm.Chapters.Select(c => c.State));
        Assert.Equal(0.25, vm.Chapters[1].CurrentFraction);
        Assert.Equal(0d, vm.Chapters[0].CurrentFraction);
        Assert.Equal(1d, vm.Chapters.Max(c => c.LengthFraction), 3);
        Assert.All(vm.Chapters, c => Assert.InRange(c.LengthFraction, 0d, 1d));
        Assert.True(vm.Chapters[0].IsRead && vm.Chapters[1].IsCurrent && vm.Chapters[2].IsUnread);
    }

    [Fact]
    public void Chapters_AreAllRead_WhenFinished_AndAllUnread_WhenNeverOpened()
    {
        int finished = AddEpub("Done", b => { b.Status = BookReadingStatus.Finished; b.LastOpenedTime = DateTime.UtcNow; b.LastChapterIndex = 0; });
        int fresh = AddEpub("Fresh");
        var vm = Create();

        vm.LoadBook(finished);
        Assert.All(vm.Chapters, c => Assert.Equal(BookChapterState.Read, c.State));

        vm.LoadBook(fresh);
        Assert.Equal(BookChapterState.Unread, vm.Chapters[0].State); // never opened: chapter 0 is not "current"
    }

    // ---- tabs ----

    [Fact]
    public void Tabs_DependOnFormatAndContent_AndResetPerBook()
    {
        int epub = AddEpub("Book");
        int pdf = AddPdf("Scan");
        var vm = Create();

        vm.LoadBook(epub);
        Assert.True(vm.ShowChaptersTab);
        Assert.False(vm.ShowNotesTab); // nothing to list yet
        Assert.True(vm.IsOverviewTab);

        vm.SetTabCommand.Execute(BookDetailTab.Chapters);
        Assert.True(vm.IsChaptersTab);
        vm.ReloadCurrentBook();
        Assert.True(vm.IsChaptersTab); // reloading the same book keeps the tab

        vm.LoadBook(pdf);
        Assert.True(vm.IsOverviewTab); // a different book starts on Overview
        Assert.False(vm.ShowChaptersTab);
        Assert.True(vm.ShowNotesTab); // a PDF's captured regions live in Notes
    }

    // ---- notes ----

    private void AddNotes(int bookId)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.BookHighlights.AddRange(
            new BookHighlight { BookId = bookId, ChapterIndex = 1, BlockId = "pb-p9", Excerpt = "later in ch 2", Color = BookHighlightColor.Pink, CreatedTime = new DateTime(2026, 10, 3) },
            new BookHighlight { BookId = bookId, ChapterIndex = 1, BlockId = "pb-p2", Excerpt = "early in ch 2", Color = BookHighlightColor.Yellow, Note = "mine", CreatedTime = new DateTime(2026, 10, 4) },
            new BookHighlight { BookId = bookId, ChapterIndex = 0, BlockId = "pb-p5", Excerpt = "in ch 1", Color = BookHighlightColor.Yellow, CreatedTime = new DateTime(2026, 10, 5) });
        context.BookBookmarks.Add(new BookBookmark { BookId = bookId, ChapterIndex = 0, BlockId = "pb-p1", Excerpt = "bookmark at start", CreatedTime = new DateTime(2026, 10, 6) });
        context.SaveChanges();
    }

    [Fact]
    public void Notes_ListHighlightsAndBookmarks_InBookOrder_AndFilter()
    {
        int id = AddEpub("Annotated");
        AddNotes(id);
        var vm = Create();

        vm.LoadBook(id);

        Assert.True(vm.ShowNotesTab);
        Assert.Equal(new[] { "bookmark at start", "in ch 1", "early in ch 2", "later in ch 2" }, vm.Notes.Select(n => n.Excerpt));
        Assert.True(vm.HasHighlights);

        vm.SetNoteKindFilterCommand.Execute(BookNoteKindFilter.Bookmarks);
        Assert.Equal(new[] { "bookmark at start" }, vm.Notes.Select(n => n.Excerpt));
        Assert.True(vm.IsKindBookmarks);

        vm.SetNoteKindFilterCommand.Execute(BookNoteKindFilter.All);
        vm.ToggleNoteColorCommand.Execute(BookHighlightColor.Yellow);
        Assert.Equal(new[] { "in ch 1", "early in ch 2" }, vm.Notes.Select(n => n.Excerpt));
        Assert.True(vm.IsColorYellow);
        Assert.True(vm.Notes[1].HasNote);

        vm.ToggleNoteColorCommand.Execute(BookHighlightColor.Yellow); // the active colour clears the filter
        Assert.Equal(4, vm.Notes.Count);

        vm.ToggleNoteColorCommand.Execute(BookHighlightColor.Blue);
        Assert.Empty(vm.Notes);
        Assert.True(vm.ShowNoNoteMatches);
        Assert.False(vm.ShowNoNotes);
    }

    [Fact]
    public void OpenNote_GoesToTheNotesBlock_AndDeleteRemovesItEverywhere()
    {
        int id = AddEpub("Annotated");
        AddNotes(id);
        BookPosition? opened = null;
        var vm = Create((_, _, position) => opened = position);
        vm.LoadBook(id);
        var early = vm.Notes.Single(n => n.Excerpt == "early in ch 2");

        vm.OpenNoteCommand.Execute(early);
        Assert.Equal(new BookPosition(1, "pb-p2"), opened);

        vm.DeleteNoteCommand.Execute(early);
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.DoesNotContain(context.BookHighlights, h => h.Excerpt == "early in ch 2");
        }

        vm.ReloadCurrentBook(); // the list refresh after delete is posted to the UI thread; a reload shows the persisted truth
        Assert.DoesNotContain(vm.Notes, n => n.Excerpt == "early in ch 2");
        Assert.Equal(3, vm.Notes.Count);
    }

    [Fact]
    public void PdfRegions_ListByPage_AndOpenWithoutAPosition()
    {
        int id = AddPdf("Scan");
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.BookAnnotationImages.AddRange(
                new BookAnnotationImage { BookId = id, PageIndex = 7, ImagePath = "b.png", Note = "chart", CreatedTime = DateTime.UtcNow },
                new BookAnnotationImage { BookId = id, PageIndex = 2, ImagePath = "a.png", CreatedTime = DateTime.UtcNow });
            context.SaveChanges();
        }

        bool called = false;
        BookPosition? position = new BookPosition(99);
        var vm = Create((_, _, p) => { called = true; position = p; });
        vm.LoadBook(id);

        Assert.Equal(new[] { 2, 7 }, vm.Notes.Select(n => n.PageIndex));
        Assert.Equal("Page 3", vm.Notes[0].Where);
        Assert.Equal("Captured region", vm.Notes[0].KindLabel);

        vm.OpenNoteCommand.Execute(vm.Notes[1]);
        Assert.True(called);
        Assert.Null(position); // the PDF reader takes no start position yet
    }

    // ---- dashboard ----

    [Fact]
    public void Dashboard_ShowsOnlyWhatTheLogCanSay()
    {
        int id = AddEpub("Read a bit", b => { b.CharacterCount = 360_000; b.Status = BookReadingStatus.Reading; b.LastOpenedTime = DateTime.UtcNow; b.ChapterCount = 3; b.LastChapterIndex = 1; });
        var vm = Create();
        vm.LoadBook(id);
        Assert.False(vm.HasStats);
        Assert.False(vm.ShowStats);

        using (var context = PaperbunkrDb.CreateContext())
        {
            for (int i = 1; i <= 2; i++)
            {
                context.ReadingEvents.Add(new ReadingEvent
                {
                    ItemType = ReadingItemType.Novel, ItemId = id, Kind = ReadingEventKind.Opened,
                    TimestampUtc = new DateTime(2026, 10, i, 20, 0, 0, DateTimeKind.Utc), ActiveSeconds = 1200, PagesRead = 20,
                });
            }

            context.SaveChanges();
        }

        vm.ReloadCurrentBook();

        Assert.True(vm.HasStats);
        Assert.False(vm.ShowStats); // books redesign §4.1: the dashboard moved to its own tab
        vm.SetTabCommand.Execute(BookDetailTab.YourReading);
        Assert.True(vm.ShowStats);
        var tiles = vm.StatTiles.ToDictionary(t => t.Label, t => t.Value);
        Assert.Equal("2", tiles["Sessions"]);
        Assert.Equal("40m", tiles["Time read"]);
        Assert.Equal("60 pages/hour", tiles["Pace"]);
        Assert.StartsWith("About ", tiles["Time left"]);

        vm.SetTabCommand.Execute(BookDetailTab.Chapters);
        Assert.False(vm.ShowStats); // Your reading only
    }

    [Theory]
    [InlineData(0, "1m")]
    [InlineData(45, "45m")]
    [InlineData(60, "1h")]
    [InlineData(200, "3h 20m")]
    public void FormatDuration_IsCompact(int minutes, string expected)
    {
        Assert.Equal(expected, BookDetailScreenViewModel.FormatDuration(TimeSpan.FromMinutes(minutes)));
    }

    // ---- author link ----

    [Fact]
    public void OpenAuthor_PassesTheAuthorName_AndDoesNothingWithoutOne()
    {
        int named = AddEpub("Named");
        int anonymous = AddEpub("Anonymous", b => b.Author = null);
        var asked = new List<string>();
        var vm = Create();
        vm.GoBooksForAuthor = asked.Add;

        vm.LoadBook(named);
        vm.OpenAuthorCommand.Execute(null);
        vm.LoadBook(anonymous);
        vm.OpenAuthorCommand.Execute(null);

        Assert.Equal(new[] { "Ada Author" }, asked);
    }
}
