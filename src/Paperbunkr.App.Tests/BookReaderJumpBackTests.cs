using Avalonia;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Slice 3a: jump-back chip and the end-of-session toast on <see cref="BookReaderScreenViewModel"/>.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookReaderJumpBackTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _epubPath;

    public BookReaderJumpBackTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_jumpback_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
        _epubPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_jumpback_test_{Guid.NewGuid():N}.epub");
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

    private int AddBook()
    {
        EpubFixture.Create(_epubPath, firstChapterEmpty: false);
        using var context = new PaperbunkrDbContext(_dbOptions);
        var book = new Book { Title = "Test", FilePath = _epubPath, Format = BookFormat.Epub, AddedTime = DateTime.UtcNow };
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private BookReaderScreenViewModel Open(out DateTime[] clock, Action<ToastRequest>? toast = null, ReaderActivityTracker? tracker = null)
    {
        int bookId = AddBook();
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        var now = new[] { new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc) };
        clock = now;
        var vm = new BookReaderScreenViewModel(() => { }) { ShowToast = toast, Clock = () => now[0] };
        if (tracker is not null)
        {
            vm._activity = tracker;
        }

        vm.LoadBook(bookId);
        vm.UpdateViewportSize(new Size(700, 800));
        return vm;
    }

    [Fact]
    public void TocJumpToAnotherChapter_OffersJumpBack()
    {
        var vm = Open(out _);

        vm.GoToChapterCommand.Execute(vm.TableOfContents[1]);

        Assert.True(vm.HasJumpBack);
        Assert.Equal("Back to Ch. 1", vm.JumpBackLabel);
    }

    [Fact]
    public void JumpBack_ReturnsToTheOldChapterAndClearsTheChip()
    {
        var vm = Open(out _);
        vm.GoToChapterCommand.Execute(vm.TableOfContents[1]);
        Assert.Equal("The End", vm.ChapterTitle);

        Assert.True(vm.TryJumpBack());

        Assert.Equal("The Beginning", vm.ChapterTitle);
        Assert.False(vm.HasJumpBack);
        Assert.False(vm.TryJumpBack());
    }

    [Fact]
    public void JumpingBack_DoesNotOfferAnotherJumpBack()
    {
        var vm = Open(out _);
        vm.GoToChapterCommand.Execute(vm.TableOfContents[1]);

        vm.JumpBackCommand.Execute(null);

        Assert.False(vm.HasJumpBack);
    }

    [Fact]
    public void PageTurn_ClearsTheChip()
    {
        var vm = Open(out _);
        vm.GoToChapterCommand.Execute(vm.TableOfContents[0]);
        vm.GoToChapterCommand.Execute(vm.TableOfContents[1]);
        Assert.True(vm.HasJumpBack);

        vm.PreviousPageCommand.Execute(null);

        Assert.False(vm.HasJumpBack);
    }

    [Fact]
    public void PositionReportInsideTheSettleWindow_KeepsTheChip()
    {
        var vm = Open(out var clock);
        vm.GoToChapterCommand.Execute(vm.TableOfContents[1]);

        clock[0] += TimeSpan.FromMilliseconds(200);
        vm.OnPositionCaptured("pb-p1", 0, "x");

        Assert.True(vm.HasJumpBack);
    }

    [Fact]
    public void PositionReportAfterTheSettleWindow_ClearsTheChip()
    {
        var vm = Open(out var clock);
        vm.GoToChapterCommand.Execute(vm.TableOfContents[1]);

        clock[0] += BookReaderScreenViewModel.JumpBackSettle + TimeSpan.FromMilliseconds(1);
        vm.OnPositionCaptured("pb-p5", 0.3, "x");

        Assert.False(vm.HasJumpBack);
    }

    [Fact]
    public void ChipExpires()
    {
        var vm = Open(out _);
        vm.GoToChapterCommand.Execute(vm.TableOfContents[1]);

        vm.OnJumpBackExpired(null, EventArgs.Empty);

        Assert.False(vm.HasJumpBack);
        Assert.Null(vm.JumpBackLabel);
    }

    [Fact]
    public void JumpBackAction_IsDeclaredInTheBookReaderGroupWithAltLeft()
    {
        var info = InputActions.Core.Single(a => a.Id == InputActionIds.BookJumpBack);

        Assert.Equal(InputActions.BookReaderGroup, info.Group);
        Assert.Equal(InputScope.BookReader, info.Scope);
    }

    private static ReaderActivityTracker TrackerAt(DateTime[] clockBox, out Action advanceMinute)
    {
        var tracker = new ReaderActivityTracker(() => clockBox[0], useTimer: false);
        advanceMinute = () =>
        {
            tracker.NoteInput();
            clockBox[0] += TimeSpan.FromMinutes(1);
            tracker.Tick();
        };
        return tracker;
    }

    [Fact]
    public void Close_AfterFiveMinutes_ShowsASessionToast()
    {
        var clockBox = new[] { new DateTime(2026, 10, 27, 12, 0, 0) };
        var tracker = TrackerAt(clockBox, out var minute);
        ToastRequest? shown = null;
        var vm = Open(out _, t => shown = t, tracker);
        for (int i = 0; i < 6; i++)
        {
            minute();
        }

        vm.CloseCommand.Execute(null);

        Assert.NotNull(shown);
        Assert.Equal("Reading session", shown!.Title);
        Assert.StartsWith("6 min", shown.Message);
    }

    [Fact]
    public void Close_UnderFiveMinutes_ShowsNothing()
    {
        var clockBox = new[] { new DateTime(2026, 10, 27, 12, 0, 0) };
        var tracker = TrackerAt(clockBox, out var minute);
        ToastRequest? shown = null;
        var vm = Open(out _, t => shown = t, tracker);
        minute();
        minute();

        vm.CloseCommand.Execute(null);

        Assert.Null(shown);
    }
}
