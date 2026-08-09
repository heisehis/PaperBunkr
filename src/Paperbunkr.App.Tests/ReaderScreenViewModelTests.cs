using Avalonia.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ReaderScreenViewModel"/>'s <c>OpenLastPage</c>/<c>AutoNavigateComics</c>
/// behavior (docs/superpowers/specs/2026-08-07-preferences-behavior-tab-design.md §3). Redirects
/// <see cref="PaperbunkrDbContext.DatabasePathOverride"/> to a temp SQLite file for the whole test
/// - unlike <see cref="SkinService"/>/<c>CoverThumbnailService</c>, none of the App-side
/// ViewModels have an injected context-factory seam, so this is the smallest way to keep
/// <c>PaperbunkrDb.CreateContext()</c> off the real per-user database. Runs under
/// <see cref="AvaloniaTestCollection"/> since page decode needs a real Skia platform.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReaderScreenViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly string _issue1Path;
    private readonly string _issue2Path;
    private readonly int _seriesId;
    private readonly int _issue1Id;
    private readonly int _issue2Id;

    public ReaderScreenViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_vm_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        _issue1Path = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_vm_issue1_{Guid.NewGuid():N}.cbz");
        _issue2Path = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_vm_issue2_{Guid.NewGuid():N}.cbz");
        CbzFixture.Create(_issue1Path, pageCount: 3);
        CbzFixture.Create(_issue2Path, pageCount: 2);

        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();

        var series = new Series { Name = "Test Series" };
        context.Series.Add(series);
        context.SaveChanges();
        _seriesId = series.Id;

        var issue1 = new Issue { SeriesId = series.Id, Number = "1", FilePath = _issue1Path };
        var issue2 = new Issue { SeriesId = series.Id, Number = "2", FilePath = _issue2Path };
        context.Issues.AddRange(issue1, issue2);
        context.SaveChanges();
        _issue1Id = issue1.Id;
        _issue2Id = issue2.Id;
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_issue1Path)) File.Delete(_issue1Path);
            if (File.Exists(_issue2Path)) File.Delete(_issue2Path);
        }
        catch (IOException)
        {
        }
    }

    private static void SetAutoNavigateComics(bool value)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.GetOrCreateAppSettings().AutoNavigateComics = value;
        context.SaveChanges();
    }

    private static void SetOpenLastPage(bool value)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.GetOrCreateAppSettings().OpenLastPage = value;
        context.SaveChanges();
    }

    private static void SetReverseRtlNavigation(bool value)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.GetOrCreateAppSettings().ReverseRtlNavigation = value;
        context.SaveChanges();
    }

    private static void SetHighQualityPageDisplay(bool value)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.GetOrCreateAppSettings().HighQualityPageDisplay = value;
        context.SaveChanges();
    }

    private static void SetPageTurnLeftKey(Key key) =>
        new KeyBindingService().SetKey(KeyboardCommandRegistry.ReaderPageTurnLeft, key);

    private void SetSeriesReadingMode(ReadingMode mode)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.Series.First(s => s.Id == _seriesId).ReadingMode = mode;
        context.SaveChanges();
    }

    [Fact]
    public void NextPage_PastLastPage_LoadsNextIssue_WhenAutoNavigateEnabled()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);
        Assert.Equal("PAGE 3 / 3", vm.PageLabel);

        vm.NextPageCommand.Execute(null);

        Assert.Equal("PAGE 1 / 2", vm.PageLabel);
        Assert.Contains("#2", vm.IssueTitle);
    }

    [Fact]
    public void NextPage_AtEndOfSeries_NoOps()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue2Id);
        vm.NextPageCommand.Execute(null);
        Assert.Equal("PAGE 2 / 2", vm.PageLabel);

        vm.NextPageCommand.Execute(null);

        Assert.Equal("PAGE 2 / 2", vm.PageLabel);
        Assert.Contains("#2", vm.IssueTitle);
    }

    [Fact]
    public void NextPage_PastLastPage_DoesNotCrossIssues_WhenAutoNavigateDisabled()
    {
        SetAutoNavigateComics(false);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);
        Assert.Equal("PAGE 3 / 3", vm.PageLabel);

        vm.NextPageCommand.Execute(null);

        Assert.Equal("PAGE 3 / 3", vm.PageLabel);
        Assert.Contains("#1", vm.IssueTitle);
    }

    [Fact]
    public void PreviousPage_BeforeFirstPage_LoadsPreviousIssueAtItsLastPage()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue2Id);

        vm.PreviousPageCommand.Execute(null);

        Assert.Equal("PAGE 3 / 3", vm.PageLabel);
        Assert.Contains("#1", vm.IssueTitle);
    }

    [Fact]
    public void OpenLastPage_False_StartsAtFirstPage_RegardlessOfSavedProgress()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Issues.First(i => i.Id == _issue1Id).LastPageRead = 2;
            context.SaveChanges();
        }

        SetOpenLastPage(false);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        Assert.Equal("PAGE 1 / 3", vm.PageLabel);
    }

    [Fact]
    public void OpenLastPage_True_ResumesAtSavedProgress()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Issues.First(i => i.Id == _issue1Id).LastPageRead = 2;
            context.SaveChanges();
        }

        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        Assert.Equal("PAGE 3 / 3", vm.PageLabel);
    }

    [Fact]
    public void GoLeftGoRight_LeftToRight_MatchPreviousNext()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.GoRightCommand.Execute(null);
        Assert.Equal("PAGE 2 / 3", vm.PageLabel);

        vm.GoLeftCommand.Execute(null);
        Assert.Equal("PAGE 1 / 3", vm.PageLabel);
    }

    [Fact]
    public void GoLeftGoRight_RightToLeft_AreFlipped()
    {
        SetSeriesReadingMode(ReadingMode.RightToLeft);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.GoLeftCommand.Execute(null);
        Assert.Equal("PAGE 2 / 3", vm.PageLabel);

        vm.GoRightCommand.Execute(null);
        Assert.Equal("PAGE 1 / 3", vm.PageLabel);
    }

    [Fact]
    public void GoLeftGoRight_RightToLeft_NotFlipped_WhenReverseRtlNavigationDisabled()
    {
        SetSeriesReadingMode(ReadingMode.RightToLeft);
        SetReverseRtlNavigation(false);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.GoRightCommand.Execute(null);
        Assert.Equal("PAGE 2 / 3", vm.PageLabel);

        vm.GoLeftCommand.Execute(null);
        Assert.Equal("PAGE 1 / 3", vm.PageLabel);
    }

    [Fact]
    public void HighQualityPageDisplay_DefaultsTrue_AndReflectsAppSettingsOnLoad()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        Assert.True(vm.HighQualityPageDisplay);

        SetHighQualityPageDisplay(false);
        vm.LoadIssue(_issue1Id);

        Assert.False(vm.HighQualityPageDisplay);
    }

    [Fact]
    public void PageTurnKeys_DefaultToArrowKeys_AndReflectRemappingOnLoad()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        Assert.Equal(Key.Left, vm.PageTurnLeftKey);
        Assert.Equal(Key.Right, vm.PageTurnRightKey);

        SetPageTurnLeftKey(Key.J);
        vm.LoadIssue(_issue1Id);

        Assert.Equal(Key.J, vm.PageTurnLeftKey);
        Assert.Equal(Key.Right, vm.PageTurnRightKey); // untouched
    }

    [Fact]
    public void GoLeft_RightToLeft_AtEndOfIssue_CrossesToNextIssue_ThroughFlippedCommand()
    {
        SetSeriesReadingMode(ReadingMode.RightToLeft);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.GoLeftCommand.Execute(null);
        vm.GoLeftCommand.Execute(null);
        Assert.Equal("PAGE 3 / 3", vm.PageLabel);

        vm.GoLeftCommand.Execute(null);

        Assert.Equal("PAGE 1 / 2", vm.PageLabel);
        Assert.Contains("#2", vm.IssueTitle);
    }

    [Fact]
    public void ZoomLevel_DefaultsTo1()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        Assert.Equal(1.0, vm.ZoomLevel);
    }

    [Fact]
    public void ZoomLevel_ClampsAboveMax_To4()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.ZoomLevel = 10;

        Assert.Equal(4.0, vm.ZoomLevel);
    }

    [Fact]
    public void ZoomLevel_ClampsBelowMin_To1()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.ZoomLevel = 0.2;

        Assert.Equal(1.0, vm.ZoomLevel);
    }

    [Fact]
    public void ZoomLevel_SetToMin_ResetsPanOffsetToZeroZero()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.ZoomLevel = 2.5;
        vm.PanOffsetX = 40;
        vm.PanOffsetY = -10;

        vm.ZoomLevel = 1.0;

        Assert.Equal(0, vm.PanOffsetX);
        Assert.Equal(0, vm.PanOffsetY);
    }

    [Fact]
    public void PanOffset_DefaultsToZeroZero()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        Assert.Equal(0, vm.PanOffsetX);
        Assert.Equal(0, vm.PanOffsetY);
    }

    [Fact]
    public void Load_ResetsZoomAndPan_OnReopeningAnAlreadyZoomedIssue()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.ZoomLevel = 2.5;
        vm.PanOffsetX = 40;
        vm.PanOffsetY = -10;

        vm.LoadIssue(_issue1Id);

        Assert.Equal(1.0, vm.ZoomLevel);
        Assert.Equal(0, vm.PanOffsetX);
        Assert.Equal(0, vm.PanOffsetY);
    }

    [Fact]
    public void NextPage_AcrossIssueBoundary_ResetsZoomAndPan()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);
        Assert.Equal("PAGE 3 / 3", vm.PageLabel);
        vm.ZoomLevel = 2.5;
        vm.PanOffsetX = 40;
        vm.PanOffsetY = -10;

        vm.NextPageCommand.Execute(null);

        Assert.Contains("#2", vm.IssueTitle);
        Assert.Equal(1.0, vm.ZoomLevel);
        Assert.Equal(0, vm.PanOffsetX);
        Assert.Equal(0, vm.PanOffsetY);
    }
}
