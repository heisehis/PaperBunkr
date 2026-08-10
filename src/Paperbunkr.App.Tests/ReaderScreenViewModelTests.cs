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

    private static void SetResetZoomOnPageChange(bool value)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.GetOrCreateAppSettings().ResetZoomOnPageChange = value;
        context.SaveChanges();
    }

    private static void SetMouseWheelSpeed(double value)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.GetOrCreateAppSettings().MouseWheelSpeed = value;
        context.SaveChanges();
    }

    private static void SetDefaultPageFitMode(ImageFitMode value)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.GetOrCreateAppSettings().DefaultPageFitMode = value;
        context.SaveChanges();
    }

    private static void SetDefaultAutoRotate(bool value)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.GetOrCreateAppSettings().DefaultAutoRotate = value;
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

    /// <summary>
    /// docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md
    /// §5 - "zoom is free and unclamped upward" in continuous mode, unlike paged mode's fixed 4.0
    /// ceiling above.
    /// </summary>
    [Fact]
    public void ZoomLevel_InContinuousMode_ClampsAt4_SameCeilingAsPagedMode()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);

        vm.ZoomLevel = 10;

        Assert.Equal(4.0, vm.ZoomLevel);
    }

    /// <summary>User direction after initial testing: continuous/webtoon zoom is a bounded 0.5x-4x range (matching the toolbar slider), not paged mode's 1x-4x - supersedes the design spec's originally-scoped "unclamped upward."</summary>
    [Fact]
    public void ZoomLevel_InContinuousMode_AllowsZoomingOutBelowPagedFloor()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);

        vm.ZoomLevel = 0.5;

        Assert.Equal(0.5, vm.ZoomLevel);
    }

    [Fact]
    public void ZoomLevel_InContinuousMode_ClampsBelow0Point5To0Point5()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);

        vm.ZoomLevel = 0.1;

        Assert.Equal(0.5, vm.ZoomLevel);
    }

    [Fact]
    public void ZoomLevel_ReClampsToPagedFloor_WhenSwitchingBackFromContinuousMode()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);
        vm.ZoomLevel = 0.5;
        Assert.Equal(0.5, vm.ZoomLevel);

        vm.SetReadingModeCommand.Execute(ReadingMode.LeftToRight);
        vm.ZoomLevel = 0.5; // setter re-evaluates the now-paged floor (1.0, not 0.5)

        Assert.Equal(1.0, vm.ZoomLevel);
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

    [Fact]
    public void ToggleReadingModeCommand_FlipsSeriesReadingMode_AndUpdatesLabel()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        Assert.Equal("Left to Right ▾", vm.ReadingModeLabel);

        vm.ToggleReadingModeCommand.Execute(null);
        Assert.Equal("Right to Left ▾", vm.ReadingModeLabel);

        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal(ReadingMode.RightToLeft, context.Series.First(s => s.Id == _seriesId).ReadingMode);
        }

        vm.ToggleReadingModeCommand.Execute(null);
        Assert.Equal("Left to Right ▾", vm.ReadingModeLabel);
    }

    /// <summary>
    /// docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md
    /// §4 - continuous mode needs a real way in, since <see cref="ReaderScreenViewModel.ToggleReadingModeCommand"/>
    /// only ever flips between LeftToRight/RightToLeft.
    /// </summary>
    [Fact]
    public void SetReadingModeCommand_VerticalContinuous_UpdatesLabelAndIsContinuousMode()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        Assert.False(vm.IsContinuousMode);

        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);

        Assert.Equal("Vertical (Continuous) ▾", vm.ReadingModeLabel);
        Assert.True(vm.IsContinuousMode);
        Assert.Equal(ReadingMode.VerticalContinuous, vm.EffectiveReadingMode);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(ReadingMode.VerticalContinuous, context.Series.First(s => s.Id == _seriesId).ReadingMode);
    }

    [Fact]
    public void SetReadingModeCommand_HorizontalContinuous_UpdatesLabelAndIsContinuousMode()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.SetReadingModeCommand.Execute(ReadingMode.HorizontalContinuous);

        Assert.Equal("Horizontal (Continuous) ▾", vm.ReadingModeLabel);
        Assert.True(vm.IsContinuousMode);
    }

    /// <summary>User direction added after initial testing - a real horizontal RTL mode, not just LTR.</summary>
    [Fact]
    public void SetReadingModeCommand_HorizontalContinuousRightToLeft_UpdatesLabelAndIsContinuousMode()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.SetReadingModeCommand.Execute(ReadingMode.HorizontalContinuousRightToLeft);

        Assert.Equal("Horizontal RTL (Continuous) ▾", vm.ReadingModeLabel);
        Assert.True(vm.IsContinuousMode);
        Assert.Equal(ReadingMode.HorizontalContinuousRightToLeft, vm.EffectiveReadingMode);
    }

    /// <summary>User direction added after initial testing - Webtoon (merged, no gap) as a mode distinct from VerticalContinuous (gapped).</summary>
    [Fact]
    public void SetReadingModeCommand_Webtoon_UpdatesLabelAndIsContinuousMode()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.SetReadingModeCommand.Execute(ReadingMode.Webtoon);

        Assert.Equal("Webtoon ▾", vm.ReadingModeLabel);
        Assert.True(vm.IsContinuousMode);
        Assert.NotNull(vm.Decoder); // opens the continuous-aware decoder same as the other continuous modes
    }

    [Fact]
    public void IsContinuousMode_ReturnsToFalse_WhenSwitchedBackToPagedMode()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);
        Assert.True(vm.IsContinuousMode);

        vm.SetReadingModeCommand.Execute(ReadingMode.LeftToRight);

        Assert.False(vm.IsContinuousMode);
    }

    /// <summary>Reopening the issue picks the continuous decoder path in <see cref="ReaderScreenViewModel.LoadIssue"/> - confirms <see cref="ReaderScreenViewModel.Decoder"/> is non-null and page-count-correct via that path too, not just PageImageDecoder's.</summary>
    [Fact]
    public void LoadIssue_InContinuousMode_OpensDecoderSuccessfully_AndPopulatesPageCount()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Series.First(s => s.Id == _seriesId).ReadingMode = ReadingMode.VerticalContinuous;
            context.SaveChanges();
        }

        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        Assert.True(vm.IsContinuousMode);
        Assert.NotNull(vm.Decoder);
        Assert.Equal(3, vm.PageCount);
        Assert.False(vm.HasError);
    }

    [Fact]
    public void ToggleReadingModeCommand_AlsoFlipsSpatialGoLeftGoRight()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.ToggleReadingModeCommand.Execute(null); // now Right to Left

        vm.GoLeftCommand.Execute(null);
        Assert.Equal("PAGE 2 / 3", vm.PageLabel); // spatial Left now advances, matching GoLeftGoRight_RightToLeft_AreFlipped
    }

    [Fact]
    public void SelectThumbnailCommand_JumpsToClickedPage()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        Assert.Equal("PAGE 1 / 3", vm.PageLabel);

        vm.SelectThumbnailCommand.Execute(vm.Thumbnails[2]);

        Assert.Equal("PAGE 3 / 3", vm.PageLabel);
    }

    [Fact]
    public void SelectThumbnailCommand_StaleThumbnail_NoOps()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        var staleThumbnail = new ReaderThumbnailSample { CoverBrush = vm.CoverBrush };

        vm.SelectThumbnailCommand.Execute(staleThumbnail);

        Assert.Equal("PAGE 1 / 3", vm.PageLabel);
    }

    /// <summary>
    /// Regression test for a real bug: StartThumbnailGeneration's background loop declared its
    /// page index in the `for` header, and every Dispatcher.UIThread.Post closure captured that
    /// SAME shared variable by reference instead of a per-iteration snapshot. The background loop
    /// races far ahead of the UI thread draining its dispatcher queue (decoding a tiny thumbnail
    /// takes microseconds), so by the time a queued closure actually ran, the shared index had
    /// already advanced past whatever page it was meant to write - leaving early pages (page 0
    /// especially, since every later iteration raced past it before its own Post got a turn)
    /// permanently null.
    ///
    /// Verifying this needs Dispatcher.UIThread.RunJobs() to drain the queued closures (headless
    /// tests have no running dispatcher loop), which only works called from the exact OS thread
    /// that bootstrapped the platform (TestAppBuilder.EnsureInitialized) - true and reliable when
    /// this file's tests run in isolation, but xUnit doesn't guarantee that same thread when the
    /// full assembly runs (CheckAccess() comes back false; the documented Invoke() fallback hangs,
    /// since headless mode has nothing looping to service a cross-thread marshal). Rather than
    /// block the whole suite on that unrelated xUnit/Avalonia-headless scheduling gap, this no-ops
    /// when it can't get real access instead of hanging or spuriously failing - it still runs for
    /// real (and did catch this exact bug pre-fix) via `dotnet test --filter Name=<this test>`.
    /// </summary>
    // Fit mode / auto-rotate persistence (docs/superpowers/specs/2026-08-10-reader-polish-core-
    // viewing-controls-design.md §3) - global default + per-Issue override, mirroring
    // Issue.ReadingModeOverride's shape but written from the reader toolbar itself.
    // Preferences Reader tab additions (docs/superpowers/specs/2026-08-10-preferences-reader-tab-design.md)
    [Fact]
    public void GoToPage_ResetZoomOnPageChangeEnabled_ResetsZoomToOne()
    {
        SetResetZoomOnPageChange(true);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.ZoomLevel = 2.5;

        vm.NextPageCommand.Execute(null);

        Assert.Equal(1.0, vm.ZoomLevel);
    }

    [Fact]
    public void GoToPage_ResetZoomOnPageChangeDisabled_LeavesZoomAlone()
    {
        // Default (false) - matches Paperbunkr's pre-existing behavior, unchanged by this setting's addition.
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.ZoomLevel = 2.5;

        vm.NextPageCommand.Execute(null);

        Assert.Equal(2.5, vm.ZoomLevel);
    }

    [Fact]
    public void MouseWheelSpeed_DefaultsTo2_AndReflectsAppSettingsOnLoad()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        Assert.Equal(2.0, vm.MouseWheelSpeed);

        SetMouseWheelSpeed(4.5);
        vm.LoadIssue(_issue1Id);

        Assert.Equal(4.5, vm.MouseWheelSpeed);
    }

    [Fact]
    public void FitMode_ReflectsAppSettingsDefault_ForAnIssueWithNoOverride()
    {
        SetDefaultPageFitMode(ImageFitMode.BestFit);
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.LoadIssue(_issue1Id);

        Assert.Equal(ImageFitMode.BestFit, vm.FitMode);
    }

    [Fact]
    public void AutoRotate_ReflectsAppSettingsDefault_ForAnIssueWithNoOverride()
    {
        SetDefaultAutoRotate(true);
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.LoadIssue(_issue1Id);

        Assert.True(vm.AutoRotate);
    }

    [Fact]
    public void FitMode_DefaultsToFitWidth_ForAnIssueWithNoOverride()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        Assert.Equal(ImageFitMode.FitWidth, vm.FitMode);
    }

    [Fact]
    public void SetFitModeCommand_PersistsPerIssueOverride_ReadBackOnNextLoad()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.SetFitModeCommand.Execute(ImageFitMode.BestFit);

        Assert.Equal(ImageFitMode.BestFit, vm.FitMode);
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal(ImageFitMode.BestFit, context.Issues.First(i => i.Id == _issue1Id).PageFitModeOverride);
        }

        var reopened = new ReaderScreenViewModel(goBack: () => { });
        reopened.LoadIssue(_issue1Id);
        Assert.Equal(ImageFitMode.BestFit, reopened.FitMode);
    }

    [Fact]
    public void SetFitModeCommand_OnOneIssue_DoesNotAffectAnother()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetFitModeCommand.Execute(ImageFitMode.Original);

        vm.LoadIssue(_issue2Id);

        Assert.Equal(ImageFitMode.FitWidth, vm.FitMode); // issue2's own default, untouched
    }

    [Fact]
    public void AutoRotate_DefaultsToFalse_AndToggleCommandPersistsPerIssueOverride()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        Assert.False(vm.AutoRotate);

        vm.ToggleAutoRotateCommand.Execute(null);

        Assert.True(vm.AutoRotate);
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.True(context.Issues.First(i => i.Id == _issue1Id).AutoRotateOverride);
        }

        var reopened = new ReaderScreenViewModel(goBack: () => { });
        reopened.LoadIssue(_issue1Id);
        Assert.True(reopened.AutoRotate);

        vm.ToggleAutoRotateCommand.Execute(null);
        Assert.False(vm.AutoRotate);
    }

    [Fact]
    public void ManualRotationDegrees_DefaultsToZero_AndRotateClockwiseStepsBy90AndWraps()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        Assert.Equal(0, vm.ManualRotationDegrees);

        vm.RotateClockwiseCommand.Execute(null);
        vm.RotateClockwiseCommand.Execute(null);
        vm.RotateClockwiseCommand.Execute(null);
        vm.RotateClockwiseCommand.Execute(null);

        Assert.Equal(0, vm.ManualRotationDegrees); // four 90-degree steps wrap back to 0
    }

    [Fact]
    public void ManualRotationDegrees_IsSessionOnly_ResetsOnReopen_NeverPersistedToIssue()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.RotateClockwiseCommand.Execute(null);
        Assert.Equal(90, vm.ManualRotationDegrees);

        vm.LoadIssue(_issue1Id);

        Assert.Equal(0, vm.ManualRotationDegrees);
    }

    [Fact]
    public void ZoomPresetCommands_SetExpectedZoomLevels()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.SetZoom150Command.Execute(null);
        Assert.Equal(1.5, vm.ZoomLevel);

        vm.SetZoom400Command.Execute(null);
        Assert.Equal(4.0, vm.ZoomLevel);

        vm.SetZoom100Command.Execute(null);
        Assert.Equal(1.0, vm.ZoomLevel);
    }

    [Fact]
    public void ZoomInZoomOutCommands_StepAndClamp()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.ZoomInCommand.Execute(null);
        Assert.Equal(1.25, vm.ZoomLevel);

        vm.ZoomOutCommand.Execute(null);
        vm.ZoomOutCommand.Execute(null);
        Assert.Equal(1.0, vm.ZoomLevel); // clamped at MinZoom, doesn't go below
    }

    [Fact]
    public void LoadIssue_GeneratesThumbnailsForEveryPage_NoneLeftNull()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        Assert.Equal(3, vm.Thumbnails.Count);

        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            return;
        }

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.Thumbnails.Any(t => t.CoverImage is null) && DateTime.UtcNow < deadline)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.All(vm.Thumbnails, t => Assert.NotNull(t.CoverImage));
    }
}
