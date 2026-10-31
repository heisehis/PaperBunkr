using Avalonia;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

public class BookReadingAidMathTests
{
    [Fact]
    public void RulerBand_IsTwoLinesTallCentredOnThePointer()
    {
        var (top, height) = BookRulerMath.Band(pointerY: 300, lineHeightPx: 25, viewportHeight: 800);

        Assert.Equal(50, height);
        Assert.Equal(275, top);
    }

    [Fact]
    public void RulerBand_StaysInsideTheViewport()
    {
        Assert.Equal(0, BookRulerMath.Band(2, 25, 800).Top);
        Assert.Equal(750, BookRulerMath.Band(799, 25, 800).Top);
    }

    [Fact]
    public void RulerBand_FillsATinyViewport()
    {
        var (top, height) = BookRulerMath.Band(10, 25, 40);

        Assert.Equal(0, top);
        Assert.Equal(40, height);
    }

    [Fact]
    public void RulerStep_MovesWholeLinesAndClamps()
    {
        Assert.Equal(125, BookRulerMath.Step(100, 25, 1, 800));
        Assert.Equal(75, BookRulerMath.Step(100, 25, -1, 800));
        Assert.Equal(0, BookRulerMath.Step(10, 25, -1, 800));
        Assert.Equal(750, BookRulerMath.Step(740, 25, 1, 800));
    }

    [Fact]
    public void AutoScrollSpeed_ScalesWithLineHeight()
    {
        double small = BookAutoScrollMath.PixelsPerSecond(3, 20);
        double big = BookAutoScrollMath.PixelsPerSecond(3, 40);

        Assert.Equal(small * 2, big, 6);
        Assert.Equal(40 * 0.15 * 3, big, 6);
    }

    [Fact]
    public void AutoScrollSpeed_IsClampedToOneThroughEight()
    {
        Assert.Equal(1, BookAutoScrollMath.ClampSpeed(-5));
        Assert.Equal(8, BookAutoScrollMath.ClampSpeed(99));
        Assert.Equal(BookAutoScrollMath.PixelsPerSecond(8, 30), BookAutoScrollMath.PixelsPerSecond(20, 30));
    }
}

[Collection(nameof(AvaloniaTestCollection))]
public class BookReaderAidsViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _epubPath;
    private readonly List<string> _extraEpubs = new();

    public BookReaderAidsViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_aids_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
        _epubPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_aids_test_{Guid.NewGuid():N}.epub");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        PaperbunkrDbContext.DatabasePathOverride = null;
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_epubPath)) File.Delete(_epubPath);
            foreach (var extra in _extraEpubs)
            {
                if (File.Exists(extra)) File.Delete(extra);
            }
        }
        catch (IOException)
        {
        }
    }

    private int AddBook(Action<Book>? configure = null)
    {
        // One file per book: an earlier view model may still hold its book open.
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_aids_test_{Guid.NewGuid():N}.epub");
        _extraEpubs.Add(path);
        EpubFixture.Create(path, firstChapterEmpty: false);
        using var context = new PaperbunkrDbContext(_dbOptions);
        var book = new Book { Title = "Test", FilePath = path, Format = BookFormat.Epub, AddedTime = DateTime.UtcNow, LanguageISO = "fr" };
        configure?.Invoke(book);
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private BookReaderScreenViewModel Open(int bookId)
    {
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        var vm = new BookReaderScreenViewModel(() => { });
        vm.LoadBook(bookId);
        vm.UpdateViewportSize(new Size(700, 800));
        return vm;
    }

    private AppSettings Settings()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        return context.GetOrCreateAppSettings();
    }

    [Fact]
    public void NewColumns_DefaultToOffAndSpeedThree()
    {
        var settings = Settings();

        Assert.False(settings.BookReaderJustify);
        Assert.False(settings.BookReaderHyphenate);
        Assert.False(settings.BookReaderUsePublisherStyles);
        Assert.Null(settings.BookReaderCustomFont);
        Assert.False(settings.BookReaderRulerEnabled);
        Assert.Equal(3, settings.BookReaderAutoScrollSpeed);
    }

    [Fact]
    public void JustifyAndHyphenate_FollowTheGlobalDefaultUnlessTheBookOverrides()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var settings = context.GetOrCreateAppSettings();
            settings.BookReaderJustify = true;
            settings.BookReaderHyphenate = true;
            context.SaveChanges();
        }

        int plain = AddBook();
        int overridden = AddBook(b => { b.JustifyOverride = false; });

        var first = Open(plain);
        Assert.True(first.Settings.Justify);
        Assert.True(first.Settings.Hyphenate);

        var second = Open(overridden);
        Assert.False(second.Settings.Justify);
        Assert.True(second.Settings.Hyphenate);
    }

    [Fact]
    public void BookLanguage_ComesFromTheBook() =>
        Assert.Equal("fr", Open(AddBook()).BookLanguage);

    [Fact]
    public void ClosingTheSheet_SavesJustifyPerBookAndPublisherStylesGlobally()
    {
        int bookId = AddBook();
        var vm = Open(bookId);

        vm.OpenFontSheetCommand.Execute(null);
        vm.Settings.Justify = true;
        vm.Settings.UsePublisherStyles = true;
        vm.Settings.CustomFont = "Bookerly";
        vm.CloseFontSheetCommand.Execute(null);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.True(context.Books.Single(b => b.Id == bookId).JustifyOverride);
        var app = context.GetOrCreateAppSettings();
        Assert.True(app.BookReaderUsePublisherStyles);
        Assert.Equal("Bookerly", app.BookReaderCustomFont);
    }

    [Fact]
    public void ChoosingAnInstalledFont_SwitchesTheFamilyToCustom()
    {
        var vm = Open(AddBook());

        vm.Settings.CustomFont = "Bookerly";

        Assert.Equal(BookFontFamilyOption.Custom, vm.Settings.FontFamilyOption);
    }

    [Fact]
    public void Ruler_StateIsRememberedGlobally()
    {
        var vm = Open(AddBook());
        Assert.False(vm.IsRulerOn);

        vm.ToggleRulerCommand.Execute(null);

        Assert.True(Settings().BookReaderRulerEnabled);
        Assert.True(Open(AddBook()).IsRulerOn);
    }

    [Fact]
    public void RulerLineKeys_DoNothingWhileTheRulerIsOff()
    {
        var vm = Open(AddBook());
        int moves = 0;
        vm.RulerMoveRequested += _ => moves++;

        vm.RulerLineDownCommand.Execute(null);
        vm.ToggleRulerCommand.Execute(null);
        vm.RulerLineDownCommand.Execute(null);
        vm.RulerLineUpCommand.Execute(null);

        Assert.Equal(2, moves);
    }

    [Fact]
    public void AutoScroll_AlwaysStartsOffButSpeedIsRemembered()
    {
        var vm = Open(AddBook());
        vm.Layout = BookReaderLayout.Scroll; // auto-scroll is a scroll-mode aid
        vm.ToggleAutoScrollCommand.Execute(null);
        vm.AutoScrollFasterCommand.Execute(null);
        Assert.True(vm.IsAutoScrolling);
        Assert.Equal(4, vm.AutoScrollSpeed);

        var reopened = Open(AddBook());

        Assert.False(reopened.IsAutoScrolling);
        Assert.Equal(4, reopened.AutoScrollSpeed);
    }

    [Fact]
    public void AutoScrollSpeed_StaysBetweenOneAndEight()
    {
        var vm = Open(AddBook());

        for (int i = 0; i < 20; i++) vm.AutoScrollFasterCommand.Execute(null);
        Assert.Equal(8, vm.AutoScrollSpeed);

        for (int i = 0; i < 20; i++) vm.AutoScrollSlowerCommand.Execute(null);
        Assert.Equal(1, vm.AutoScrollSpeed);
    }

    [Fact]
    public void PageStoppingAutoScroll_UpdatesTheViewModel()
    {
        var vm = Open(AddBook());
        vm.ToggleAutoScrollCommand.Execute(null);

        vm.OnAutoScrollStoppedByPage();

        Assert.False(vm.IsAutoScrolling);
    }

    [Fact]
    public void AutoScrollPositionReports_DoNotCountAsInput()
    {
        var clock = new[] { new DateTime(2026, 10, 27, 12, 0, 0) };
        var tracker = new ReaderActivityTracker(() => clock[0], useTimer: false);
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        var vm = new BookReaderScreenViewModel(() => { }) { };
        vm._activity = tracker;
        vm.LoadBook(AddBook());
        vm.UpdateViewportSize(new Size(700, 800));
        vm.ToggleAutoScrollCommand.Execute(null);

        clock[0] += TimeSpan.FromMinutes(3);
        tracker.Tick();
        vm.OnPositionCaptured("pb-p5", 0.5, "x"); // a scroll the page made by itself
        clock[0] += TimeSpan.FromMinutes(3);

        // Nothing the reader did after the speed key press, so the idle cut-off ended the credit well short of six minutes.
        Assert.True(tracker.Stop() < 6 * 60);
    }

    [Fact]
    public void ReadingAidActions_AreInTheBookReaderGroupWithoutBindingConflicts()
    {
        string[] ids =
        [
            InputActionIds.BookRulerToggle, InputActionIds.BookRulerLineUp, InputActionIds.BookRulerLineDown,
            InputActionIds.BookAutoScrollToggle, InputActionIds.BookAutoScrollFaster, InputActionIds.BookAutoScrollSlower,
        ];
        var bookReader = InputActions.Core.Where(a => a.Scope == InputScope.BookReader).ToList();

        foreach (var id in ids)
        {
            Assert.Equal(InputActions.BookReaderGroup, InputActions.Core.Single(a => a.Id == id).Group);
        }

        var bindings = bookReader.SelectMany(a => a.Defaults.Select(b => (a.Id, b))).ToList();
        Assert.Equal(bindings.Count, bindings.Select(x => x.b).Distinct().Count());
    }
}
