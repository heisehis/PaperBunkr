using Avalonia.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Reading units, paged/scroll layout, the footer and Go to page (docs/superpowers/specs/2026-10-27-books-reader-paged-scroll-modes-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookReaderLayoutTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_layout_{Guid.NewGuid():N}.db");
    private readonly string _epub = Path.Combine(Path.GetTempPath(), $"paperbunkr_layout_{Guid.NewGuid():N}.epub");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public BookReaderLayoutTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        PaperbunkrDbContext.DatabasePathOverride = null;
        try
        {
            File.Delete(_dbPath);
            File.Delete(_epub);
        }
        catch (IOException)
        {
        }
    }

    // The fixture's second file packs two contents entries ("The End", "Epilogue"), so the book has three units in two files.
    private BookReaderScreenViewModel Open(string? printPageList = null)
    {
        EpubFixture.Create(_epub, packedChapter2: true, printPageList: printPageList);
        using (var context = new PaperbunkrDbContext(_options))
        {
            context.Books.Add(new Book { Title = "T", FilePath = _epub, Format = BookFormat.Epub, AddedTime = DateTime.UtcNow });
            context.SaveChanges();
        }

        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        var vm = new BookReaderScreenViewModel(() => { });
        vm.LoadBook(1);
        vm.UpdateViewportSize(new Avalonia.Size(700, 800));
        return vm;
    }

    private static void WaitForPageMap(BookReaderScreenViewModel vm)
    {
        vm.PageMapReady.Wait(TimeSpan.FromSeconds(10));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void APackedFileIsShownOneContentsEntryAtATime()
    {
        var vm = Open();

        Assert.Equal(3, vm.UnitCount);
        vm.NextPageCommand.Execute(null);
        Assert.Equal(1, vm.CurrentUnitIndex);
        Assert.Contains("And so it ended", vm.CurrentChapterHtml);
        Assert.DoesNotContain("Years later", vm.CurrentChapterHtml);

        vm.NextPageCommand.Execute(null);
        Assert.Equal(2, vm.CurrentUnitIndex);
        Assert.Equal("Epilogue", vm.ChapterTitle);
        Assert.Contains("Years later", vm.CurrentChapterHtml);
        Assert.DoesNotContain("And so it ended", vm.CurrentChapterHtml);
    }

    [Fact]
    public void BackIntoThePreviousUnitLandsOnItsLastPage()
    {
        var vm = Open();
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);
        string? restored = null;
        vm.PositionRestoreRequested += b => restored = b;

        vm.PreviousPageCommand.Execute(null);

        Assert.Equal(1, vm.CurrentUnitIndex);
        Assert.Equal(BookReaderScreenViewModel.LastPageAnchor, restored);
    }

    [Fact]
    public void TurningPastTheLastUnitFinishesTheBook()
    {
        var vm = Open();
        for (int i = 0; i < 3; i++) vm.NextPageCommand.Execute(null);

        using var context = new PaperbunkrDbContext(_options);
        Assert.Equal(BookReadingStatus.Finished, context.Books.Single().Status);
    }

    [Fact]
    public void AContentsRowOpensItsUnitAndIsMarkedActive()
    {
        var vm = Open();

        vm.GoToChapterCommand.Execute(vm.TableOfContents.Single(r => r.Title == "Epilogue"));

        Assert.Equal(2, vm.CurrentUnitIndex);
        Assert.True(vm.TableOfContents.Single(r => r.Title == "Epilogue").IsActive);
    }

    [Fact]
    public void LayoutIsRememberedAndPagedRefusesAutoScroll()
    {
        var vm = Open();
        Assert.True(vm.IsPaged); // the default

        vm.ToggleAutoScrollCommand.Execute(null);
        vm.IsAutoScrolling = true; // the font sheet's switch
        Assert.False(vm.IsAutoScrolling);

        vm.SetLayoutCommand.Execute(BookReaderLayout.Scroll);
        using (var context = new PaperbunkrDbContext(_options))
        {
            Assert.Equal(BookReaderLayout.Scroll, context.GetOrCreateAppSettings().BookReaderLayout);
        }

        vm.ToggleAutoScrollCommand.Execute(null);
        Assert.True(vm.IsAutoScrolling);
        vm.Layout = BookReaderLayout.Paged;
        Assert.False(vm.IsAutoScrolling); // switching to paged stops it
    }

    [Fact]
    public void FooterShowsTheBookPageTheUnitAndTheScreenPageOrPercent()
    {
        var vm = Open();
        WaitForPageMap(vm);

        vm.OnScreenPage(1, 4);
        Assert.Equal("p. 1 of 3 · The Beginning · 2 / 4", vm.ProgressLabel);

        vm.Layout = BookReaderLayout.Scroll;
        vm.ProgressPercent = 37;
        Assert.Equal("p. 1 of 3 · The Beginning · 37%", vm.ProgressLabel);

        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);
        Assert.StartsWith("p. 3 of 3 · Epilogue", vm.ProgressLabel);
        Assert.Equal(1.0, vm.ProgressFraction, 3);
    }

    [Fact]
    public void GoToPage_JumpsToAWordCountPageAndRejectsAnUnknownOne()
    {
        var vm = Open();
        WaitForPageMap(vm);

        vm.GoToPageText = "3";
        vm.GoToPageCommand.Execute(null);
        Assert.Equal(2, vm.CurrentUnitIndex);
        Assert.Equal(string.Empty, vm.GoToPageError);

        vm.GoToPageText = "9";
        vm.GoToPageCommand.Execute(null);
        Assert.Equal("No page 9", vm.GoToPageError);
    }

    [Fact]
    public void ABookWithAPrintPageListShowsAndJumpsToPrintPages()
    {
        var vm = Open(printPageList: "nav");
        WaitForPageMap(vm);

        Assert.StartsWith("p. 1 of 2", vm.ProgressLabel);
        Assert.Equal("of 2", vm.GoToPageTotalText);

        vm.GoToPageText = "2";
        vm.GoToPageCommand.Execute(null);

        Assert.Equal(1, vm.CurrentUnitIndex); // page 2's marker sits in "The End", before the Epilogue
        Assert.StartsWith("p. 2 of 2", vm.ProgressLabel);
    }
}
