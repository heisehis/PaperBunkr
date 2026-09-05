using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
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
    private readonly string _issue3Path;
    private readonly string _issue4Path;
    private readonly int _seriesId;
    private readonly int _issue1Id;
    private readonly int _issue2Id;
    private readonly int _issue3Id;
    private readonly int _otherSeriesId;
    private readonly int _issue4Id;

    public ReaderScreenViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_vm_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        _issue1Path = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_vm_issue1_{Guid.NewGuid():N}.cbz");
        _issue2Path = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_vm_issue2_{Guid.NewGuid():N}.cbz");
        _issue3Path = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_vm_issue3_{Guid.NewGuid():N}.cbz");
        _issue4Path = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_vm_issue4_{Guid.NewGuid():N}.cbz");
        CbzFixture.Create(_issue1Path, pageCount: 3);
        CbzFixture.Create(_issue2Path, pageCount: 2);
        CbzFixture.Create(_issue4Path, pageCount: 1); // a different series (docs/superpowers/specs/2026-08-23-cbl-manager-manual-editing-and-list-aware-reading-design.md §3) - proves list-order navigation actually crosses series, not just re-derives series order

        // Double-page spread fixture (docs/superpowers/specs/2026-08-15-reader-double-page-spread-
        // design.md §7): index 0 cover (type irrelevant, always solo), 1+2 both portrait (pairs), 3
        // landscape (breaks pairing on both sides), 4+5 both portrait (pairs again).
        CbzFixture.Create(_issue3Path, pageCount: 6, pageSize: i => i == 3 ? new System.Drawing.Size(96, 64) : new System.Drawing.Size(64, 96));

        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();

        var series = new Series { Name = "Test Series" };
        context.Series.Add(series);
        context.SaveChanges();
        _seriesId = series.Id;

        var issue1 = new Issue { SeriesId = series.Id, Number = "1", FilePath = _issue1Path };
        var issue2 = new Issue { SeriesId = series.Id, Number = "2", FilePath = _issue2Path };
        // Number "0" - sorts before issue1/issue2 (OrderByNumber), so existing series-boundary tests
        // that depend on issue2 being the *last* issue (e.g. NextPage_AtEndOfSeries_NoOps) still hold.
        var issue3 = new Issue { SeriesId = series.Id, Number = "0", FilePath = _issue3Path };
        context.Issues.AddRange(issue1, issue2, issue3);
        context.SaveChanges();
        _issue1Id = issue1.Id;
        _issue2Id = issue2.Id;
        _issue3Id = issue3.Id;

        var otherSeries = new Series { Name = "Other Series" };
        context.Series.Add(otherSeries);
        context.SaveChanges();
        _otherSeriesId = otherSeries.Id;

        var issue4 = new Issue { SeriesId = otherSeries.Id, Number = "1", FilePath = _issue4Path };
        context.Issues.Add(issue4);
        context.SaveChanges();
        _issue4Id = issue4.Id;
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
            if (File.Exists(_issue3Path)) File.Delete(_issue3Path);
            if (File.Exists(_issue4Path)) File.Delete(_issue4Path);
        }
        catch (IOException)
        {
        }
    }

    private static int CreateReadingList(params int[] issueIdsInOrder)
    {
        using var context = PaperbunkrDb.CreateContext();
        var list = new ReadingList { Name = "Test List", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.ReadingLists.Add(list);
        context.SaveChanges();

        for (int i = 0; i < issueIdsInOrder.Length; i++)
        {
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = list.Id, IssueId = issueIdsInOrder[i], SortOrder = i });
        }
        context.SaveChanges();
        return list.Id;
    }

    private static int CreatePlaceholderIssue()
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = "Placeholder Series" };
        context.Series.Add(series);
        context.SaveChanges();

        var issue = new Issue { SeriesId = series.Id, Number = "1", IsPlaceholder = true, FileIsMissing = true };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
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

    private static void SetPageTransitionSettings(PageTransitionStyle style, int durationMs)
    {
        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();
        settings.PageTransitionStyle = style;
        settings.PageTransitionDurationMs = durationMs;
        context.SaveChanges();
    }

    private static void SetDefaultPageLayoutMode(PageLayoutMode mode)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.GetOrCreateAppSettings().DefaultPageLayoutMode = mode;
        context.SaveChanges();
    }

    private void SetSeriesPageLayoutMode(PageLayoutMode? mode)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.Series.Find(_seriesId)!.PageLayoutMode = mode;
        context.SaveChanges();
    }

    private void SetIssuePageLayoutModeOverride(int issueId, PageLayoutMode? mode)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.Issues.Find(issueId)!.PageLayoutModeOverride = mode;
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

    private static void SetImageBackgroundMode(ImageBackgroundMode value)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.GetOrCreateAppSettings().ImageBackgroundMode = value;
        context.SaveChanges();
    }

    private static void SetBackgroundColor(string value)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.GetOrCreateAppSettings().BackgroundColor = value;
        context.SaveChanges();
    }

    private static void SetPageMargin(bool enabled, double percentWidth)
    {
        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();
        settings.PageMarginEnabled = enabled;
        settings.PageMarginPercentWidth = percentWidth;
        context.SaveChanges();
    }

    private static void SetKeyBinding(string commandId, KeyGesture gesture) =>
        new KeyBindingService().SetKey(commandId, gesture);

    private void SetSeriesReadingMode(ReadingMode mode)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.Series.First(s => s.Id == _seriesId).ReadingMode = mode;
        context.SaveChanges();
    }

    [Fact]
    public void SharedElementKey_NullBeforeAnyIssueLoaded_ThenIssueCoverAfterLoadIssue()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        Assert.Null(vm.SharedElementKey);

        vm.LoadIssue(_issue1Id);

        Assert.Equal($"issue-cover:{_issue1Id}", vm.SharedElementKey);
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
        vm.OnChapterTransitionHoldTick(null, EventArgs.Empty); // docs/superpowers/specs/2026-08-23-reader-chapter-transition-design.md - navigation is deferred behind the transition card's hold timer; advance it directly (same test seam as OnAutoScrollTick).

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

    // ===================== "Ask me to rate a comic when I finish it" (docs/superpowers/specs/
    // 2026-09-04-behavior-settings-batch2-design.md §3.3, CE AutoShowQuickReview) =====================

    private static void SetPromptReviewOnFinish(bool value)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.GetOrCreateAppSettings().PromptReviewOnFinish = value;
        context.SaveChanges();
    }

    [Fact]
    public void FinishPrompt_FiresWithIssueId_AtEndOfSeries_WhenEnabled()
    {
        SetPromptReviewOnFinish(true);
        var prompted = new List<int>();
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.ReviewPromptRequested += prompted.Add;
        vm.LoadIssue(_issue2Id); // last issue in the series (issue3 is "0", sorts first)
        vm.NextPageCommand.Execute(null);
        Assert.Equal("PAGE 2 / 2", vm.PageLabel);

        vm.NextPageCommand.Execute(null);

        Assert.Equal(new[] { _issue2Id }, prompted);
    }

    [Fact]
    public void FinishPrompt_DoesNotFire_WhenDisabled()
    {
        // PromptReviewOnFinish defaults false.
        var prompted = new List<int>();
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.ReviewPromptRequested += prompted.Add;
        vm.LoadIssue(_issue2Id);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);

        Assert.Empty(prompted);
    }

    [Fact]
    public void FinishPrompt_DoesNotFire_MidSeries_WhenAutoNavigateAdvances()
    {
        SetPromptReviewOnFinish(true);
        var prompted = new List<int>();
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.ReviewPromptRequested += prompted.Add;
        vm.LoadIssue(_issue1Id); // has a next issue - AutoNavigateComics is on by default
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);

        vm.NextPageCommand.Execute(null); // shows the chapter card, does not "finish"

        Assert.Empty(prompted);
    }

    [Fact]
    public void FinishPrompt_DoesNotFire_OnBackwardUnderrun_AtStartOfSeries()
    {
        SetPromptReviewOnFinish(true);
        var prompted = new List<int>();
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.ReviewPromptRequested += prompted.Add;
        vm.LoadIssue(_issue3Id); // Number "0" - the first issue in the series
        Assert.Equal("PAGE 1 / 6", vm.PageLabel);

        vm.PreviousPageCommand.Execute(null); // backward past the first page - not "finishing"

        Assert.Empty(prompted);
    }

    [Fact]
    public void FinishPrompt_FiresAtMostOnce_PerLoad()
    {
        SetPromptReviewOnFinish(true);
        var prompted = new List<int>();
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.ReviewPromptRequested += prompted.Add;
        vm.LoadIssue(_issue2Id);
        vm.NextPageCommand.Execute(null);

        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);

        Assert.Equal(new[] { _issue2Id }, prompted);
    }

    // ===================== Chapter transition (docs/superpowers/specs/2026-08-23-reader-chapter-
    // transition-design.md) =====================

    [Fact]
    public void NextPage_PastLastPage_ShowsCardImmediately_AndDefersTheActualNavigate()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);

        vm.NextPageCommand.Execute(null);

        Assert.Equal(ChapterTransitionState.Card, vm.ChapterTransitionState);
        Assert.Equal("#1", vm.ChapterTransitionFromLabel);
        Assert.Equal("#2", vm.ChapterTransitionToLabel);
        // Navigate is deferred behind the hold timer - still on issue 1 until the tick fires.
        Assert.Equal("PAGE 3 / 3", vm.PageLabel);
        Assert.Contains("#1", vm.IssueTitle);
    }

    [Fact]
    public void NextPage_HoldTick_HidesTheCard()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);

        vm.OnChapterTransitionHoldTick(null, EventArgs.Empty);

        Assert.Equal(ChapterTransitionState.Hidden, vm.ChapterTransitionState);
    }

    [Fact]
    public void PreviousPage_BeforeFirstPage_ShowsCard_WithLabelsInBackwardOrder()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue2Id);

        vm.PreviousPageCommand.Execute(null);

        Assert.Equal(ChapterTransitionState.Card, vm.ChapterTransitionState);
        Assert.Equal("#2", vm.ChapterTransitionFromLabel);
        Assert.Equal("#1", vm.ChapterTransitionToLabel);
    }

    [Fact]
    public void NextPage_PastLastPage_AutoNavigateDisabled_NeverShowsTheCard()
    {
        SetAutoNavigateComics(false);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);

        vm.NextPageCommand.Execute(null);

        Assert.Equal(ChapterTransitionState.Hidden, vm.ChapterTransitionState);
    }

    [Fact]
    public void NextPage_RepeatedPresses_WhileCardShowing_AreIgnored()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null); // shows the card, defers navigate

        vm.NextPageCommand.Execute(null); // re-entrant press while still showing
        vm.NextPageCommand.Execute(null);

        // Still just one pending transition - the hold tick lands on issue 2, not further.
        vm.OnChapterTransitionHoldTick(null, EventArgs.Empty);
        Assert.Contains("#2", vm.IssueTitle);
    }

    [Fact]
    public void NextChapterCommand_NavigatesImmediately_EvenWithAutoNavigateDisabled()
    {
        SetAutoNavigateComics(false);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.NextChapterCommand.Execute(null);

        Assert.Contains("#2", vm.IssueTitle);
    }

    [Fact]
    public void PreviousChapterCommand_NavigatesImmediately_EvenWithAutoNavigateDisabled()
    {
        SetAutoNavigateComics(false);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.PreviousChapterCommand.Execute(null);

        Assert.Contains("#0", vm.IssueTitle);
    }

    [Fact]
    public void NextChapterCommand_AtEndOfSeries_NoOps()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue2Id);

        vm.NextChapterCommand.Execute(null);

        Assert.Contains("#2", vm.IssueTitle);
    }

    [Fact]
    public void ChapterBoundaryOverscrollCommand_ShowsLoadingThenNavigatesAndShowsCard()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.ChapterBoundaryOverscrollCommand.Execute(true);
        Assert.Equal(ChapterTransitionState.Loading, vm.ChapterTransitionState);

        vm.OnChapterTransitionLoadDeferTick(null, EventArgs.Empty);

        Assert.Equal(ChapterTransitionState.Card, vm.ChapterTransitionState);
        Assert.Contains("#2", vm.IssueTitle); // navigate already happened, behind the Loading state
    }

    [Fact]
    public void PreviousPage_BeforeFirstPage_LoadsPreviousIssueAtItsLastPage()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue2Id);

        vm.PreviousPageCommand.Execute(null);
        vm.OnChapterTransitionHoldTick(null, EventArgs.Empty); // see NextPage_PastLastPage_LoadsNextIssue_WhenAutoNavigateEnabled

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
        Assert.Equal(new KeyGesture(Key.Left), vm.PageTurnLeftKey);
        Assert.Equal(new KeyGesture(Key.Right), vm.PageTurnRightKey);

        SetKeyBinding(KeyboardCommandRegistry.ReaderPageTurnLeft, new KeyGesture(Key.J));
        vm.LoadIssue(_issue1Id);

        Assert.Equal(new KeyGesture(Key.J), vm.PageTurnLeftKey);
        Assert.Equal(new KeyGesture(Key.Right), vm.PageTurnRightKey); // untouched
    }

    [Fact]
    public void NewReaderShortcutKeys_DefaultCorrectly_AndReflectRemappingOnLoad()
    {
        // Representative sample across all three UI groups (Navigation/Zoom & Fit/Display) -
        // docs/superpowers/specs/2026-08-16-remappable-reader-shortcuts-design.md.
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        Assert.Equal(new KeyGesture(Key.Left), vm.PanLeftKey);
        Assert.Equal(new KeyGesture(Key.Z), vm.ZoomInKey);
        Assert.Equal(new KeyGesture(Key.F), vm.ToggleFullscreenKey);

        SetKeyBinding(KeyboardCommandRegistry.ReaderPanLeft, new KeyGesture(Key.A));
        SetKeyBinding(KeyboardCommandRegistry.ReaderZoomIn, new KeyGesture(Key.OemComma));
        SetKeyBinding(KeyboardCommandRegistry.ReaderToggleFullscreen, new KeyGesture(Key.OemPeriod));
        vm.LoadIssue(_issue1Id);

        Assert.Equal(new KeyGesture(Key.A), vm.PanLeftKey);
        Assert.Equal(new KeyGesture(Key.OemComma), vm.ZoomInKey);
        Assert.Equal(new KeyGesture(Key.OemPeriod), vm.ToggleFullscreenKey);
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
        vm.OnChapterTransitionHoldTick(null, EventArgs.Empty); // see NextPage_PastLastPage_LoadsNextIssue_WhenAutoNavigateEnabled

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
        vm.OnChapterTransitionHoldTick(null, EventArgs.Empty); // see NextPage_PastLastPage_LoadsNextIssue_WhenAutoNavigateEnabled

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

    /// <summary>
    /// docs/superpowers/specs/2026-08-27-vertical-paged-reading-mode-design.md - TopToBottom is a
    /// paged mode (page-turns run along Y), so unlike the *Continuous modes it must stay
    /// IsContinuousMode == false and keep the paged decoder.
    /// </summary>
    [Fact]
    public void SetReadingModeCommand_TopToBottom_IsPagedAndLabelledVertical()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.SetReadingModeCommand.Execute(ReadingMode.TopToBottom);

        Assert.Equal("Vertical ▾", vm.ReadingModeLabel);
        Assert.False(vm.IsContinuousMode);
        Assert.Equal(ReadingMode.TopToBottom, vm.EffectiveReadingMode);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(ReadingMode.TopToBottom, context.Series.First(s => s.Id == _seriesId).ReadingMode);
    }

    [Fact]
    public void TopToBottom_SeededOnSeries_LoadsAsPagedVerticalMode()
    {
        SetSeriesReadingMode(ReadingMode.TopToBottom);
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.LoadIssue(_issue1Id);

        Assert.Equal("Vertical ▾", vm.ReadingModeLabel);
        Assert.False(vm.IsContinuousMode);
        Assert.Equal(ReadingMode.TopToBottom, vm.EffectiveReadingMode);
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

    /// <summary>
    /// docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md
    /// §6 - <see cref="Views.PageCanvas"/> writes <see cref="ReaderScreenViewModel.CurrentContinuousPageIndex"/>
    /// TwoWay every scroll pass; this exercises the VM-side effect directly (as PageCanvas itself
    /// would set it) without needing a live composition visual.
    /// </summary>
    [Fact]
    public void CurrentContinuousPageIndex_InContinuousMode_UpdatesPageLabelAndProgress()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);

        vm.CurrentContinuousPageIndex = 2;

        Assert.Equal("PAGE 3 / 3", vm.PageLabel);
        Assert.Equal(1.0, vm.ProgressFraction);
    }

    [Fact]
    public void CurrentContinuousPageIndex_InPagedMode_IsIgnored()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        Assert.Equal("PAGE 1 / 3", vm.PageLabel);

        vm.CurrentContinuousPageIndex = 2;

        Assert.Equal("PAGE 1 / 3", vm.PageLabel); // paged mode drives PageLabel through GoToPage, not this
    }

    [Fact]
    public void CurrentContinuousPageIndex_ResetsToNegativeOne_OnLoad()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);
        vm.CurrentContinuousPageIndex = 2;

        vm.LoadIssue(_issue1Id);

        Assert.Equal(-1, vm.CurrentContinuousPageIndex);
    }

    /// <summary>
    /// Spec §6's "throttled to avoid a SaveChanges per scroll-frame" - <see cref="ReaderScreenViewModel.FlushPendingPositionSave"/>
    /// is the internal seam tests use instead of waiting on a real <c>DispatcherTimer</c> tick (see
    /// its own doc comment for why - headless tests don't reliably drive a real dispatcher timer).
    /// </summary>
    [Fact]
    public void CurrentContinuousPageIndex_Change_PersistsLastPageRead_OnceFlushed()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);

        vm.CurrentContinuousPageIndex = 1;
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Null(context.Issues.First(i => i.Id == _issue1Id).LastPageRead); // not written yet - still debounced
        }

        vm.FlushPendingPositionSave();

        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal(1, context.Issues.First(i => i.Id == _issue1Id).LastPageRead);
        }
    }

    /// <summary>
    /// Real-world scenario spec §6 calls for: the user scrolls, then immediately navigates away
    /// before the debounce window elapses - <see cref="ReaderScreenViewModel.Load"/> flushes the
    /// *previous* issue's pending save itself, so this shouldn't need an explicit
    /// <see cref="ReaderScreenViewModel.FlushPendingPositionSave"/> call to avoid losing progress.
    /// </summary>
    [Fact]
    public void CurrentContinuousPageIndex_PendingSave_IsFlushed_WhenLoadingADifferentIssue()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);
        vm.CurrentContinuousPageIndex = 1;

        vm.LoadIssue(_issue2Id);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(1, context.Issues.First(i => i.Id == _issue1Id).LastPageRead);
    }

    /// <summary>
    /// Spec §6 - "in continuous mode they [bookmarks/search hits/thumbnail-rail clicks] instead
    /// scroll the target page's top edge into view" rather than a paged-mode index jump, since the
    /// ViewModel has no page-size knowledge to compute a scroll offset itself.
    /// </summary>
    [Fact]
    public void SelectThumbnailCommand_InContinuousMode_RaisesScrollToPageRequested_InsteadOfJumping()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);
        int? requestedIndex = null;
        vm.ScrollToPageRequested += index => requestedIndex = index;

        vm.SelectThumbnailCommand.Execute(vm.Thumbnails[2]);

        Assert.Equal(2, requestedIndex);
        Assert.Equal("PAGE 1 / 3", vm.PageLabel); // unchanged - no paged-mode GoToPage jump happened
    }

    /// <summary>
    /// Real bug, found via manual testing: reopening an issue with a saved <c>LastPageRead</c> in
    /// continuous mode showed the correct page NUMBER in the label but the canvas itself always
    /// started scrolled to page 1 - <c>_currentPageIndex</c> was computed correctly but nothing told
    /// the canvas to actually scroll there. Fixed via the same <see cref="ReaderScreenViewModel.ScrollToPageRequested"/>
    /// path the thumbnail rail already uses.
    /// </summary>
    [Fact]
    public void LoadIssue_InContinuousMode_WithSavedLastPageRead_RaisesScrollToPageRequested_ForResumedPage()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Series.First(s => s.Id == _seriesId).ReadingMode = ReadingMode.VerticalContinuous;
            context.Issues.First(i => i.Id == _issue1Id).LastPageRead = 2;
            context.SaveChanges();
        }

        var vm = new ReaderScreenViewModel(goBack: () => { });
        int? requestedIndex = null;
        vm.ScrollToPageRequested += index => requestedIndex = index;

        vm.LoadIssue(_issue1Id);

        Assert.Equal("PAGE 3 / 3", vm.PageLabel);
        Assert.Equal(2, requestedIndex);
    }

    /// <summary>
    /// Deliberately unconditional, not gated on <c>_currentPageIndex &gt; 0</c>: the same code path
    /// also covers <see cref="ReaderScreenViewModel.NavigateToAdjacentIssue"/>'s backward crossing
    /// (<c>forcedStartPage = int.MaxValue</c>, landing on the *last* page), which would otherwise hit
    /// the identical bug this fix addresses. Firing at index 0 too is a harmless no-op scroll.
    /// </summary>
    [Fact]
    public void LoadIssue_InContinuousMode_AlwaysRaisesScrollToPageRequested_EvenAtPageZero()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Series.First(s => s.Id == _seriesId).ReadingMode = ReadingMode.VerticalContinuous;
            context.SaveChanges();
        }

        var vm = new ReaderScreenViewModel(goBack: () => { });
        int? requestedIndex = null;
        vm.ScrollToPageRequested += index => requestedIndex = index;

        vm.LoadIssue(_issue1Id);

        Assert.Equal(0, requestedIndex);
    }

    [Fact]
    public void LoadIssue_InPagedMode_DoesNotRaiseScrollToPageRequested()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        bool raised = false;
        vm.ScrollToPageRequested += _ => raised = true;

        vm.LoadIssue(_issue1Id);

        Assert.False(raised);
    }

    /// <summary>
    /// User direction: the thumbnail rail should keep the current page's thumbnail scrolled into
    /// view as the current page changes (paged navigation, continuous scroll, or a fresh
    /// <see cref="ReaderScreenViewModel.Load"/>) - "follows along, but it's not really bound to it."
    /// </summary>
    [Fact]
    public void CurrentPageIndexChanged_Fires_OnLoadAndOnPagedNavigation()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        var seen = new List<int>();
        vm.CurrentPageIndexChanged += seen.Add;

        vm.LoadIssue(_issue1Id);
        Assert.Equal(new[] { 0 }, seen);

        vm.NextPageCommand.Execute(null);
        Assert.Equal(new[] { 0, 1 }, seen);
    }

    /// <summary>
    /// Same underlying bug/fix as <see cref="LoadIssue_InContinuousMode_WithSavedLastPageRead_RaisesScrollToPageRequested_ForResumedPage"/>,
    /// via a different <c>_currentPageIndex</c> source: <see cref="ReaderScreenViewModel.PreviousPage"/>
    /// crossing backward into a previous issue lands on that issue's *last* page
    /// (<c>forcedStartPage = int.MaxValue</c>) - continuous mode needs the canvas scrolled there too,
    /// not just the label showing it.
    /// </summary>
    [Fact]
    public void PreviousPage_CrossingIssueBoundaryBackward_InContinuousMode_ScrollsToLastPageOfPreviousIssue()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Series.First(s => s.Id == _seriesId).ReadingMode = ReadingMode.VerticalContinuous;
            context.SaveChanges();
        }

        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue2Id);
        int? requestedIndex = null;
        vm.ScrollToPageRequested += index => requestedIndex = index;

        vm.PreviousPageCommand.Execute(null);
        vm.OnChapterTransitionHoldTick(null, EventArgs.Empty); // see NextPage_PastLastPage_LoadsNextIssue_WhenAutoNavigateEnabled

        Assert.Equal("PAGE 3 / 3", vm.PageLabel);
        Assert.Contains("#1", vm.IssueTitle);
        Assert.Equal(2, requestedIndex); // issue1 has 3 pages - last index is 2
    }

    [Fact]
    public void CurrentPageIndexChanged_Fires_OnContinuousModeScroll()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);
        var seen = new List<int>();
        vm.CurrentPageIndexChanged += seen.Add;

        vm.CurrentContinuousPageIndex = 2;

        Assert.Equal(new[] { 2 }, seen);
    }

    /// <summary>docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §7 - one combined toggle, entering fullscreen also shows the overlay layer immediately.</summary>
    [Fact]
    public void ToggleFullscreenCommand_TurnsOn_AndShowsOverlaysImmediately()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.ToggleFullscreenCommand.Execute(null);

        Assert.True(vm.IsFullscreen);
        Assert.True(vm.ShowChrome);
    }

    /// <summary>docs/superpowers/specs/2026-08-25-reader-chrome-design.md - ShowChrome (renamed from
    /// ShowFullscreenOverlays) now applies in windowed mode too, so leaving fullscreen no longer
    /// hides it - the toggle itself counts as activity, same as any other cursor movement.</summary>
    [Fact]
    public void ToggleFullscreenCommand_TurnsOff_ChromeStaysVisible()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.ToggleFullscreenCommand.Execute(null);

        vm.ToggleFullscreenCommand.Execute(null);

        Assert.False(vm.IsFullscreen);
        Assert.True(vm.ShowChrome);
    }

    /// <summary>Fullscreen is a window-chrome session preference, not per-book view state (unlike ZoomLevel/ManualRotationDegrees/ScrollOffset, all of which reset every Load) - switching books mid-fullscreen-session should stay fullscreen.</summary>
    [Fact]
    public void IsFullscreen_PersistsAcrossLoad_UnlikeZoomOrRotation()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.ToggleFullscreenCommand.Execute(null);
        Assert.True(vm.IsFullscreen);

        vm.LoadIssue(_issue2Id);

        Assert.True(vm.IsFullscreen);
    }

    [Fact]
    public void GoBackCommand_ExitsFullscreen()
    {
        bool wentBack = false;
        var vm = new ReaderScreenViewModel(goBack: () => wentBack = true);
        vm.LoadIssue(_issue1Id);
        vm.ToggleFullscreenCommand.Execute(null);

        vm.GoBackCommand.Execute(null);

        Assert.False(vm.IsFullscreen);
        Assert.False(vm.ShowChrome);
        Assert.True(wentBack);
    }

    /// <summary>docs/superpowers/specs/2026-08-25-reader-chrome-design.md - idle-fade now applies in
    /// windowed mode too (previously this asserted the opposite: cursor activity was a no-op outside
    /// fullscreen). NotifyCursorActivity no longer gates on IsFullscreen at all.</summary>
    [Fact]
    public void NotifyCursorActivity_InWindowedMode_ShowsChromeToo()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.NotifyCursorActivity();

        Assert.False(vm.IsFullscreen);
        Assert.True(vm.ShowChrome);
    }

    /// <summary>docs/superpowers/specs/2026-08-25-reader-chrome-design.md - the drawer's open state is independent of chrome idle-fade, it doesn't hide on its own.</summary>
    [Fact]
    public void ToggleDrawerCommand_FlipsIsDrawerOpen_WithoutTouchingShowChrome()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        bool chromeBefore = vm.ShowChrome;

        vm.ToggleDrawerCommand.Execute(null);
        Assert.True(vm.IsDrawerOpen);
        Assert.Equal(chromeBefore, vm.ShowChrome);

        vm.ToggleDrawerCommand.Execute(null);
        Assert.False(vm.IsDrawerOpen);
    }

    /// <summary>docs/superpowers/specs/2026-08-25-reader-chrome-design.md - the real bug this phase fixes: a hint bound at construction time would go stale after a remap. GetShortcutHint reads KeyBindingService fresh on every call instead.</summary>
    [Fact]
    public void GetShortcutHint_ReflectsARemapMadeAfterConstruction()
    {
        var keyBindingService = new KeyBindingService(() => PaperbunkrDb.CreateContext());
        var vm = new ReaderScreenViewModel(goBack: () => { }, keyBindingService);
        string before = vm.GetShortcutHint(KeyboardCommandRegistry.ReaderRotateClockwise);

        keyBindingService.SetKey(KeyboardCommandRegistry.ReaderRotateClockwise, new KeyGesture(Key.J));
        string after = vm.GetShortcutHint(KeyboardCommandRegistry.ReaderRotateClockwise);

        Assert.NotEqual(before, after);
        Assert.Contains("J", after);
    }

    /// <summary>docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §9 - effective value with no override/default set is just 0 (CE's own BitmapAdjustment.Empty).</summary>
    [Fact]
    public void Adjustment_DefaultsToZero_ForAnIssueWithNoOverrideOrGlobalDefault()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        Assert.Equal(0, vm.Brightness);
        Assert.Equal(0, vm.Contrast);
        Assert.Equal(0, vm.Saturation);
        Assert.Equal(0, vm.Gamma);
    }

    [Fact]
    public void Adjustment_ReflectsGlobalDefault_ForAnIssueWithNoOverride()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            settings.DefaultBrightness = 20;
            settings.DefaultContrast = -10;
            context.SaveChanges();
        }

        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        Assert.Equal(20, vm.Brightness);
        Assert.Equal(-10, vm.Contrast);
    }

    /// <summary>Setting the bound (effective) value persists just the delta as the per-issue override - additive like CE's own BitmapAdjustment.Add, mirroring SetFitModeCommand's per-issue-override shape but for a continuous slider instead of a discrete enum.</summary>
    [Fact]
    public void Adjustment_SettingEffectiveValue_PersistsOverrideDelta_ReadBackOnNextLoad()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.GetOrCreateAppSettings().DefaultBrightness = 20;
            context.SaveChanges();
        }

        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.Brightness = 35; // effective 35 with a global default of 20 -> override should be +15

        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal(15, context.Issues.First(i => i.Id == _issue1Id).BrightnessOverride);
        }

        var reopened = new ReaderScreenViewModel(goBack: () => { });
        reopened.LoadIssue(_issue1Id);
        Assert.Equal(35, reopened.Brightness);
    }

    [Fact]
    public void Adjustment_OnOneIssue_DoesNotAffectAnother()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.Saturation = 50;

        vm.LoadIssue(_issue2Id);

        Assert.Equal(0, vm.Saturation); // issue2's own default, untouched
    }

    [Fact]
    public void ResetAdjustmentCommand_ClearsPerIssueOverrides_BackToGlobalDefaults()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.GetOrCreateAppSettings().DefaultGamma = 5;
            context.SaveChanges();
        }

        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.Brightness = 40;
        vm.Gamma = 25;

        vm.ResetAdjustmentCommand.Execute(null);

        Assert.Equal(0, vm.Brightness);
        Assert.Equal(5, vm.Gamma); // back to the global default, not zero
        using var context2 = PaperbunkrDb.CreateContext();
        var issue = context2.Issues.First(i => i.Id == _issue1Id);
        Assert.Null(issue.BrightnessOverride);
        Assert.Null(issue.GammaOverride);
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

    /// <summary>docs/superpowers/specs/2026-08-13-reader-page-transition-animations-design.md §5.</summary>
    [Fact]
    public void PageTransitionSettings_DefaultToNoneAnd250_AndReflectAppSettingsOnLoad()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        Assert.Equal(PageTransitionStyle.None, vm.PageTransitionStyle);
        Assert.Equal(250, vm.PageTransitionDurationMs);

        SetPageTransitionSettings(PageTransitionStyle.Slide, 400);
        vm.LoadIssue(_issue1Id);

        Assert.Equal(PageTransitionStyle.Slide, vm.PageTransitionStyle);
        Assert.Equal(400, vm.PageTransitionDurationMs);
    }

    /// <summary>
    /// Unlike SetFitModeCommand (per-Issue override), this has no per-Issue column - it's a Reader-
    /// toolbar shortcut to the same global AppSettings.PageTransitionStyle value Preferences edits, so
    /// it should be visible both to a freshly reopened issue and to a *different* issue's own
    /// ViewModel instance, not scoped to the issue it was set from.
    /// </summary>
    [Fact]
    public void SetPageTransitionStyleCommand_PersistsGlobally_VisibleToAnyIssue()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.SetPageTransitionStyleCommand.Execute(PageTransitionStyle.Crossfade);

        Assert.Equal(PageTransitionStyle.Crossfade, vm.PageTransitionStyle);
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal(PageTransitionStyle.Crossfade, context.GetOrCreateAppSettings().PageTransitionStyle);
        }

        var otherVm = new ReaderScreenViewModel(goBack: () => { });
        otherVm.LoadIssue(_issue2Id);
        Assert.Equal(PageTransitionStyle.Crossfade, otherVm.PageTransitionStyle);
    }

    // Double-page spread (docs/superpowers/specs/2026-08-15-reader-double-page-spread-design.md) -
    // _issue3Id's fixture: index 0 cover, 1+2 portrait (pairs), 3 landscape (breaks pairing), 4+5
    // portrait (pairs again). See its own setup comment in the constructor for the full layout.

    [Fact]
    public void CurrentPageSecondary_Null_InSingleMode()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue3Id);
        vm.SelectThumbnailCommand.Execute(vm.Thumbnails[1]);

        Assert.Null(vm.CurrentPageSecondary);
    }

    [Fact]
    public void CurrentPageSecondary_Null_ForTheCoverEvenInDoubleMode()
    {
        SetSeriesPageLayoutMode(PageLayoutMode.Double);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue3Id);

        Assert.Null(vm.CurrentPageSecondary);
    }

    [Fact]
    public void CurrentPageSecondary_PairsAdjacentPortraitPages()
    {
        SetSeriesPageLayoutMode(PageLayoutMode.Double);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue3Id);
        vm.SelectThumbnailCommand.Execute(vm.Thumbnails[1]);

        Assert.NotNull(vm.CurrentPageSecondary);
    }

    [Fact]
    public void CurrentPageSecondary_Null_WhenTheNextPageIsLandscape()
    {
        SetSeriesPageLayoutMode(PageLayoutMode.Double);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue3Id);
        vm.SelectThumbnailCommand.Execute(vm.Thumbnails[2]); // page 3 (landscape) would be its partner

        Assert.Null(vm.CurrentPageSecondary);
    }

    [Fact]
    public void CurrentPageSecondary_Null_WhenThePageItselfIsLandscape()
    {
        SetSeriesPageLayoutMode(PageLayoutMode.Double);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue3Id);
        vm.SelectThumbnailCommand.Execute(vm.Thumbnails[3]); // landscape itself

        Assert.Null(vm.CurrentPageSecondary);
    }

    [Fact]
    public void CurrentPageSecondary_Null_OnTheLastPageWithNoPartner()
    {
        SetSeriesPageLayoutMode(PageLayoutMode.Double);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue2Id); // 2 portrait pages
        vm.SelectThumbnailCommand.Execute(vm.Thumbnails[1]);

        Assert.Null(vm.CurrentPageSecondary);
    }

    [Fact]
    public void NextPage_StepsByTwo_WhenCurrentlyPaired()
    {
        SetSeriesPageLayoutMode(PageLayoutMode.Double);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue3Id);
        vm.SelectThumbnailCommand.Execute(vm.Thumbnails[1]); // paired with page 2

        vm.NextPageCommand.Execute(null);

        Assert.Equal("PAGE 4 / 6", vm.PageLabel); // lands on index 3 (landscape, solo)
    }

    [Fact]
    public void NextPage_StepsByOne_WhenCurrentlySolo()
    {
        SetSeriesPageLayoutMode(PageLayoutMode.Double);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue3Id);
        vm.SelectThumbnailCommand.Execute(vm.Thumbnails[3]); // landscape, solo

        vm.NextPageCommand.Execute(null);

        Assert.Equal("PAGE 5 / 6", vm.PageLabel); // lands on index 4, first of the (4,5) pair
    }

    [Fact]
    public void PreviousPage_StepsByTwo_WhenThePairImmediatelyBehindIsEligible()
    {
        SetSeriesPageLayoutMode(PageLayoutMode.Double);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue3Id);
        vm.SelectThumbnailCommand.Execute(vm.Thumbnails[3]); // landscape, solo, at index 3

        vm.PreviousPageCommand.Execute(null);

        Assert.Equal("PAGE 2 / 6", vm.PageLabel); // (1,2) eligible behind index 3, steps back to index 1
    }

    [Fact]
    public void PreviousPage_StepsByOne_WhenThePairImmediatelyBehindIsNotEligible()
    {
        SetSeriesPageLayoutMode(PageLayoutMode.Double);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue3Id);
        vm.SelectThumbnailCommand.Execute(vm.Thumbnails[4]); // paired with index 5, at index 4

        vm.PreviousPageCommand.Execute(null);

        Assert.Equal("PAGE 4 / 6", vm.PageLabel); // (2,3) not eligible (page 3 landscape), steps back to index 3 only
    }

    [Fact]
    public void EffectivePageLayoutMode_ResolvesFromAppSettingsDefault_WhenSeriesAndIssueUnset()
    {
        SetDefaultPageLayoutMode(PageLayoutMode.Double);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        Assert.Equal(PageLayoutMode.Double, vm.EffectivePageLayoutMode);
    }

    [Fact]
    public void EffectivePageLayoutMode_SeriesOverridesAppSettingsDefault()
    {
        SetDefaultPageLayoutMode(PageLayoutMode.Single);
        SetSeriesPageLayoutMode(PageLayoutMode.Double);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        Assert.Equal(PageLayoutMode.Double, vm.EffectivePageLayoutMode);
    }

    [Fact]
    public void EffectivePageLayoutMode_IssueOverrideWinsOverSeriesAndAppSettings()
    {
        SetDefaultPageLayoutMode(PageLayoutMode.Double);
        SetSeriesPageLayoutMode(PageLayoutMode.Double);
        SetIssuePageLayoutModeOverride(_issue1Id, PageLayoutMode.Single);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        Assert.Equal(PageLayoutMode.Single, vm.EffectivePageLayoutMode);
    }

    [Fact]
    public void ToggleDoublePageModeCommand_PersistsToSeries_AndRePairsImmediately()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue3Id);
        vm.SelectThumbnailCommand.Execute(vm.Thumbnails[1]);
        Assert.Null(vm.CurrentPageSecondary); // Single mode by default

        vm.ToggleDoublePageModeCommand.Execute(null);

        Assert.Equal(PageLayoutMode.Double, vm.EffectivePageLayoutMode);
        Assert.NotNull(vm.CurrentPageSecondary); // re-paired immediately, page 1+2 both portrait
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal(PageLayoutMode.Double, context.Series.Find(_seriesId)!.PageLayoutMode);
        }

        vm.ToggleDoublePageModeCommand.Execute(null);

        Assert.Equal(PageLayoutMode.Single, vm.EffectivePageLayoutMode);
        Assert.Null(vm.CurrentPageSecondary);
    }

    /// <summary>
    /// docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md
    /// §10 - background/margin are global-only, no per-Issue override, read fresh on every Load.
    /// </summary>
    [Fact]
    public void PageMarginMultiplier_DefaultsTo1_WhenMarginDisabled()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        Assert.Equal(1.0, vm.PageMarginMultiplier);
    }

    [Fact]
    public void PageMarginMultiplier_ReflectsAppSettings_WhenMarginEnabled()
    {
        SetPageMargin(enabled: true, percentWidth: 0.05);
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.LoadIssue(_issue1Id);

        Assert.Equal(0.95, vm.PageMarginMultiplier, 6);
    }

    [Fact]
    public void PageMarginMultiplier_UsesConfiguredPercentWidth()
    {
        SetPageMargin(enabled: true, percentWidth: 0.2);
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.LoadIssue(_issue1Id);

        Assert.Equal(0.8, vm.PageMarginMultiplier, 6);
    }

    /// <summary>
    /// Real bug, found via manual testing: background/margin were only ever re-read inside Load,
    /// so changing them in Preferences while the same book stayed open (the rail-nav switcher never
    /// destroys/recreates the Reader) appeared to "get stuck" on whatever was set the last time Load
    /// happened to run. RefreshDisplaySettings is what MainViewModel wires to
    /// PreferencesScreenViewModel.ReaderDisplaySettingsChanged to fix that - this test exercises the
    /// method directly, without needing a full PreferencesScreenViewModel/MainViewModel wiring.
    /// </summary>
    [Fact]
    public void RefreshDisplaySettings_PicksUpChanges_WithoutReloadingTheIssue()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        Assert.Equal(1.0, vm.PageMarginMultiplier);

        SetPageMargin(enabled: true, percentWidth: 0.1);
        SetImageBackgroundMode(ImageBackgroundMode.Color);
        SetBackgroundColor("WhiteSmoke");
        vm.RefreshDisplaySettings();

        Assert.Equal(0.9, vm.PageMarginMultiplier, 6);
        var brush = Assert.IsType<ImmutableSolidColorBrush>(vm.CanvasBackgroundBrush);
        Assert.Equal(Colors.WhiteSmoke, brush.Color);
    }

    [Fact]
    public void RefreshDisplaySettings_WorksBeforeAnyIssueIsLoaded()
    {
        SetImageBackgroundMode(ImageBackgroundMode.Color);
        SetBackgroundColor("Black");
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.RefreshDisplaySettings();

        var brush = Assert.IsType<ImmutableSolidColorBrush>(vm.CanvasBackgroundBrush);
        Assert.Equal(Colors.Black, brush.Color);
    }

    [Fact]
    public void CanvasBackgroundBrush_AutoMode_UsesTheFixedDefault_NotTheConfiguredColor()
    {
        SetImageBackgroundMode(ImageBackgroundMode.Auto);
        SetBackgroundColor("Red"); // should be ignored in Auto mode
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.LoadIssue(_issue1Id);

        var brush = Assert.IsType<ImmutableSolidColorBrush>(vm.CanvasBackgroundBrush);
        Assert.Equal(Color.Parse("#0B0C0F"), brush.Color);
    }

    [Fact]
    public void CanvasBackgroundBrush_ColorMode_ParsesTheConfiguredNamedColor()
    {
        SetImageBackgroundMode(ImageBackgroundMode.Color);
        SetBackgroundColor("WhiteSmoke");
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.LoadIssue(_issue1Id);

        var brush = Assert.IsType<ImmutableSolidColorBrush>(vm.CanvasBackgroundBrush);
        Assert.Equal(Colors.WhiteSmoke, brush.Color);
    }

    [Fact]
    public void CanvasBackgroundBrush_ColorMode_ParsesAHexColor()
    {
        SetImageBackgroundMode(ImageBackgroundMode.Color);
        SetBackgroundColor("#112233");
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.LoadIssue(_issue1Id);

        var brush = Assert.IsType<ImmutableSolidColorBrush>(vm.CanvasBackgroundBrush);
        Assert.Equal(Color.Parse("#112233"), brush.Color);
    }

    [Fact]
    public void CanvasBackgroundBrush_ColorMode_InvalidColor_FallsBackToTheFixedDefault()
    {
        SetImageBackgroundMode(ImageBackgroundMode.Color);
        SetBackgroundColor("not-a-real-color");
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.LoadIssue(_issue1Id);

        var brush = Assert.IsType<ImmutableSolidColorBrush>(vm.CanvasBackgroundBrush);
        Assert.Equal(Color.Parse("#0B0C0F"), brush.Color);
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
    public void ManualRotationDegrees_RotateCounterClockwiseStepsByMinus90AndWraps()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        Assert.Equal(0, vm.ManualRotationDegrees);

        vm.RotateCounterClockwiseCommand.Execute(null);
        Assert.Equal(270, vm.ManualRotationDegrees);

        vm.RotateCounterClockwiseCommand.Execute(null);
        vm.RotateCounterClockwiseCommand.Execute(null);
        vm.RotateCounterClockwiseCommand.Execute(null);

        Assert.Equal(0, vm.ManualRotationDegrees); // four -90-degree steps wrap back to 0
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
    public void ToggleAutoScrollCommand_TogglesIsAutoScrolling()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);
        Assert.False(vm.IsAutoScrolling);

        vm.ToggleAutoScrollCommand.Execute(null);
        Assert.True(vm.IsAutoScrolling);

        vm.ToggleAutoScrollCommand.Execute(null);
        Assert.False(vm.IsAutoScrolling);
    }

    [Fact]
    public void AutoScroll_TickAdvancesScrollOffsetBySpeedTimesInterval()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);
        vm.ToggleAutoScrollCommand.Execute(null);
        double before = vm.ScrollOffset;

        vm.OnAutoScrollTick(null, EventArgs.Empty);

        Assert.Equal(before + (vm.AutoScrollSpeed * 0.04), vm.ScrollOffset, precision: 3);
    }

    [Fact]
    public void AutoScroll_ManualScrollOffsetWrite_StopsIt()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);
        vm.ToggleAutoScrollCommand.Execute(null);
        Assert.True(vm.IsAutoScrolling);

        // Simulates a drag/wheel/keyboard scroll round-tripped in from PageCanvas's TwoWay binding.
        vm.ScrollOffset = 500;

        Assert.False(vm.IsAutoScrolling);
    }

    /// <summary>
    /// No live PageCanvas in a ViewModel unit test, so there's nothing to reclamp ScrollOffset at a
    /// real end-of-book - exercises the tick handler's own before/after stop condition directly by
    /// forcing a zero-magnitude tick (AutoScrollSpeed = 0), the same code path a real saturated
    /// reclamp round-trip would hit.
    /// </summary>
    [Fact]
    public void AutoScroll_TickThatDoesNotMoveScrollOffset_StopsIt()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);
        vm.ToggleAutoScrollCommand.Execute(null);
        vm.AutoScrollSpeed = 0;

        vm.OnAutoScrollTick(null, EventArgs.Empty);

        Assert.False(vm.IsAutoScrolling);
    }

    [Fact]
    public void LoadIssue_And_GoBack_BothStopAutoScroll()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);
        vm.ToggleAutoScrollCommand.Execute(null);
        Assert.True(vm.IsAutoScrolling);

        vm.LoadIssue(_issue1Id);
        Assert.False(vm.IsAutoScrolling);

        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);
        vm.ToggleAutoScrollCommand.Execute(null);
        Assert.True(vm.IsAutoScrolling);

        vm.GoBackCommand.Execute(null);
        Assert.False(vm.IsAutoScrolling);
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

    /// <summary>docs/superpowers/specs/2026-08-17-metadata-model-phase1-canonical-metadata-design.md - the first place OpenCount/OpenedTime are actually written; confirmed via a fresh DB read, not just the in-memory object, matching this project's existing pattern for AppSettings-touching ReaderScreenViewModel behavior.</summary>
    [Fact]
    public void LoadIssue_IncrementsOpenCount_AndSetsOpenedTime_OnlyForTheLoadedIssue()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.LoadIssue(_issue1Id);

        using var context = PaperbunkrDb.CreateContext();
        var loaded = context.Issues.First(i => i.Id == _issue1Id);
        Assert.Equal(1, loaded.OpenCount);
        Assert.NotNull(loaded.OpenedTime);

        var untouched = context.Issues.First(i => i.Id == _issue2Id);
        Assert.Equal(0, untouched.OpenCount);
        Assert.Null(untouched.OpenedTime);
    }

    [Fact]
    public void LoadIssue_CalledAgain_IncrementsOpenCountFurther()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.LoadIssue(_issue1Id);
        vm.LoadIssue(_issue2Id);
        vm.LoadIssue(_issue1Id);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(2, context.Issues.First(i => i.Id == _issue1Id).OpenCount);
    }

    // ===================== Reading Status (docs/superpowers/specs/2026-08-19-metadata-model-reading-status-design.md) =====================

    [Fact]
    public void LoadIssue_FirstOpen_SetsSeriesReadingStatusToReading()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.LoadIssue(_issue1Id);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(ReadingStatus.Reading, context.Series.First(s => s.Id == _seriesId).ReadingStatus);
    }

    [Fact]
    public void LoadIssue_SeriesAlreadyDropped_DoesNotOverwriteReadingStatus()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Series.First(s => s.Id == _seriesId).ReadingStatus = ReadingStatus.Dropped;
            context.SaveChanges();
        }

        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        using var reloaded = PaperbunkrDb.CreateContext();
        Assert.Equal(ReadingStatus.Dropped, reloaded.Series.First(s => s.Id == _seriesId).ReadingStatus);
    }

    // ===================== Bookmarks (docs/superpowers/specs/2026-08-18-metadata-model-ui-gaps-status-and-bookmarks-design.md) =====================

    /// <summary>docs/superpowers/specs/2026-08-25-reader-chrome-design.md - Actions cluster's glow pulse; the timer-driven "back to false" half isn't asserted here (no virtual-clock seam on this ViewModel to advance PbGlowPulseDuration deterministically), matching this test class's existing precedent of not unit-testing DispatcherTimer completion.</summary>
    [Fact]
    public void ToggleBookmark_SetsBookmarkJustToggled_ForTheGlowPulse()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.ToggleBookmarkCommand.Execute(null);

        Assert.True(vm.BookmarkJustToggled);
    }

    [Fact]
    public void ToggleBookmark_OnUnbookmarkedPage_CreatesRowWithAutoLabel_MarksStateActive()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id); // 3 pages, starts on page index 0

        vm.ToggleBookmarkCommand.Execute(null);

        Assert.True(vm.IsCurrentPageBookmarked);
        var summary = Assert.Single(vm.Bookmarks);
        Assert.Equal(0, summary.PageNumber);
        Assert.Equal("Page 1", summary.Label);
        Assert.True(vm.Thumbnails[0].IsBookmarked);

        using var context = PaperbunkrDb.CreateContext();
        var row = Assert.Single(context.IssueBookmarks.Where(b => b.IssueId == _issue1Id));
        Assert.Equal(0, row.PageNumber);
    }

    [Fact]
    public void ToggleBookmark_OnAlreadyBookmarkedPage_RemovesIt()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.ToggleBookmarkCommand.Execute(null);

        vm.ToggleBookmarkCommand.Execute(null);

        Assert.False(vm.IsCurrentPageBookmarked);
        Assert.Empty(vm.Bookmarks);
        Assert.False(vm.Thumbnails[0].IsBookmarked);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.IssueBookmarks.Where(b => b.IssueId == _issue1Id));
    }

    [Fact]
    public void GoToBookmark_NavigatesToItsPage()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null); // now on page index 2
        vm.ToggleBookmarkCommand.Execute(null);
        vm.SelectThumbnailCommand.Execute(vm.Thumbnails[0]); // navigate away

        vm.GoToBookmarkCommand.Execute(vm.Bookmarks[0]);

        Assert.Equal("PAGE 3 / 3", vm.PageLabel);
    }

    [Fact]
    public void DeleteBookmark_RemovesRowAndClearsActiveState()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.ToggleBookmarkCommand.Execute(null);
        var summary = vm.Bookmarks[0];

        vm.DeleteBookmarkCommand.Execute(summary);

        Assert.False(vm.IsCurrentPageBookmarked);
        Assert.Empty(vm.Bookmarks);
    }

    [Fact]
    public void PreviousNextBookmark_FindNearestInEachDirection_NoOpAtEnds()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id); // 3 pages: 0, 1, 2
        vm.ToggleBookmarkCommand.Execute(null); // bookmark page 0 (starts there)
        vm.SelectThumbnailCommand.Execute(vm.Thumbnails[2]);
        vm.ToggleBookmarkCommand.Execute(null); // bookmark page 2
        vm.SelectThumbnailCommand.Execute(vm.Thumbnails[1]); // sit between the two bookmarks

        vm.PreviousBookmarkCommand.Execute(null);
        Assert.Equal("PAGE 1 / 3", vm.PageLabel); // jumped to page 0

        vm.NextBookmarkCommand.Execute(null);
        vm.NextBookmarkCommand.Execute(null);
        Assert.Equal("PAGE 3 / 3", vm.PageLabel); // jumped to page 2, then no-op (no bookmark after it)

        vm.PreviousBookmarkCommand.Execute(null);
        vm.PreviousBookmarkCommand.Execute(null);
        Assert.Equal("PAGE 1 / 3", vm.PageLabel); // back to page 0, then no-op (no bookmark before it)
    }

    [Fact]
    public void Bookmarks_AreScopedToTheirOwnIssue()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.ToggleBookmarkCommand.Execute(null);

        vm.LoadIssue(_issue2Id);

        Assert.Empty(vm.Bookmarks);
        Assert.False(vm.IsCurrentPageBookmarked);
    }

    // ===================== Reading-list-order auto-advance (docs/superpowers/specs/2026-08-23-
    // cbl-manager-manual-editing-and-list-aware-reading-design.md §3) =====================

    [Fact]
    public void NextPage_PastLastPage_FollowsReadingListOrder_AcrossSeries_WhenAnchored()
    {
        // List order deliberately differs from series order (issue1's series-order successor is
        // issue2, not the other-series issue) - proves list mode actually took effect.
        int listId = CreateReadingList(_issue1Id, _issue4Id, _issue2Id);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id, listId); // 3 pages
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);
        Assert.Equal("PAGE 3 / 3", vm.PageLabel);

        vm.NextPageCommand.Execute(null);
        vm.OnChapterTransitionHoldTick(null, EventArgs.Empty);

        Assert.Equal("PAGE 1 / 1", vm.PageLabel);
        Assert.Contains("Other Series", vm.BreadcrumbSeries);
    }

    [Fact]
    public void NextPage_PastLastPage_SkipsAMissingRow_WhenAnchoredToReadingList()
    {
        int placeholderId = CreatePlaceholderIssue();
        int listId = CreateReadingList(_issue1Id, placeholderId, _issue4Id);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id, listId);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);

        vm.NextPageCommand.Execute(null);
        vm.OnChapterTransitionHoldTick(null, EventArgs.Empty);

        Assert.Contains("Other Series", vm.BreadcrumbSeries); // landed on issue4, not the placeholder
    }

    [Fact]
    public void NextPage_AtTheListsLastIssue_NoOps_InsteadOfFallingBackToSeriesOrder()
    {
        // issue1 is last in the list here, even though its series-order successor (issue2) exists.
        int listId = CreateReadingList(_issue4Id, _issue1Id);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id, listId);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);
        Assert.Equal("PAGE 3 / 3", vm.PageLabel);

        vm.NextPageCommand.Execute(null);

        Assert.Equal("PAGE 3 / 3", vm.PageLabel);
        Assert.Contains("#1", vm.IssueTitle);
        Assert.Equal(ChapterTransitionState.Hidden, vm.ChapterTransitionState);
    }

    [Fact]
    public void LoadIssue_WithThePlainOverload_ClearsAnyPreviousReadingListAnchor()
    {
        // Under this list, issue2 is NOT the last item - if the anchor survived the plain LoadIssue
        // below, NextPage would advance to issue4 instead of no-op'ing on plain series order.
        int listId = CreateReadingList(_issue1Id, _issue2Id, _issue4Id);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id, listId);

        vm.LoadIssue(_issue2Id); // plain single-arg overload - no reading-list id

        vm.NextPageCommand.Execute(null);
        Assert.Equal("PAGE 2 / 2", vm.PageLabel);
        vm.NextPageCommand.Execute(null); // past the last page - series order has no successor for issue2

        Assert.Equal("PAGE 2 / 2", vm.PageLabel);
        Assert.Contains("#2", vm.IssueTitle);
    }
}
