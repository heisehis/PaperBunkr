using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ReaderScreenViewModel"/>'s <c>OpenLastPage</c>/<c>AutoNavigateComics</c>
/// behavior (docs/superpowers/specs/2026-08-07-preferences-behavior-tab-design.md §3). Redirects
/// <see cref="PaperbunkrDbContext.DatabasePathOverride"/> to a temp SQLite file for the whole test
/// - unlike <see cref="ThemeService"/>/<c>CoverThumbnailService</c>, none of the App-side
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
        new KeyBindingService().AddKey(commandId, gesture);

    private void SetSeriesReadingMode(ReadingMode mode)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.Series.First(s => s.Id == _seriesId).ReadingMode = mode;
        context.SaveChanges();
    }

    // ===== Jump-back chip (docs/superpowers/specs/2026-09-21-comic-reader-flow-and-defaults-design.md 4, pitch #10) =====

    private string? _longIssuePath;

    /// <summary>Best-effort: the reader may still hold the archive open (same reason <see cref="Dispose"/> swallows IOException).</summary>
    private void TryDeleteLongIssue()
    {
        try
        {
            if (_longIssuePath is not null && File.Exists(_longIssuePath)) File.Delete(_longIssuePath);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A 12-page issue in the test series - the shared fixtures top out at 6 pages, too short for a jump of more than 5.</summary>
    private int CreateLongIssue()
    {
        _longIssuePath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_vm_long_{Guid.NewGuid():N}.cbz");
        CbzFixture.Create(_longIssuePath, pageCount: 12);
        using var context = PaperbunkrDb.CreateContext();
        var issue = new Issue { SeriesId = _seriesId, Number = "5", FilePath = _longIssuePath };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    [Fact]
    public void JumpBack_ThumbnailJumpOverThreshold_ShowsChip_AndReturnRestoresThePage()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());

            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[10]);

            Assert.Equal("PAGE 11 / 12", vm.PageLabel);
            Assert.True(vm.HasJumpBack);
            Assert.Equal("Back to page 1", vm.JumpBackLabel);

            vm.JumpBackCommand.Execute(null);

            Assert.Equal("PAGE 1 / 12", vm.PageLabel);
            Assert.False(vm.HasJumpBack);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Theory]
    [InlineData(5, false)] // exactly the threshold is not "a big jump"
    [InlineData(6, true)]
    public void JumpBack_OnlyAppearsForJumpsBeyondTheThreshold(int targetPage, bool expectChip)
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());

            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[targetPage]);

            Assert.Equal(expectChip, vm.HasJumpBack);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void JumpBack_ClearsOnTheNextPageTurn_AndOnExpiry()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());

            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[10]);
            vm.NextPageCommand.Execute(null);
            Assert.False(vm.HasJumpBack);

            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[0]);
            Assert.True(vm.HasJumpBack);
            vm.OnJumpBackExpired(null, EventArgs.Empty);
            Assert.False(vm.HasJumpBack);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void JumpBack_KeepsOnlyTheMostRecentOrigin()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());

            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[8]); // from page 1
            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[0]); // from page 9

            Assert.Equal("Back to page 9", vm.JumpBackLabel);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void JumpBack_IsARegisteredRemappableCommand()
    {
        Assert.Contains(KeyboardCommandRegistry.Commands, c => c.Id == KeyboardCommandRegistry.ReaderJumpBack);
    }

    // ===== Panels & zoom (docs/superpowers/specs/2026-09-25-comic-reader-panels-and-zoom-design.md) =====

    private static readonly Paperbunkr.App.Services.Reader.Panels.PagePanels TwoPanels = new(
        [new Paperbunkr.App.Services.Reader.Panels.PanelRect(0.05, 0.05, 0.9, 0.42), new Paperbunkr.App.Services.Reader.Panels.PanelRect(0.05, 0.53, 0.9, 0.42)], true);

    private static void WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
        {
            TestDispatcher.Drain();
            Thread.Sleep(10);
        }

        TestDispatcher.Drain();
    }

    [Fact]
    public void GuidedView_IsOffByDefault_AndTogglesInPagedMode()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());
            Assert.False(vm.IsGuidedView);

            vm.ToggleGuidedViewCommand.Execute(null);
            Assert.True(vm.IsGuidedView);

            vm.ToggleGuidedViewCommand.Execute(null);
            Assert.False(vm.IsGuidedView);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void GuidedView_RefusesInContinuousMode_AndInDoublePageLayout_WithAToast()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            var toasts = new List<ToastRequest>();
            vm.ToastRequested += toasts.Add;
            vm.LoadIssue(CreateLongIssue());
            vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);

            vm.ToggleGuidedViewCommand.Execute(null);

            Assert.False(vm.IsGuidedView);
            Assert.Equal("Guided view is for paged reading", Assert.Single(toasts).Title);

            vm.SetReadingModeCommand.Execute(ReadingMode.LeftToRight);
            SetSeriesPageLayoutMode(PageLayoutMode.Double);
            vm.RefreshDisplaySettings();
            toasts.Clear();

            vm.ToggleGuidedViewCommand.Execute(null);

            Assert.False(vm.IsGuidedView);
            Assert.Equal("Guided view needs single-page layout", Assert.Single(toasts).Title);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void GuidedView_DetectsThePanelsOfThePageOnScreen_InTheBackground()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { }) { PanelAnalyzer = (_, _) => TwoPanels };
            vm.LoadIssue(CreateLongIssue());
            Assert.Null(vm.CurrentPagePanels);

            vm.ToggleGuidedViewCommand.Execute(null);
            WaitFor(() => vm.CurrentPagePanels is not null);

            Assert.Same(TwoPanels, vm.CurrentPagePanels);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void GuidedView_PanelsAreClearedOnPageChange_AndCachedForPagesSeenBefore()
    {
        try
        {
            int calls = 0;
            var vm = new ReaderScreenViewModel(goBack: () => { }) { PanelAnalyzer = (_, _) => { Interlocked.Increment(ref calls); return TwoPanels; } };
            vm.LoadIssue(CreateLongIssue());
            vm.ToggleGuidedViewCommand.Execute(null);
            WaitFor(() => vm.CurrentPagePanels is not null);
            Assert.Equal(1, calls);

            vm.GoToPage(1);
            Assert.Null(vm.CurrentPagePanels);                       // the old page's panels never sit on the new page
            WaitFor(() => vm.CurrentPagePanels is not null);
            Assert.Equal(2, calls);

            vm.GoToPage(0);                                          // seen before: straight from the cache, no new analysis
            Assert.NotNull(vm.CurrentPagePanels);
            Assert.Equal(2, calls);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void GuidedView_ALateResultForAPageLeftBehind_IsDropped()
    {
        try
        {
            using var release = new SemaphoreSlim(0);
            var slow = new Paperbunkr.App.Services.Reader.Panels.PagePanels([new Paperbunkr.App.Services.Reader.Panels.PanelRect(0, 0, 0.5, 0.5), new Paperbunkr.App.Services.Reader.Panels.PanelRect(0.5, 0.5, 0.5, 0.5)], true);
            int call = 0;
            var vm = new ReaderScreenViewModel(goBack: () => { })
            {
                PanelAnalyzer = (_, _) =>
                {
                    if (Interlocked.Increment(ref call) == 1)
                    {
                        release.Wait(TimeSpan.FromSeconds(20));   // page 1's analysis is slow
                        return slow;
                    }

                    return TwoPanels;
                },
            };
            vm.LoadIssue(CreateLongIssue());
            vm.ToggleGuidedViewCommand.Execute(null);

            vm.GoToPage(1);                                          // page 2 is now on screen; page 1's result is still pending
            WaitFor(() => vm.CurrentPagePanels is not null);
            Assert.Same(TwoPanels, vm.CurrentPagePanels);

            release.Release();
            WaitFor(() => false);
            Thread.Sleep(100);
            TestDispatcher.Drain();

            Assert.Same(TwoPanels, vm.CurrentPagePanels);            // the late page-1 result did not replace page 2's
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void EnsurePanels_DetectsOnDemand_WhenGuidedViewIsOff()
    {
        try
        {
            int calls = 0;
            var vm = new ReaderScreenViewModel(goBack: () => { }) { PanelAnalyzer = (_, _) => { calls++; return TwoPanels; } };
            vm.LoadIssue(CreateLongIssue());
            Assert.Null(vm.CurrentPagePanels);
            Assert.Equal(0, calls);                                  // nothing is analysed just for turning pages

            vm.EnsurePanelsCommand.Execute(null);
            vm.EnsurePanelsCommand.Execute(null);

            Assert.Same(TwoPanels, vm.CurrentPagePanels);
            Assert.Equal(1, calls);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void GuidedView_PassesTheReadingDirectionToTheDetector()
    {
        try
        {
            bool? sawRtl = null;
            var vm = new ReaderScreenViewModel(goBack: () => { }) { PanelAnalyzer = (_, rtl) => { sawRtl = rtl; return TwoPanels; } };
            vm.LoadIssue(CreateLongIssue());
            vm.SetReadingModeCommand.Execute(ReadingMode.RightToLeft);
            vm.ToggleGuidedViewCommand.Execute(null);
            WaitFor(() => vm.CurrentPagePanels is not null);

            Assert.True(sawRtl);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void GuidedViewOnOpen_And_SmartDoubleClick_ComeFromSettings_AndProfiles()
    {
        try
        {
            SetComfortSettings(s => { s.GuidedViewOnOpen = true; s.SmartDoubleClickZoom = false; });
            var vm = new ReaderScreenViewModel(goBack: () => { });

            vm.LoadIssue(CreateLongIssue());

            Assert.True(vm.IsGuidedView);
            Assert.False(vm.SmartDoubleClickZoom);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void GuidedViewOnOpen_ASessionProfileCanSwitchItOn()
    {
        try
        {
            int profile = CreateProfile("Panels", new ReaderProfileState(GuidedView: true));
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());
            Assert.False(vm.IsGuidedView);

            vm.ApplySessionProfile(profile, "Panels");

            Assert.True(vm.IsGuidedView);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void GuidedView_APreferencesChange_DoesNotUndoAVisitsOwnToggle()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());
            vm.ToggleGuidedViewCommand.Execute(null);
            Assert.True(vm.IsGuidedView);

            vm.RefreshDisplaySettings();                             // an unrelated Preferences change

            Assert.True(vm.IsGuidedView);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void GuidedView_IsARegisteredRemappableCommand_PagedOnly()
    {
        var command = Assert.Single(KeyboardCommandRegistry.Commands, c => c.Id == KeyboardCommandRegistry.ReaderToggleGuidedView);
        Assert.Equal(new KeyGesture(Key.G), command.DefaultGesture);
        Assert.Equal(ConflictContext.Paged, command.Context);
    }

    [Fact]
    public void Palette_OffersGuidedView()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());

            var entry = Assert.Single(vm.BuildPaletteEntries(), e => e.Title == "Guided view (panel by panel)");
            Assert.Equal("G", entry.Shortcut);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    // ===== Comfort (docs/superpowers/specs/2026-09-25-comic-reader-comfort-design.md) =====

    private static void SetComfortSettings(Action<AppSettings> apply)
    {
        using var context = PaperbunkrDb.CreateContext();
        apply(context.GetOrCreateAppSettings());
        context.SaveChanges();
    }

    [Fact]
    public void Hud_StartsHidden_ThenShowsSessionStatsOnceThereIsEnoughData()
    {
        try
        {
            SetComfortSettings(s => s.ShowSessionHud = true);
            var now = new DateTime(2026, 9, 25, 20, 0, 0);
            var vm = new ReaderScreenViewModel(goBack: () => { }) { NowProvider = () => now };
            vm.LoadIssue(CreateLongIssue());
            Assert.True(vm.IsSessionHudVisible);
            vm.SetUserPresent(true);
            vm.OnSessionTick(null, EventArgs.Empty);   // the first tick only sets the clock going

            vm.GoToPage(1);
            vm.GoToPage(2);
            vm.GoToPage(3);
            for (int i = 0; i < 14; i++)               // a little over two active minutes
            {
                now = now.AddSeconds(10);
                vm.NoteReaderInput();
                vm.OnSessionTick(null, EventArgs.Empty);
            }

            Assert.Matches(@"^\d+ min · \d+ pages · \d+\.\d/min · ", vm.SessionHudText);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void Hud_ToggleCommand_FlipsItForTheVisit_AndPreferencesSetTheDefault()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());
            Assert.False(vm.IsSessionHudVisible);

            vm.ToggleSessionHudCommand.Execute(null);
            Assert.True(vm.IsSessionHudVisible);
            Assert.NotEmpty(vm.SessionHudText);

            SetComfortSettings(s => s.ShowSessionHud = true);
            vm.RefreshDisplaySettings();
            Assert.True(vm.IsSessionHudVisible);

            SetComfortSettings(s => s.ShowSessionHud = false);
            vm.RefreshDisplaySettings();
            Assert.False(vm.IsSessionHudVisible);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void Hud_IsRegisteredAsARemappableCommand_WithTheDefaultKeys()
    {
        Assert.Equal(new KeyGesture(Key.H), Assert.Single(KeyboardCommandRegistry.Commands, c => c.Id == KeyboardCommandRegistry.ReaderToggleSessionHud).DefaultGesture);
        Assert.Equal(new KeyGesture(Key.W), Assert.Single(KeyboardCommandRegistry.Commands, c => c.Id == KeyboardCommandRegistry.ReaderToggleWarmShift).DefaultGesture);
        Assert.Equal(new KeyGesture(Key.C, KeyModifiers.Control), Assert.Single(KeyboardCommandRegistry.Commands, c => c.Id == KeyboardCommandRegistry.ReaderCopyPage).DefaultGesture);
    }

    [Fact]
    public void Nudge_RaisesOneActionableToastAfterTheInterval_AndSnoozeClosesIt()
    {
        try
        {
            SetComfortSettings(s => { s.BreakNudgesEnabled = true; s.BreakNudgeIntervalMinutes = 10; });
            var now = new DateTime(2026, 9, 25, 20, 0, 0);
            var vm = new ReaderScreenViewModel(goBack: () => { }) { NowProvider = () => now };
            var shown = new List<ToastRequest>();
            var closed = new List<ToastRequest>();
            vm.ToastRequested += shown.Add;
            vm.ToastCloseRequested += closed.Add;
            vm.LoadIssue(CreateLongIssue());
            vm.SetUserPresent(true);
            vm.OnSessionTick(null, EventArgs.Empty);

            for (int i = 0; i < 61; i++)                // 10 minutes and 10 seconds of steady reading
            {
                now = now.AddSeconds(10);
                vm.NoteReaderInput();
                vm.OnSessionTick(null, EventArgs.Empty);
            }

            var nudge = Assert.Single(shown, t => t.Title == "Time for a break");
            var action = Assert.Single(nudge.Actions!);
            Assert.Equal("Snooze 10 min", action.Label);

            action.Command.Execute(null);

            Assert.Same(nudge, Assert.Single(closed));
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void Nudge_IsOffByDefault_AndNeverFiresWhileTheUserIsAway()
    {
        try
        {
            var now = new DateTime(2026, 9, 25, 20, 0, 0);
            var vm = new ReaderScreenViewModel(goBack: () => { }) { NowProvider = () => now };
            var shown = new List<ToastRequest>();
            vm.ToastRequested += shown.Add;
            vm.LoadIssue(CreateLongIssue());
            vm.SetUserPresent(true);
            for (int i = 0; i < 200; i++)               // long past any interval, but the nudges are off
            {
                now = now.AddSeconds(10);
                vm.NoteReaderInput();
                vm.OnSessionTick(null, EventArgs.Empty);
            }

            Assert.DoesNotContain(shown, t => t.Title == "Time for a break");

            SetComfortSettings(s => { s.BreakNudgesEnabled = true; s.BreakNudgeIntervalMinutes = 10; });
            vm.RefreshDisplaySettings();
            vm.SetUserPresent(false);
            for (int i = 0; i < 200; i++)
            {
                now = now.AddSeconds(10);
                vm.OnSessionTick(null, EventArgs.Empty);
            }

            Assert.DoesNotContain(shown, t => t.Title == "Time for a break");
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void Nudge_AnOpenToastIsClosedWhenTheReaderIsLeft()
    {
        try
        {
            SetComfortSettings(s => { s.BreakNudgesEnabled = true; s.BreakNudgeIntervalMinutes = 10; });
            var now = new DateTime(2026, 9, 25, 20, 0, 0);
            var vm = new ReaderScreenViewModel(goBack: () => { }) { NowProvider = () => now };
            var closed = new List<ToastRequest>();
            vm.ToastCloseRequested += closed.Add;
            vm.LoadIssue(CreateLongIssue());
            vm.SetUserPresent(true);
            vm.OnSessionTick(null, EventArgs.Empty);
            for (int i = 0; i < 61; i++)
            {
                now = now.AddSeconds(10);
                vm.NoteReaderInput();
                vm.OnSessionTick(null, EventArgs.Empty);
            }

            vm.GoBackCommand.Execute(null);

            Assert.Single(closed);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void WarmShift_FollowsTheSchedule_AndTheToggleForcesItForTheVisit()
    {
        try
        {
            SetComfortSettings(s => { s.WarmShiftEnabled = true; s.WarmShiftStartMinutes = 21 * 60; s.WarmShiftEndMinutes = 7 * 60; s.WarmShiftStrength = 50; });
            var now = new DateTime(2026, 9, 25, 22, 0, 0);
            var vm = new ReaderScreenViewModel(goBack: () => { }) { NowProvider = () => now };
            var toasts = new List<ToastRequest>();
            vm.ToastRequested += toasts.Add;
            vm.LoadIssue(CreateLongIssue());
            Assert.Equal(0.5, vm.Warmth, 6);

            now = new DateTime(2026, 9, 26, 12, 0, 0);
            vm.OnSessionTick(null, EventArgs.Empty);
            Assert.Equal(0, vm.Warmth);

            vm.ToggleWarmShiftCommand.Execute(null);      // noon, forced on
            Assert.Equal(0.5, vm.Warmth, 6);
            Assert.Equal("Warm tint on", toasts[^1].Title);

            vm.ToggleWarmShiftCommand.Execute(null);
            Assert.Equal(0, vm.Warmth);
            Assert.Equal("Warm tint off", toasts[^1].Title);

            vm.ToggleWarmShiftCommand.Execute(null);
            vm.GoBackCommand.Execute(null);               // leaving drops the override
            Assert.Equal(0, vm.Warmth);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void WarmShift_IsOffByDefault()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { }) { NowProvider = () => new DateTime(2026, 9, 25, 23, 0, 0) };
            vm.LoadIssue(CreateLongIssue());

            Assert.Equal(0, vm.Warmth);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void WarmShift_ASessionProfileCanSwitchItOnAndSetItsStrength()
    {
        try
        {
            int profile = CreateProfile("Cosy", new ReaderProfileState(WarmShiftEnabled: true, WarmShiftStrength: 80));
            SetComfortSettings(s => { s.WarmShiftStartMinutes = 0; s.WarmShiftEndMinutes = 1439; });
            var vm = new ReaderScreenViewModel(goBack: () => { }) { NowProvider = () => new DateTime(2026, 9, 25, 12, 0, 0) };
            vm.LoadIssue(CreateLongIssue());
            Assert.Equal(0, vm.Warmth);

            vm.ApplySessionProfile(profile, "Cosy");

            Assert.Equal(0.8, vm.Warmth, 6);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public async Task CopyPage_HandsTheDecodedPageToTheClipboard_AndToasts()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            Avalonia.Media.Imaging.Bitmap? copied = null;
            vm.BitmapClipboardWriter = bitmap =>
            {
                copied = bitmap;
                return Task.FromResult(true);
            };
            var toasts = new List<ToastRequest>();
            vm.ToastRequested += toasts.Add;
            vm.LoadIssue(CreateLongIssue());

            await vm.CopyPageCommand.ExecuteAsync(null);

            Assert.NotNull(copied);
            Assert.Equal(64, copied!.PixelSize.Width);
            Assert.Equal(96, copied.PixelSize.Height);
            var toast = Assert.Single(toasts);
            Assert.Equal("Page copied", toast.Title);
            Assert.Equal("Page 1", toast.Message);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public async Task CopyPage_ReportsAnError_WhenTheClipboardIsUnavailable()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { }) { BitmapClipboardWriter = _ => Task.FromResult(false) };
            var toasts = new List<ToastRequest>();
            vm.ToastRequested += toasts.Add;
            vm.LoadIssue(CreateLongIssue());

            await vm.CopyPageCommand.ExecuteAsync(null);

            Assert.Equal(ToastSeverity.Error, Assert.Single(toasts).Severity);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public async Task CopyWhatYouSee_CopiesTheStitchedSpreadWhenAPairIsShowing_AndThePageOtherwise()
    {
        SetSeriesPageLayoutMode(PageLayoutMode.Double);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        var sizes = new List<Avalonia.PixelSize>();
        var titles = new List<string>();
        vm.BitmapClipboardWriter = bitmap =>
        {
            sizes.Add(bitmap.PixelSize);
            return Task.FromResult(true);
        };
        vm.ToastRequested += t => titles.Add(t.Title);
        vm.LoadIssue(_issue3Id);

        await vm.CopyWhatYouSeeCommand.ExecuteAsync(null);      // the cover is alone
        vm.SelectThumbnailCommand.Execute(vm.Thumbnails[1]);
        Assert.True(vm.IsSpreadShowing);
        await vm.CopyWhatYouSeeCommand.ExecuteAsync(null);      // pages 2+3 as a pair
        await vm.CopyPageCommand.ExecuteAsync(null);            // explicitly just page 2

        Assert.Equal(new Avalonia.PixelSize(64, 96), sizes[0]);
        Assert.Equal(new Avalonia.PixelSize(128, 96), sizes[1]);
        Assert.Equal(new Avalonia.PixelSize(64, 96), sizes[2]);
        Assert.Equal(["Page copied", "Spread copied", "Page copied"], titles);
    }

    [Fact]
    public void ContextMenu_OffersSpreadItemsOnlyWhileASpreadIsShowing()
    {
        SetSeriesPageLayoutMode(PageLayoutMode.Double);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue3Id);
        var builder = new ReaderPageContextMenuBuilder(vm);

        var solo = builder.Build(null)!.Select(e => e.Header).ToList();
        vm.SelectThumbnailCommand.Execute(vm.Thumbnails[1]);
        var paired = builder.Build(null)!.Select(e => e.Header).ToList();

        Assert.Contains("Copy Page", solo);
        Assert.DoesNotContain("Copy Spread", solo);
        Assert.DoesNotContain("Save Spread as PNG…", solo);
        Assert.Contains("Copy Spread", paired);
        Assert.Contains("Save Spread as PNG…", paired);
        Assert.Contains("Save Spread as JPEG…", paired);
        Assert.Contains("Save Page as PNG…", paired);
    }

    [Fact]
    public void Palette_OffersTheComfortAndCopyEntries()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());

            var titles = vm.BuildPaletteEntries().Select(e => e.Title).ToList();

            Assert.Contains("Copy page", titles);
            Assert.Contains("Toggle reading stats", titles);
            Assert.Contains("Toggle warm tint", titles);
            Assert.DoesNotContain("Copy spread", titles);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    // ===== Profiles (docs/superpowers/specs/2026-09-25-comic-reader-profiles-design.md) =====

    private static int CreateProfile(string name, ReaderProfileState state) =>
        new WorkspaceService().Create(WorkspaceScreen.Reader, name, ReaderProfileStateJson.Serialize(state)).Id;

    private void PointSeriesAtProfile(int? profileId)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.Series.Find(_seriesId)!.ReaderProfileId = profileId;
        context.SaveChanges();
    }

    [Fact]
    public void Load_AppliesTheSeriesProfile_UnderTheSeriesAndIssueOverrides()
    {
        try
        {
            int profile = CreateProfile("Night", new ReaderProfileState(FitMode: ImageFitMode.FitHeight, Brightness: 10, ImageBackgroundMode: ImageBackgroundMode.Color, BackgroundColor: "#101010", PageMarginEnabled: true, PageMarginPercentWidth: 0.2));
            PointSeriesAtProfile(profile);
            var vm = new ReaderScreenViewModel(goBack: () => { });

            vm.LoadIssue(CreateLongIssue());

            Assert.Equal("Night", vm.ActiveProfileName);
            Assert.Equal(ImageFitMode.FitHeight, vm.FitMode);
            Assert.Equal(10, vm.Brightness);
            Assert.Equal(0.8, vm.PageMarginMultiplier, 3);

            // The series' own fit override still beats a pointer profile.
            using (var context = PaperbunkrDb.CreateContext())
            {
                context.Series.Find(_seriesId)!.PageFitModeOverride = ImageFitMode.FitWidth;
                context.SaveChanges();
            }

            vm.LoadIssue(vm.LoadedIssue!.Id);
            Assert.Equal(ImageFitMode.FitWidth, vm.FitMode);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void SessionProfile_WinsOverSeriesOverrides_WithoutWritingAnyRow()
    {
        try
        {
            int profile = CreateProfile("Webtoon-ish", new ReaderProfileState(FitMode: ImageFitMode.FitHeight, Brightness: 12, Contrast: -4));
            using (var context = PaperbunkrDb.CreateContext())
            {
                context.Series.Find(_seriesId)!.PageFitModeOverride = ImageFitMode.FitWidth;
                context.SaveChanges();
            }

            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());
            Assert.Equal(ImageFitMode.FitWidth, vm.FitMode);

            vm.ApplySessionProfile(profile, "Webtoon-ish");

            Assert.Equal(ImageFitMode.FitHeight, vm.FitMode);
            Assert.Equal(12, vm.Brightness);
            Assert.Equal(-4, vm.Contrast);
            Assert.Equal("Webtoon-ish", vm.ActiveProfileName);
            using var context2 = PaperbunkrDb.CreateContext();
            var issue = context2.Issues.Find(vm.LoadedIssue!.Id)!;
            Assert.Null(issue.PageFitModeOverride);
            Assert.Null(issue.BrightnessOverride);
            Assert.Null(issue.ContrastOverride);
            Assert.Equal(ImageFitMode.FitWidth, context2.Series.Find(_seriesId)!.PageFitModeOverride);
            Assert.Null(context2.Series.Find(_seriesId)!.ReaderProfileId);
            Assert.Null(context2.GetOrCreateAppSettings().DefaultReaderProfileId);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void SessionProfile_SurvivesLoadingAnotherIssue_AndIsClearedByGoBack()
    {
        try
        {
            int profile = CreateProfile("Night", new ReaderProfileState(Brightness: 15));
            bool wentBack = false;
            var vm = new ReaderScreenViewModel(goBack: () => wentBack = true);
            vm.LoadIssue(CreateLongIssue());
            vm.ApplySessionProfile(profile, "Night");

            vm.LoadIssue(_issue1Id);
            Assert.Equal("Night", vm.ActiveProfileName);
            Assert.Equal(15, vm.Brightness);

            vm.GoBackCommand.Execute(null);
            Assert.True(wentBack);

            vm.LoadIssue(_issue1Id);
            Assert.Equal("Standard", vm.ActiveProfileName);
            Assert.Equal(0, vm.Brightness);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void StandardSession_IgnoresTheSeriesPointer_AndRestoresPlainValues()
    {
        try
        {
            int profile = CreateProfile("Night", new ReaderProfileState(Brightness: 15));
            PointSeriesAtProfile(profile);
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());
            Assert.Equal(15, vm.Brightness);

            vm.ApplySessionProfile(ReaderProfileSelector.StandardSessionId, "Standard");

            Assert.Equal("Standard", vm.ActiveProfileName);
            Assert.Equal(0, vm.Brightness);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void ManualAdjustment_AfterASwitch_StillWritesTheIssueOverride_AsADeltaOverTheProfile()
    {
        try
        {
            int profile = CreateProfile("Night", new ReaderProfileState(Brightness: 10));
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());
            vm.ApplySessionProfile(profile, "Night");

            vm.Brightness = 25;

            using var context = PaperbunkrDb.CreateContext();
            Assert.Equal(15f, context.Issues.Find(vm.LoadedIssue!.Id)!.BrightnessOverride);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void SessionProfile_TapZoneChange_ShowsTheZonesBriefly_AndToasts()
    {
        try
        {
            int profile = CreateProfile("Zoned", new ReaderProfileState(PagedTapZoneLayout: TapZoneLayout.LShaped));
            var vm = new ReaderScreenViewModel(goBack: () => { });
            var toasts = new List<ToastRequest>();
            vm.ToastRequested += toasts.Add;
            vm.LoadIssue(CreateLongIssue());

            vm.ApplySessionProfile(profile, "Zoned");

            Assert.Equal(TapZoneLayout.LShaped, vm.PagedTapZoneLayout);
            Assert.True(vm.IsTapZoneFlashVisible);
            Assert.Equal("Profile: Zoned", Assert.Single(toasts).Title);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void NextProfile_CyclesStandardThenEachProfile_ThenBackToStandard()
    {
        try
        {
            new WorkspaceService().EnsureBuiltInsSeeded();
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());
            var seen = new List<string>();

            for (int i = 0; i < 5; i++)
            {
                vm.NextProfileCommand.Execute(null);
                seen.Add(vm.ActiveProfileName);
            }

            Assert.Equal(["Manga night", "Webtoon", "Tablet", "Standard", "Manga night"], seen);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void SaveCurrentAsProfile_CapturesTheLiveLook_AndSwitchesToIt()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());
            vm.FitMode = ImageFitMode.FitHeight;
            vm.Brightness = 7;

            vm.SaveCurrentAsProfileNamed("Mine");

            Assert.Equal("Mine", vm.ActiveProfileName);
            Assert.True(vm.CanUpdateActiveProfile);
            var row = Assert.Single(new WorkspaceService().List(WorkspaceScreen.Reader), r => r.Name == "Mine");
            var state = ReaderProfileStateJson.Deserialize(row.StateJson);
            Assert.Equal(ImageFitMode.FitHeight, state.FitMode);
            Assert.Equal(7, state.Brightness);
            Assert.NotNull(state.ImageBackgroundMode);
            Assert.NotNull(state.PagedTapZoneLayout);

            // Saving under the same name updates rather than duplicating.
            vm.Brightness = 9;
            vm.SaveCurrentAsProfileNamed("mine");
            Assert.Single(new WorkspaceService().List(WorkspaceScreen.Reader), r => r.Name == "Mine");
            Assert.Equal(9, ReaderProfileStateJson.Deserialize(new WorkspaceService().List(WorkspaceScreen.Reader).Single(r => r.Name == "Mine").StateJson).Brightness);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void UpdateActiveProfile_IsOnlyForUserProfiles()
    {
        try
        {
            new WorkspaceService().EnsureBuiltInsSeeded();
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());
            vm.NextProfileCommand.Execute(null);      // a built-in
            Assert.False(vm.CanUpdateActiveProfile);

            vm.Brightness = 33;
            vm.UpdateActiveProfileCommand.Execute(null);

            var builtIn = new WorkspaceService().List(WorkspaceScreen.Reader).First(r => r.Name == vm.ActiveProfileName);
            Assert.NotEqual(33, ReaderProfileStateJson.Deserialize(builtIn.StateJson).Brightness);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void UseProfileForSeries_AndAsDefault_WriteThePointers_AndClearSeriesProfileRemovesIt()
    {
        try
        {
            int profile = CreateProfile("Night", new ReaderProfileState(Brightness: 15));
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());
            vm.ApplySessionProfile(profile, "Night");

            vm.UseProfileForSeriesCommand.Execute(null);
            vm.UseProfileAsDefaultCommand.Execute(null);

            Assert.True(vm.HasSeriesProfile);
            using (var context = PaperbunkrDb.CreateContext())
            {
                Assert.Equal(profile, context.Series.Find(_seriesId)!.ReaderProfileId);
                Assert.Equal(profile, context.GetOrCreateAppSettings().DefaultReaderProfileId);
            }

            vm.ClearSeriesProfileCommand.Execute(null);

            Assert.False(vm.HasSeriesProfile);
            using (var context = PaperbunkrDb.CreateContext())
            {
                Assert.Null(context.Series.Find(_seriesId)!.ReaderProfileId);
            }
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void DefaultProfile_AppliesToASeriesWithNoPointer()
    {
        try
        {
            int profile = CreateProfile("Night", new ReaderProfileState(Brightness: 15));
            using (var context = PaperbunkrDb.CreateContext())
            {
                context.GetOrCreateAppSettings().DefaultReaderProfileId = profile;
                context.SaveChanges();
            }

            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());

            Assert.Equal("Night", vm.ActiveProfileName);
            Assert.Equal(15, vm.Brightness);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void DeletedProfilePointer_FallsBackToPlainSettings()
    {
        try
        {
            PointSeriesAtProfile(9999);
            var vm = new ReaderScreenViewModel(goBack: () => { });

            vm.LoadIssue(CreateLongIssue());

            Assert.Equal("Standard", vm.ActiveProfileName);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void ProfileList_HoldsStandardFirst_AndMarksTheActiveOne()
    {
        try
        {
            new WorkspaceService().EnsureBuiltInsSeeded();
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());
            Assert.Equal(["Standard", "Manga night", "Webtoon", "Tablet"], vm.Profiles.Select(r => r.Name));
            Assert.True(vm.Profiles[0].IsActive);

            vm.Profiles[2].SelectCommand.Execute(null);

            Assert.Equal("Webtoon", vm.ActiveProfileName);
            Assert.True(vm.Profiles[2].IsActive);
            Assert.False(vm.Profiles[0].IsActive);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void NextProfile_IsARegisteredRemappableCommand()
    {
        var command = Assert.Single(KeyboardCommandRegistry.Commands, c => c.Id == KeyboardCommandRegistry.ReaderNextProfile);
        Assert.Equal(new KeyGesture(Key.P), command.DefaultGesture);
    }

    // ===== Reach (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md) =====

    [Fact]
    public void Palette_GoToPage_JumpsThroughTheJumpPath_AndShowsTheBackChip()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());

            vm.Palette.OpenGoToPage();
            vm.Palette.Query = "page 11";
            vm.Palette.ExecuteSelectedCommand.Execute(null);
            TestDispatcher.Drain(); // the entry runs one dispatcher tick after the palette closes

            Assert.False(vm.Palette.IsOpen);
            Assert.Equal("PAGE 11 / 12", vm.PageLabel);
            Assert.True(vm.HasJumpBack);
            Assert.Equal("Back to page 1", vm.JumpBackLabel);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void Palette_EntriesCoverTheReadingActions_WithCurrentShortcuts()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());

            var entries = vm.BuildPaletteEntries();

            Assert.Contains(entries, e => e.Title == "Toggle fullscreen" && e.Shortcut == "F");
            Assert.Contains(entries, e => e.Title == "Next page" && e.Shortcut == "PageDown");
            Assert.Contains(entries, e => e.Title == "Show tap zones");
            Assert.Contains(entries, e => e.Title == "Rate this issue…");
            Assert.Contains(entries, e => e.Title == "Go to page…");
            Assert.Contains(entries, e => e.Title.StartsWith("Reading mode: Right to Left"));
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void Palette_ToggleCommandPalette_OpensAndCloses()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());

            vm.ToggleCommandPaletteCommand.Execute(null);
            Assert.True(vm.Palette.IsOpen);
            vm.ToggleCommandPaletteCommand.Execute(null);
            Assert.False(vm.Palette.IsOpen);

            vm.OpenGoToPageCommand.Execute(null);
            Assert.True(vm.Palette.IsGoToPageMode);
            vm.OpenGoToPageCommand.Execute(null);
            Assert.False(vm.Palette.IsOpen);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void PaletteAndGoToPage_AreRegisteredRemappableCommands()
    {
        var palette = Assert.Single(KeyboardCommandRegistry.Commands, c => c.Id == KeyboardCommandRegistry.ReaderCommandPalette);
        Assert.Equal(new KeyGesture(Key.K, KeyModifiers.Control), palette.DefaultGesture);
        var goTo = Assert.Single(KeyboardCommandRegistry.Commands, c => c.Id == KeyboardCommandRegistry.ReaderGoToPage);
        Assert.Equal(new KeyGesture(Key.G, KeyModifiers.Control), goTo.DefaultGesture);
    }

    [Fact]
    public void Load_ReadsTheReadingOrderKeys_AndTheDefaultInputSettings()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());

            Assert.Equal([new KeyGesture(Key.PageDown), new KeyGesture(Key.Space), new KeyGesture(Key.MediaNextTrack)], vm.NextPageKey);
            Assert.Equal([new KeyGesture(Key.PageUp), new KeyGesture(Key.Space, KeyModifiers.Shift), new KeyGesture(Key.MediaPreviousTrack)], vm.PreviousPageKey);
            Assert.True(vm.ExtraMouseButtonsTurnPages);
            Assert.True(vm.TapZonesForMouse);
            Assert.Equal(TapZoneLayout.Default, vm.PagedTapZoneLayout);
            Assert.Equal(TapZoneLayout.Disabled, vm.ContinuousTapZoneLayout);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void RefreshDisplaySettings_PicksUpTapZoneChangesMadeInPreferences()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());
            using (var context = PaperbunkrDb.CreateContext())
            {
                var settings = context.GetOrCreateAppSettings();
                settings.PagedTapZoneLayout = TapZoneLayout.Edge;
                settings.PagedTapZoneInvert = TapZoneInvert.Horizontal;
                settings.ExtraMouseButtonsTurnPages = false;
                context.SaveChanges();
            }

            vm.RefreshDisplaySettings();

            Assert.Equal(TapZoneLayout.Edge, vm.PagedTapZoneLayout);
            Assert.Equal(TapZoneInvert.Horizontal, vm.PagedTapZoneInvert);
            Assert.False(vm.ExtraMouseButtonsTurnPages);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void ShowTapZoneFlash_ShowsTheActiveLayout_AndExpires()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());

            vm.ShowTapZoneFlash();

            Assert.True(vm.IsTapZoneFlashVisible);
            Assert.Equal(vm.PagedTapZoneLayout, vm.TapZoneFlashLayout);

            vm.OnTapZoneFlashExpired(null, EventArgs.Empty);

            Assert.False(vm.IsTapZoneFlashVisible);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    // ===== Page skipping (docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-design.md 2) =====

    private static void TagPages(int issueId, PageType type, params int[] pages)
    {
        using var context = PaperbunkrDb.CreateContext();
        foreach (int page in pages)
        {
            context.IssuePages.Add(new IssuePage { IssueId = issueId, PageNumber = page, PageType = type });
        }

        context.SaveChanges();
    }

    private static void SetSkipSettings(bool deleted, bool advertisements)
    {
        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();
        settings.SkipDeletedPages = deleted;
        settings.SkipAdvertisementPages = advertisements;
        context.SaveChanges();
    }

    private ReaderScreenViewModel OpenLongIssueWithTags(PageType type, params int[] pages)
    {
        int issueId = CreateLongIssue();
        TagPages(issueId, type, pages);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(issueId);
        return vm;
    }

    [Fact]
    public void NextPage_SkipsADeletedPage_ByDefault_AndShowsTheHint()
    {
        try
        {
            var vm = OpenLongIssueWithTags(PageType.Deleted, 1);

            vm.NextPageCommand.Execute(null);

            Assert.Equal("PAGE 3 / 12", vm.PageLabel);
            Assert.True(vm.HasSkippedPagesHint);
            Assert.Equal("Skipped 1 page", vm.SkippedPagesHint);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void NextPage_SkipsARunOfDeletedPages_AndCountsThem()
    {
        try
        {
            var vm = OpenLongIssueWithTags(PageType.Deleted, 1, 2, 3);

            vm.NextPageCommand.Execute(null);

            Assert.Equal("PAGE 5 / 12", vm.PageLabel);
            Assert.Equal("Skipped 3 pages", vm.SkippedPagesHint);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void NextPage_DoesNotSkipAdvertisements_ByDefault()
    {
        try
        {
            var vm = OpenLongIssueWithTags(PageType.Advertisement, 1);

            vm.NextPageCommand.Execute(null);

            Assert.Equal("PAGE 2 / 12", vm.PageLabel);
            Assert.False(vm.HasSkippedPagesHint);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void NextPage_SkipsAdvertisements_WhenTheSettingIsOn()
    {
        try
        {
            SetSkipSettings(deleted: true, advertisements: true);
            var vm = OpenLongIssueWithTags(PageType.Advertisement, 1);

            vm.NextPageCommand.Execute(null);

            Assert.Equal("PAGE 3 / 12", vm.PageLabel);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void NextPage_DeletedSkippingOff_LandsOnTheDeletedPage()
    {
        try
        {
            SetSkipSettings(deleted: false, advertisements: false);
            var vm = OpenLongIssueWithTags(PageType.Deleted, 1);

            vm.NextPageCommand.Execute(null);

            Assert.Equal("PAGE 2 / 12", vm.PageLabel);
            Assert.False(vm.HasSkippedPagesHint);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void PreviousPage_SkipsBackwardOverDeletedPages()
    {
        try
        {
            var vm = OpenLongIssueWithTags(PageType.Deleted, 3);
            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[4]);
            Assert.Equal("PAGE 5 / 12", vm.PageLabel);

            vm.PreviousPageCommand.Execute(null);

            Assert.Equal("PAGE 3 / 12", vm.PageLabel);
            Assert.Equal("Skipped 1 page", vm.SkippedPagesHint);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void NextPage_EveryPageAheadSkippable_ShowsTheEndCard_AndStaysPut()
    {
        try
        {
            var vm = OpenLongIssueWithTags(PageType.Deleted, 10, 11);
            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[9]);

            vm.NextPageCommand.Execute(null);

            Assert.Equal(ChapterTransitionState.EndCard, vm.ChapterTransitionState);
            Assert.Equal("PAGE 10 / 12", vm.PageLabel);
            Assert.False(vm.HasSkippedPagesHint);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void PreviousPage_EveryPageBehindSkippable_DoesNotMove()
    {
        try
        {
            var vm = OpenLongIssueWithTags(PageType.Deleted, 0, 1);
            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[2]);

            vm.PreviousPageCommand.Execute(null);

            Assert.Equal("PAGE 3 / 12", vm.PageLabel);
            Assert.False(vm.HasSkippedPagesHint);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void ThumbnailJump_LandsOnASkippablePage_WithoutSkipping()
    {
        try
        {
            var vm = OpenLongIssueWithTags(PageType.Deleted, 3);

            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[3]);

            Assert.Equal("PAGE 4 / 12", vm.PageLabel);
            Assert.False(vm.HasSkippedPagesHint);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void SkippedPagesHint_ClearsOnExpiry_AndOnTheNextPageTurn()
    {
        try
        {
            var vm = OpenLongIssueWithTags(PageType.Deleted, 1, 5);

            vm.NextPageCommand.Execute(null);
            Assert.True(vm.HasSkippedPagesHint);
            vm.OnSkippedPagesHintExpired(null, EventArgs.Empty);
            Assert.False(vm.HasSkippedPagesHint);
            Assert.Null(vm.SkippedPagesHint);

            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[0]);
            vm.NextPageCommand.Execute(null);
            Assert.True(vm.HasSkippedPagesHint);
            vm.NextPageCommand.Execute(null);
            Assert.False(vm.HasSkippedPagesHint);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void RetaggingAPage_TakesEffectOnTheNextTurn_WithoutReloading()
    {
        try
        {
            var vm = OpenLongIssueWithTags(PageType.Deleted, 9);

            vm.SetPageTypeDeletedCommand.Execute(vm.Thumbnails[1]);
            vm.NextPageCommand.Execute(null);

            Assert.Equal("PAGE 3 / 12", vm.PageLabel);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    // ===== Continuous-scroll boundary cost (docs/superpowers/specs/2026-09-25-comic-reader-performance-design.md B1/B2) =====

    private static int CountCollectionChanges(ReaderScreenViewModel vm, Action action)
    {
        int changes = 0;
        void Handler(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => changes++;
        vm.Thumbnails.CollectionChanged += Handler;
        try
        {
            action();
        }
        finally
        {
            vm.Thumbnails.CollectionChanged -= Handler;
        }

        return changes;
    }

    private static void AssertExactlyOneSelected(ReaderScreenViewModel vm, int expectedIndex)
    {
        for (int i = 0; i < vm.Thumbnails.Count; i++)
        {
            Assert.Equal(i == expectedIndex, vm.Thumbnails[i].IsSelected);
        }
    }

    [Fact]
    public void PageTurn_ReplacesAtMostTwoThumbnails_AndKeepsExactlyOneSelected()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());

            int changes = CountCollectionChanges(vm, () => vm.NextPageCommand.Execute(null));

            Assert.InRange(changes, 1, 2);
            AssertExactlyOneSelected(vm, 1);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void ContinuousBoundary_ReplacesAtMostTwoThumbnails()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());
            vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);

            int changes = CountCollectionChanges(vm, () => vm.CurrentContinuousPageIndex = 4);

            Assert.InRange(changes, 1, 2);
            AssertExactlyOneSelected(vm, 4);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void ThumbnailJump_ReplacesAtMostTwoThumbnails()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());

            int changes = CountCollectionChanges(vm, () => vm.SelectThumbnailCommand.Execute(vm.Thumbnails[9]));

            Assert.InRange(changes, 1, 2);
            AssertExactlyOneSelected(vm, 9);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    private static void PumpUntil(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            TestDispatcher.Drain();
            if (!condition())
            {
                Thread.Sleep(15);
            }
        }
    }

    [Fact]
    public void DebouncedPositionSave_WritesOffTheUiThread_AndCompletesBackOnIt()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());
            vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);

            int callingThread = Environment.CurrentManagedThreadId;
            var writes = new System.Collections.Concurrent.ConcurrentQueue<(int Issue, int Value, int Thread)>();
            vm.PositionWriter = (issue, value) => writes.Enqueue((issue, value, Environment.CurrentManagedThreadId));

            vm.CurrentContinuousPageIndex = 3;
            vm.FlushPendingPositionSaveInBackground();
            PumpUntil(() => !vm.PositionSaveInFlight && !writes.IsEmpty);

            var write = Assert.Single(writes);
            Assert.Equal(3, write.Value);
            Assert.NotEqual(callingThread, write.Thread);
            Assert.False(vm.PositionSaveInFlight);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void DebouncedPositionSave_WhileAWriteIsRunning_CoalescesToTheNewestPosition()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(CreateLongIssue());
            vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);

            using var gate = new ManualResetEventSlim(false);
            using var started = new ManualResetEventSlim(false);
            var values = new System.Collections.Concurrent.ConcurrentQueue<int>();
            vm.PositionWriter = (_, value) =>
            {
                values.Enqueue(value);
                started.Set();
                gate.Wait(TimeSpan.FromSeconds(5));
            };

            vm.CurrentContinuousPageIndex = 2;
            vm.FlushPendingPositionSaveInBackground();
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

            vm.CurrentContinuousPageIndex = 3;
            vm.FlushPendingPositionSaveInBackground(); // in flight: nothing new starts
            vm.CurrentContinuousPageIndex = 5;
            vm.FlushPendingPositionSaveInBackground();
            Assert.Single(values);

            gate.Set();
            PumpUntil(() => values.Count >= 2 && !vm.PositionSaveInFlight);

            Assert.Equal(new[] { 2, 5 }, values.ToArray());
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void DebouncedPositionSave_AtTheStoryEnd_StoresTheLastPage()
    {
        try
        {
            int issueId = CreateLongIssue();
            TagPages(issueId, PageType.Advertisement, 10, 11);
            var vm = new ReaderScreenViewModel(goBack: () => { });
            vm.LoadIssue(issueId);
            vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);
            var values = new System.Collections.Concurrent.ConcurrentQueue<int>();
            vm.PositionWriter = (_, value) => values.Enqueue(value);

            vm.CurrentContinuousPageIndex = 9; // the story end: pages 10 and 11 are ads
            vm.FlushPendingPositionSaveInBackground();
            PumpUntil(() => !vm.PositionSaveInFlight && !values.IsEmpty);

            Assert.Equal(11, Assert.Single(values));
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void SynchronousFlush_StillWritesImmediately()
    {
        try
        {
            var vm = new ReaderScreenViewModel(goBack: () => { });
            int issueId = CreateLongIssue();
            vm.LoadIssue(issueId);
            vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);

            vm.CurrentContinuousPageIndex = 6;
            vm.FlushPendingPositionSave();

            Assert.Equal(6, PersistedLastPageRead(issueId));
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    // ===== Next-issue pre-open (docs/superpowers/specs/2026-09-25-comic-reader-performance-design.md A) =====

    /// <summary>A 12-page issue numbered 1.5, so series order is 0, 1, 1.5, 2: its next issue is the 2-page issue 2.</summary>
    private int CreateLongIssueBeforeIssue2()
    {
        _longIssuePath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_vm_long_{Guid.NewGuid():N}.cbz");
        CbzFixture.Create(_longIssuePath, pageCount: 12);
        using var context = PaperbunkrDb.CreateContext();
        var issue = new Issue { SeriesId = _seriesId, Number = "1.5", FilePath = _longIssuePath };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    private static void SetPreOpenNextIssue(bool value)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.GetOrCreateAppSettings().PreOpenNextIssue = value;
        context.SaveChanges();
    }

    private static Services.Reader.NextIssueStager NewStager(Func<string, int?, Services.Reader.ReaderImagePipeline?>? open = null) =>
        new(ReaderScreenViewModel.ResolveStagingTarget, open);

    [Fact]
    public async Task Staging_ShortIssue_StagesTheNextIssueOnLoad()
    {
        using var stager = NewStager();
        var vm = new ReaderScreenViewModel(goBack: () => { }) { NextIssueStager = stager };

        vm.LoadIssue(_issue1Id); // 3 pages: position 0 is already within the last 3
        await stager.PendingWork.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(_issue2Id, stager.StagedIssueId);
    }

    [Fact]
    public async Task Staging_LongIssue_OnlyInTheLastThreePages_AndDropsWhenReadingBackOut()
    {
        try
        {
            using var stager = NewStager();
            var vm = new ReaderScreenViewModel(goBack: () => { }) { NextIssueStager = stager };

            vm.LoadIssue(CreateLongIssueBeforeIssue2());
            await stager.PendingWork.WaitAsync(TimeSpan.FromSeconds(60));
            Assert.False(stager.HasStaged); // page 1 of 12: far from the end

            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[8]); // 3 pages remain after page 9: hysteresis band, still nothing
            await stager.PendingWork.WaitAsync(TimeSpan.FromSeconds(60));
            Assert.False(stager.HasStaged);

            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[9]); // 2 remain: the last 3 pages
            await stager.PendingWork.WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal(_issue2Id, stager.StagedIssueId);

            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[2]); // back out of the end zone
            Assert.False(stager.HasStaged);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public async Task Staging_ContinuousScroll_StagesAtTheEndToo()
    {
        try
        {
            using var stager = NewStager();
            var vm = new ReaderScreenViewModel(goBack: () => { }) { NextIssueStager = stager };
            vm.LoadIssue(CreateLongIssueBeforeIssue2());
            vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);
            await stager.PendingWork.WaitAsync(TimeSpan.FromSeconds(60));
            Assert.False(stager.HasStaged);

            vm.CurrentContinuousPageIndex = 10;
            await stager.PendingWork.WaitAsync(TimeSpan.FromSeconds(60));

            Assert.Equal(_issue2Id, stager.StagedIssueId);
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public async Task Load_OfTheStagedIssue_AdoptsTheStagedPipeline()
    {
        Services.Reader.ReaderImagePipeline? opened = null;
        using var stager = NewStager((path, limit) => opened = Services.Reader.ReaderImagePipeline.TryOpen(path, limit));
        var vm = new ReaderScreenViewModel(goBack: () => { }) { NextIssueStager = stager };
        vm.LoadIssue(_issue1Id);
        await stager.PendingWork.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.NotNull(opened);

        vm.LoadIssue(_issue2Id);

        Assert.Same(opened, vm.Decoder);
        Assert.True(opened!.RecordStats);
        Assert.False(stager.HasStaged);
    }

    [Fact]
    public async Task Load_OfAnotherIssue_DiscardsTheStagedPipeline()
    {
        Services.Reader.ReaderImagePipeline? opened = null;
        using var stager = NewStager((path, limit) => opened = Services.Reader.ReaderImagePipeline.TryOpen(path, limit));
        var vm = new ReaderScreenViewModel(goBack: () => { }) { NextIssueStager = stager };
        vm.LoadIssue(_issue1Id);
        await stager.PendingWork.WaitAsync(TimeSpan.FromSeconds(60));

        vm.LoadIssue(_issue3Id);

        Assert.False(stager.HasStaged);
        Assert.NotSame(opened, vm.Decoder);
    }

    [Fact]
    public async Task PreOpenNextIssueOff_NeverStages()
    {
        SetPreOpenNextIssue(false);
        using var stager = NewStager();
        var vm = new ReaderScreenViewModel(goBack: () => { }) { NextIssueStager = stager };

        vm.LoadIssue(_issue1Id);
        await stager.PendingWork.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.False(stager.HasStaged);
    }

    [Fact]
    public async Task TurningThePreOpenSettingOff_WhileReading_DropsTheStagedIssue()
    {
        using var stager = NewStager();
        var vm = new ReaderScreenViewModel(goBack: () => { }) { NextIssueStager = stager };
        vm.LoadIssue(_issue1Id);
        await stager.PendingWork.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(stager.HasStaged);

        SetPreOpenNextIssue(false);
        vm.RefreshDisplaySettings();

        Assert.False(stager.HasStaged);
    }

    [Fact]
    public async Task GoingBack_DiscardsTheStagedIssue()
    {
        using var stager = NewStager();
        var vm = new ReaderScreenViewModel(goBack: () => { }) { NextIssueStager = stager };
        vm.LoadIssue(_issue1Id);
        await stager.PendingWork.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(stager.HasStaged);

        vm.GoBackCommand.Execute(null);

        Assert.False(stager.HasStaged);
    }

    [Fact]
    public async Task TheLastIssueOfASeries_HasNothingToStage()
    {
        using var stager = NewStager();
        var vm = new ReaderScreenViewModel(goBack: () => { }) { NextIssueStager = stager };

        vm.LoadIssue(_issue2Id); // issue 2 is the last in series order
        await stager.PendingWork.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.False(stager.HasStaged);
    }

    [Fact]
    public void WithoutAStager_NothingIsStagedAndNothingBreaks()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.LoadIssue(_issue1Id);
        vm.NextPageCommand.Execute(null);

        Assert.Null(vm.NextIssueStager);
    }

    // ===== Bad-page report (docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-design.md 4) =====

    private static System.Collections.Generic.List<PageReport> Reports()
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.PageReports.OrderBy(r => r.PageNumber).ToList();
    }

    [Fact]
    public void ReportBadPage_OpensThePickerForTheCurrentPage_AndWritesNothingYet()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.NextPageCommand.Execute(null);

        vm.ReportBadPageCommand.Execute(null);

        Assert.True(vm.IsReportPickerOpen);
        Assert.Equal("Report page 2", vm.ReportPickerTitle);
        Assert.Empty(Reports());
    }

    [Fact]
    public void ReportBadPage_FromAThumbnail_TargetsThatPage()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.ReportBadPageCommand.Execute(vm.Thumbnails[2]);

        Assert.Equal("Report page 3", vm.ReportPickerTitle);
    }

    [Fact]
    public void ChoosingAReason_WritesTheReport_ClosesThePicker_AndShowsTheUndoChip()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.ReportBadPageCommand.Execute(vm.Thumbnails[1]);

        vm.ReportPageReasonCommand.Execute(PageReportReason.Blank);

        var report = Assert.Single(Reports());
        Assert.Equal(_issue1Id, report.IssueId);
        Assert.Equal(1, report.PageNumber);
        Assert.Equal(PageReportReason.Blank, report.Reason);
        Assert.False(vm.IsReportPickerOpen);
        Assert.True(vm.HasPageReportChip);
        Assert.Equal("Reported page 2", vm.PageReportChipLabel);
    }

    [Fact]
    public void CancellingThePicker_WritesNothing()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.ReportBadPageCommand.Execute(null);

        vm.CancelReportPickerCommand.Execute(null);

        Assert.False(vm.IsReportPickerOpen);
        Assert.False(vm.HasPageReportChip);
        Assert.Empty(Reports());
    }

    [Fact]
    public void ReportingTheSamePageTwice_UpdatesTheReason()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.ReportBadPageCommand.Execute(null);
        vm.ReportPageReasonCommand.Execute(PageReportReason.Blank);
        vm.ReportBadPageCommand.Execute(null);
        vm.ReportPageReasonCommand.Execute(PageReportReason.Corrupt);

        Assert.Equal(PageReportReason.Corrupt, Assert.Single(Reports()).Reason);
    }

    [Fact]
    public void UndoingAReport_RemovesIt_AndClearsTheChip()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.ReportBadPageCommand.Execute(null);
        vm.ReportPageReasonCommand.Execute(PageReportReason.LowRes);

        vm.UndoPageReportCommand.Execute(null);

        Assert.Empty(Reports());
        Assert.False(vm.HasPageReportChip);
    }

    [Fact]
    public void ReportChip_ClearsOnExpiry_WithoutRemovingTheReport()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.ReportBadPageCommand.Execute(null);
        vm.ReportPageReasonCommand.Execute(PageReportReason.Other);

        vm.OnPageReportChipExpired(null, EventArgs.Empty);

        Assert.False(vm.HasPageReportChip);
        Assert.Single(Reports());
    }

    [Fact]
    public void ReportPageReason_WithoutAnOpenPicker_DoesNothing()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.ReportPageReasonCommand.Execute(PageReportReason.Corrupt);

        Assert.Empty(Reports());
    }

    [Fact]
    public void LoadingAnotherIssue_ClosesAnOpenPickerAndChip()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.ReportBadPageCommand.Execute(null);
        vm.ReportPageReasonCommand.Execute(PageReportReason.Blank);
        vm.ReportBadPageCommand.Execute(null);

        vm.LoadIssue(_issue2Id);

        Assert.False(vm.IsReportPickerOpen);
        Assert.False(vm.HasPageReportChip);
    }

    [Fact]
    public void ReportBadPage_IsARegisteredRemappableCommand_BoundToX()
    {
        var command = Assert.Single(KeyboardCommandRegistry.Commands, c => c.Id == KeyboardCommandRegistry.ReaderReportBadPage);
        Assert.Equal(Key.X, command.DefaultGesture.Key);
        Assert.Equal(KeyModifiers.None, command.DefaultGesture.KeyModifiers);
    }

    // ===== Story-end finish (docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-design.md 3) =====

    private static int? PersistedLastPageRead(int issueId)
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.Issues.Find(issueId)!.LastPageRead;
    }

    private ReaderScreenViewModel OpenLongIssueForFinishTests(RecordingReadingEventRecorder recorder, PageType type, int[] taggedPages, out int issueId)
    {
        issueId = CreateLongIssue();
        TagPages(issueId, type, taggedPages);
        var vm = new ReaderScreenViewModel(() => { }, new KeyBindingService(), recorder);
        vm.LoadIssue(issueId);
        return vm;
    }

    [Fact]
    public void StoryEnd_BeforeTrailingAds_ReachingItMarksTheIssueRead_AndRecordsFinishedOnce()
    {
        try
        {
            var recorder = new RecordingReadingEventRecorder();
            var vm = OpenLongIssueForFinishTests(recorder, PageType.Advertisement, [10, 11], out int issueId);

            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[9]);
            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[8]);
            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[9]);

            Assert.Equal(11, PersistedLastPageRead(issueId));
            Assert.Single(recorder.Calls, c => c.Kind == "Finished" && c.ItemId == issueId);
            Assert.Equal("PAGE 10 / 12", vm.PageLabel); // the reader itself stays where the user is
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void StoryEnd_NotYetReached_KeepsTheRealPosition()
    {
        try
        {
            var recorder = new RecordingReadingEventRecorder();
            var vm = OpenLongIssueForFinishTests(recorder, PageType.Advertisement, [10, 11], out int issueId);

            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[8]);

            Assert.Equal(8, PersistedLastPageRead(issueId));
            Assert.DoesNotContain(recorder.Calls, c => c.Kind == "Finished");
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void StoryEnd_IsTheLastPage_WritesTheNormalPosition()
    {
        try
        {
            var recorder = new RecordingReadingEventRecorder();
            var vm = OpenLongIssueForFinishTests(recorder, PageType.Advertisement, [3], out int issueId);

            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[11]);

            Assert.Equal(11, PersistedLastPageRead(issueId));
            Assert.Single(recorder.Calls, c => c.Kind == "Finished");
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void StoryEnd_MovesWhenAPageIsRetaggedInTheReader()
    {
        try
        {
            var recorder = new RecordingReadingEventRecorder();
            var vm = OpenLongIssueForFinishTests(recorder, PageType.Advertisement, [10, 11], out int issueId);

            vm.SetPageTypeAdvertisementCommand.Execute(vm.Thumbnails[9]); // story end is now page index 8
            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[8]);

            Assert.Equal(11, PersistedLastPageRead(issueId));
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void StoryEnd_EveryPageTagged_FallsBackToTheLastPage()
    {
        try
        {
            var recorder = new RecordingReadingEventRecorder();
            var vm = OpenLongIssueForFinishTests(recorder, PageType.Deleted, [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], out int issueId);

            vm.SelectThumbnailCommand.Execute(vm.Thumbnails[5]);

            Assert.Equal(5, PersistedLastPageRead(issueId));
        }
        finally
        {
            TryDeleteLongIssue();
        }
    }

    [Fact]
    public void Finished_IsRecordedAtTheLastPageOfAShortIssue_UsingTheCeFormula()
    {
        var recorder = new RecordingReadingEventRecorder();
        var vm = new ReaderScreenViewModel(() => { }, new KeyBindingService(), recorder);
        vm.LoadIssue(_issue1Id); // 3 pages: 2/3 = 66% under the old 0-based formula, 100% under CE's

        vm.NextPageCommand.Execute(null);
        Assert.DoesNotContain(recorder.Calls, c => c.Kind == "Finished");

        vm.NextPageCommand.Execute(null);
        Assert.Contains(recorder.Calls, c => c.Kind == "Finished" && c.ItemId == _issue1Id);
    }

    // ===== Context strip (docs/superpowers/specs/2026-09-21-comic-reader-flow-and-defaults-design.md 4) =====

    [Fact]
    public void ContextStrip_OpenedFromAReadingList_ShowsLabelPositionAndNeighbours()
    {
        int listId = CreateReadingList(_issue1Id, _issue4Id, _issue2Id);
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.LoadIssue(_issue4Id, listId);

        Assert.True(vm.HasContextStrip);
        Assert.Equal("Test List \u00b7 2 of 3", vm.ContextStripLabel);
        Assert.True(vm.CanContextStripPrevious);
        Assert.True(vm.CanContextStripNext);
    }

    [Fact]
    public void ContextStrip_NoListAndNoEvent_IsHidden()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.LoadIssue(_issue1Id);

        Assert.False(vm.HasContextStrip);
        Assert.Null(vm.ContextStripLabel);
        Assert.False(vm.IsContextStripVisible);
    }

    [Fact]
    public void ContextStrip_FallsBackToStoryEventMembership_WhenNotOpenedFromAList()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            var evt = new StoryEvent { Name = "Absolute Universe" };
            evt.Members.Add(new EventMembership { IssueId = _issue1Id, Position = 0 });
            evt.Members.Add(new EventMembership { IssueId = _issue2Id, Position = 1 });
            context.StoryEvents.Add(evt);
            context.SaveChanges();
        }

        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        Assert.Equal("Absolute Universe \u00b7 1 of 2", vm.ContextStripLabel);
        Assert.False(vm.CanContextStripPrevious);
        Assert.True(vm.CanContextStripNext);
    }

    [Fact]
    public void ContextStrip_FlashesOnOpen_ThenRidesWithTheChromeOnly()
    {
        int listId = CreateReadingList(_issue1Id, _issue2Id);
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.LoadIssue(_issue1Id, listId);
        Assert.False(vm.ShowChrome);
        Assert.True(vm.IsContextStripFlashing);
        Assert.True(vm.IsContextStripVisible); // flash forces it visible even with the chrome hidden

        vm.OnContextStripFlashTick(null, EventArgs.Empty);
        Assert.False(vm.IsContextStripVisible);

        vm.IsNavigateClusterHovered = true;
        Assert.True(vm.IsContextStripVisible);
    }

    [Fact]
    public void ContextStrip_NextCommand_LoadsTheStripsNeighbour_KeepingTheListAnchor()
    {
        // List order deliberately differs from series order: issue1 -> issue4 (other series) -> issue2.
        int listId = CreateReadingList(_issue1Id, _issue4Id, _issue2Id);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id, listId);

        vm.ContextStripNextCommand.Execute(null);

        Assert.Contains("Other Series", vm.BreadcrumbSeries);
        Assert.Equal("Test List \u00b7 2 of 3", vm.ContextStripLabel);
    }

    // ===== Series-level fit / auto-rotate defaults (docs/superpowers/specs/2026-09-21-comic-reader-flow-and-defaults-design.md §2) =====

    [Fact]
    public void LoadIssue_NoOverrides_UsesSeriesFitDefault_OverGlobal()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Series.Find(_seriesId)!.PageFitModeOverride = ImageFitMode.BestFit;
            context.Series.Find(_seriesId)!.AutoRotateOverride = true;
            context.SaveChanges();
        }

        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        Assert.Equal(ImageFitMode.BestFit, vm.FitMode);
        Assert.True(vm.AutoRotate);
    }

    [Fact]
    public void LoadIssue_IssueOverride_BeatsSeriesDefault()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Series.Find(_seriesId)!.PageFitModeOverride = ImageFitMode.BestFit;
            context.Issues.Find(_issue1Id)!.PageFitModeOverride = ImageFitMode.Original;
            context.SaveChanges();
        }

        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        Assert.Equal(ImageFitMode.Original, vm.FitMode);
    }

    [Fact]
    public void SetFitMode_WritesOnlyTheIssue_ApplyToSeries_WritesTheSeriesAndSiblingsInherit()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.SetFitModeCommand.Execute(ImageFitMode.Fit);
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Null(context.Series.Find(_seriesId)!.PageFitModeOverride);
            Assert.Equal(ImageFitMode.Fit, context.Issues.Find(_issue1Id)!.PageFitModeOverride);
        }

        vm.ApplyFitModeToSeriesCommand.Execute(null);
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal(ImageFitMode.Fit, context.Series.Find(_seriesId)!.PageFitModeOverride);
        }

        var sibling = new ReaderScreenViewModel(goBack: () => { });
        sibling.LoadIssue(_issue2Id);
        Assert.Equal(ImageFitMode.Fit, sibling.FitMode);
    }

    [Fact]
    public void ApplyAutoRotateToSeries_WritesTheCurrentToggleToTheSeries()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.ToggleAutoRotateCommand.Execute(null);

        vm.ApplyAutoRotateToSeriesCommand.Execute(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(vm.AutoRotate, context.Series.Find(_seriesId)!.AutoRotateOverride);
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
    public void LoadIssue_RecordsAnOpenedReadingEvent_ThenReachingTheEndRecordsFinished()
    {
        var recorder = new RecordingReadingEventRecorder();
        var vm = new ReaderScreenViewModel(() => { }, new KeyBindingService(), recorder);

        vm.LoadIssue(_issue4Id); // 1-page issue, last in its own series
        Assert.Contains(recorder.Calls, c => c.Kind == "Opened" && c.ItemId == _issue4Id);

        // Paging past the last page of the last issue in the series is the end-of-book signal.
        vm.NextPageCommand.Execute(null);
        Assert.Contains(recorder.Calls, c => c.Kind == "Finished" && c.ItemId == _issue4Id);
    }

    [Fact]
    public void ReachingTheEnd_AsksTheTrackerAutoSyncService_OncePerSession_ForThatSeries()
    {
        var sync = new RecordingTrackerSync();
        var vm = new ReaderScreenViewModel(() => { }, new KeyBindingService(), new RecordingReadingEventRecorder(), sync);

        vm.LoadIssue(_issue4Id);
        Assert.Empty(sync.Finished);

        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);

        var seriesId = Assert.Single(sync.Finished);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(context.Issues.Find(_issue4Id)!.SeriesId, seriesId);
    }

    private sealed class RecordingTrackerSync : Paperbunkr.App.Services.ITrackerAutoSyncService
    {
        public readonly System.Collections.Generic.List<int> Finished = new();

        public System.Threading.Tasks.Task OnIssueFinishedInReaderAsync(int seriesId)
        {
            Finished.Add(seriesId);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task OnIssuesMarkedReadAsync(System.Collections.Generic.IReadOnlyCollection<int> seriesIds) => System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task<Paperbunkr.App.Services.TrackerPullResult> PullSeriesAsync(int seriesId) =>
            System.Threading.Tasks.Task.FromResult(Paperbunkr.App.Services.TrackerPullResult.None);
    }

    private sealed class RecordingReadingEventRecorder : IReadingEventRecorder
    {
        public readonly System.Collections.Generic.List<(string Kind, int ItemId)> Calls = new();

        public event System.Action? ReadingEventRecorded;

        public void RecordOpened(ReadingItemType itemType, int itemId, int? seriesId, string? publisher, string? primaryGenre)
        {
            Calls.Add(("Opened", itemId));
            ReadingEventRecorded?.Invoke();
        }

        public void RecordFinished(ReadingItemType itemType, int itemId, int? seriesId, string? publisher, string? primaryGenre, int? pagesRead)
        {
            Calls.Add(("Finished", itemId));
            ReadingEventRecorded?.Invoke();
        }

        public void UpdateSessionPages(ReadingItemType itemType, int itemId, int pagesRead)
            => Calls.Add(("SessionPages", itemId));
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
        Assert.Equal(ChapterTransitionState.EndCard, vm.ChapterTransitionState);
        vm.NextPageCommand.Execute(null); // paging forward again while the end card is up means Continue (2026-09-21 design 3)

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
    public void NextPage_PastLastPage_ShowsEndCard_AndDefersTheActualNavigate()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);

        vm.NextPageCommand.Execute(null);

        Assert.Equal(ChapterTransitionState.EndCard, vm.ChapterTransitionState);
        Assert.Equal("Finished \u00b7 #1", vm.EndCardFinishedLabel);
        Assert.True(vm.EndCardHasNext);
        Assert.Equal("#2", vm.EndCardNextLabel);
        Assert.Equal("Series: Test Series \u00b7 3 of 3", vm.EndCardSourceLabel);
        Assert.Equal("Auto in 5s \u00b7 any key cancels", vm.EndCardCountdownText);
        // Navigation waits for Continue or the countdown - still on issue 1.
        Assert.Equal("PAGE 3 / 3", vm.PageLabel);
        Assert.Contains("#1", vm.IssueTitle);
    }

    [Fact]
    public void EndCard_CountdownTicksDown_ThenAdvancesToTheNextIssue()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);

        vm.OnEndCardCountdownTick(null, EventArgs.Empty);
        Assert.Equal("Auto in 4s \u00b7 any key cancels", vm.EndCardCountdownText);
        for (int i = 0; i < 3; i++)
        {
            vm.OnEndCardCountdownTick(null, EventArgs.Empty);
        }

        Assert.Equal(ChapterTransitionState.EndCard, vm.ChapterTransitionState);
        vm.OnEndCardCountdownTick(null, EventArgs.Empty); // 5th tick reaches zero

        Assert.Equal(ChapterTransitionState.Hidden, vm.ChapterTransitionState);
        Assert.Contains("#2", vm.IssueTitle);
    }

    [Fact]
    public void EndCard_CancelCountdown_KeepsTheCardWithoutAdvancing()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);

        vm.CancelEndCardCountdown();
        for (int i = 0; i < 6; i++)
        {
            vm.OnEndCardCountdownTick(null, EventArgs.Empty);
        }

        Assert.Equal(ChapterTransitionState.EndCard, vm.ChapterTransitionState);
        Assert.Null(vm.EndCardCountdownText);
        Assert.Contains("#1", vm.IssueTitle);
    }

    [Fact]
    public void EndCard_ContinueCommand_AdvancesImmediately()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);

        vm.EndCardContinueCommand.Execute(null);

        Assert.Equal(ChapterTransitionState.Hidden, vm.ChapterTransitionState);
        Assert.Contains("#2", vm.IssueTitle);
    }

    [Fact]
    public void EndCard_PagingBack_DismissesTheCard()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);
        Assert.Equal(ChapterTransitionState.EndCard, vm.ChapterTransitionState);

        vm.PreviousPageCommand.Execute(null);

        Assert.Equal(ChapterTransitionState.Hidden, vm.ChapterTransitionState);
        Assert.Equal("PAGE 2 / 3", vm.PageLabel);
    }

    [Fact]
    public void EndCard_OnTheLastIssue_SaysSo_AndHasNothingToContinueTo()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue2Id); // last issue in the series
        vm.NextPageCommand.Execute(null);

        vm.NextPageCommand.Execute(null);

        Assert.Equal(ChapterTransitionState.EndCard, vm.ChapterTransitionState);
        Assert.False(vm.EndCardHasNext);
        Assert.Equal("That's the last issue", vm.EndCardNextLabel);
        Assert.Null(vm.EndCardSourceLabel);
        Assert.Null(vm.EndCardCountdownText);
        vm.EndCardContinueCommand.Execute(null); // no-op
        Assert.Contains("#2", vm.IssueTitle);
    }

    [Fact]
    public void EndCard_FromAReadingList_LabelsTheListAndPosition()
    {
        int listId = CreateReadingList(_issue1Id, _issue4Id, _issue2Id);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id, listId);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);

        vm.NextPageCommand.Execute(null);

        Assert.Equal("Reading list: Test List \u00b7 2 of 3", vm.EndCardSourceLabel);
    }

    [Fact]
    public void EndCard_MarkRead_SetsTheLastPage_AndDisablesItself()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);

        vm.EndCardMarkReadCommand.Execute(null);

        Assert.True(vm.EndCardMarkedRead);
        using var context = PaperbunkrDb.CreateContext();
        Assert.True(context.Issues.Find(_issue1Id)!.LastPageRead >= 2);
    }

    [Fact]
    public void EndCard_Rate_RaisesTheReviewPrompt_EvenWhenTheFinishPromptSettingIsOff()
    {
        var prompted = new List<int>();
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.ReviewPromptRequested += prompted.Add;
        vm.LoadIssue(_issue1Id);

        vm.EndCardRateCommand.Execute(null);

        Assert.Equal(new[] { _issue1Id }, prompted);
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
    public void NextPage_PastLastPage_AutoNavigateDisabled_ShowsTheEndCardWithoutACountdown()
    {
        SetAutoNavigateComics(false);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);

        vm.NextPageCommand.Execute(null);

        Assert.Equal(ChapterTransitionState.EndCard, vm.ChapterTransitionState);
        Assert.True(vm.EndCardHasNext);
        Assert.Null(vm.EndCardCountdownText);
        Assert.Contains("#1", vm.IssueTitle);
    }

    [Fact]
    public void PreviousPage_BeforeFirstPage_RepeatedPresses_WhileCardShowing_AreIgnored()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue2Id);
        vm.PreviousPageCommand.Execute(null); // shows the backward card, defers navigate

        vm.PreviousPageCommand.Execute(null); // re-entrant press while still showing
        vm.PreviousPageCommand.Execute(null);

        // Still just one pending transition - the hold tick lands on issue 1, not further.
        vm.OnChapterTransitionHoldTick(null, EventArgs.Empty);
        Assert.Contains("#1", vm.IssueTitle);
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
        Assert.Equal([new KeyGesture(Key.Left)], vm.PageTurnLeftKey);
        Assert.Equal([new KeyGesture(Key.Right)], vm.PageTurnRightKey);

        SetKeyBinding(KeyboardCommandRegistry.ReaderPageTurnLeft, new KeyGesture(Key.J));
        vm.LoadIssue(_issue1Id);

        Assert.Equal([new KeyGesture(Key.J)], vm.PageTurnLeftKey);
        Assert.Equal([new KeyGesture(Key.Right)], vm.PageTurnRightKey); // untouched
    }

    [Fact]
    public void NewReaderShortcutKeys_DefaultCorrectly_AndReflectRemappingOnLoad()
    {
        // Representative sample across all three UI groups (Navigation/Zoom & Fit/Display) -
        // docs/superpowers/specs/2026-08-16-remappable-reader-shortcuts-design.md.
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        Assert.Equal([new KeyGesture(Key.Left)], vm.PanLeftKey);
        Assert.Equal([new KeyGesture(Key.Z)], vm.ZoomInKey);
        Assert.Equal([new KeyGesture(Key.F)], vm.ToggleFullscreenKey);

        SetKeyBinding(KeyboardCommandRegistry.ReaderPanLeft, new KeyGesture(Key.A));
        SetKeyBinding(KeyboardCommandRegistry.ReaderZoomIn, new KeyGesture(Key.OemComma));
        SetKeyBinding(KeyboardCommandRegistry.ReaderToggleFullscreen, new KeyGesture(Key.OemPeriod));
        vm.LoadIssue(_issue1Id);

        Assert.Equal([new KeyGesture(Key.A)], vm.PanLeftKey);
        Assert.Equal([new KeyGesture(Key.OemComma)], vm.ZoomInKey);
        Assert.Equal([new KeyGesture(Key.OemPeriod)], vm.ToggleFullscreenKey);
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
        vm.EndCardContinueCommand.Execute(null); // forward crossing now goes through the end card (2026-09-21 design 3)

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
    public void ZoomLevel_InContinuousMode_ClampsBelow25Percent()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);

        vm.ZoomLevel = 0.1;

        Assert.Equal(0.25, vm.ZoomLevel);
    }

    [Fact]
    public void ZoomLevel_UsesTheSameSmoothRange_InPagedAndContinuousMode()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);
        vm.ZoomLevel = 0.5;
        Assert.Equal(0.5, vm.ZoomLevel);

        vm.SetReadingModeCommand.Execute(ReadingMode.LeftToRight);
        vm.ZoomLevel = 0.5;

        Assert.Equal(0.5, vm.ZoomLevel);   // one range for both modes (design 2026-09-25 panels-and-zoom section 1)
    }

    [Fact]
    public void ZoomLevel_ClampsBelowMin_To25Percent()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.ZoomLevel = 0.2;

        Assert.Equal(0.25, vm.ZoomLevel);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.37)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.333)]
    [InlineData(2.718)]
    [InlineData(3.99)]
    [InlineData(4.0)]
    public void ZoomLevel_AcceptsEveryValueInTheRange_WithNoSnapping(double zoom)
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.ZoomLevel = zoom;

        Assert.Equal(zoom, vm.ZoomLevel);
    }

    [Fact]
    public void ZoomLevel_AtOrBelow100Percent_ZeroesThePan_AboveItDoesNot()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.ZoomLevel = 2.5;
        vm.PanOffsetX = 40;
        vm.PanOffsetY = -25;

        vm.ZoomLevel = 1.8;
        Assert.Equal(40, vm.PanOffsetX);

        vm.ZoomLevel = 0.6;
        Assert.Equal(0, vm.PanOffsetX);
        Assert.Equal(0, vm.PanOffsetY);
    }

    [Theory]
    [InlineData(0.25, -2.0)]
    [InlineData(0.5, -1.0)]
    [InlineData(1.0, 0.0)]
    [InlineData(2.0, 1.0)]
    [InlineData(4.0, 2.0)]
    public void ZoomSlider_IsTheLogOfTheZoom_AndRoundTrips(double zoom, double slider)
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.ZoomLevel = zoom;

        Assert.Equal(slider, vm.ZoomSlider, 6);

        vm.ZoomLevel = 1.0;
        vm.ZoomSlider = slider;
        Assert.Equal(zoom, vm.ZoomLevel, 6);
    }

    [Fact]
    public void ZoomSlider_RaisesPropertyChanged_WhenTheZoomChanges()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ZoomLevel = 2.0;

        Assert.Contains(nameof(vm.ZoomSlider), changed);
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
        vm.EndCardContinueCommand.Execute(null); // forward crossing now goes through the end card (2026-09-21 design 3)

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
        Assert.Equal("Left to Right", vm.ReadingModeLabel);

        vm.ToggleReadingModeCommand.Execute(null);
        Assert.Equal("Right to Left", vm.ReadingModeLabel);

        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal(ReadingMode.RightToLeft, context.Series.First(s => s.Id == _seriesId).ReadingMode);
        }

        vm.ToggleReadingModeCommand.Execute(null);
        Assert.Equal("Left to Right", vm.ReadingModeLabel);
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

        Assert.Equal("Longstrip (gapped)", vm.ReadingModeLabel);
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

        Assert.Equal("Top to Bottom", vm.ReadingModeLabel);
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

        Assert.Equal("Top to Bottom", vm.ReadingModeLabel);
        Assert.False(vm.IsContinuousMode);
        Assert.Equal(ReadingMode.TopToBottom, vm.EffectiveReadingMode);
    }

    [Fact]
    public void SetReadingModeCommand_HorizontalContinuous_UpdatesLabelAndIsContinuousMode()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.SetReadingModeCommand.Execute(ReadingMode.HorizontalContinuous);

        Assert.Equal("Horizontal Long Strip", vm.ReadingModeLabel);
        Assert.True(vm.IsContinuousMode);
    }

    /// <summary>User direction added after initial testing - a real horizontal RTL mode, not just LTR.</summary>
    [Fact]
    public void SetReadingModeCommand_HorizontalContinuousRightToLeft_UpdatesLabelAndIsContinuousMode()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.SetReadingModeCommand.Execute(ReadingMode.HorizontalContinuousRightToLeft);

        Assert.Equal("Horizontal Long Strip (RTL)", vm.ReadingModeLabel);
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

        Assert.Equal("Long Strip", vm.ReadingModeLabel);
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

        keyBindingService.AddKey(KeyboardCommandRegistry.ReaderRotateClockwise, new KeyGesture(Key.J));
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
    /// tests have no running dispatcher loop), which only works from the thread that bootstrapped
    /// Avalonia. PinnedThreadTestFramework guarantees that, so this used to silently skip itself
    /// when off-thread and no longer does (a skipped pump here would make the test vacuous).
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

    // ===== Texture background (docs/superpowers/specs/2026-09-10-reader-backlog-batch-b-design.md Item 1) =====

    private static void SetBackgroundTexture(string? id)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.GetOrCreateAppSettings().BackgroundTexture = id;
        context.SaveChanges();
    }

    [Fact]
    public void CanvasBackgroundBrush_TextureMode_ReturnsATiledNonStretchedBrush()
    {
        SetImageBackgroundMode(ImageBackgroundMode.Texture);
        SetBackgroundTexture("carbon");
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.LoadIssue(_issue1Id);

        var tileBrush = Assert.IsAssignableFrom<ITileBrush>(vm.CanvasBackgroundBrush);
        Assert.Equal(TileMode.Tile, tileBrush.TileMode);
        Assert.Equal(Stretch.None, tileBrush.Stretch);
    }

    [Fact]
    public void CanvasBackgroundBrush_TextureMode_UnknownId_StillReturnsATiledBrush_ViaTheFallbackTexture()
    {
        SetImageBackgroundMode(ImageBackgroundMode.Texture);
        SetBackgroundTexture("not-a-real-texture-id");
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.LoadIssue(_issue1Id);

        var tileBrush = Assert.IsAssignableFrom<ITileBrush>(vm.CanvasBackgroundBrush);
        Assert.Equal(TileMode.Tile, tileBrush.TileMode);
    }

    [Fact]
    public void RefreshDisplaySettings_TextureMode_SetsShowPageShadow_ButNotInContinuousMode()
    {
        SetImageBackgroundMode(ImageBackgroundMode.Texture);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.RefreshDisplaySettings();
        Assert.True(vm.ShowPageShadow);

        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);
        vm.RefreshDisplaySettings();
        Assert.False(vm.ShowPageShadow);
    }

    [Fact]
    public void RefreshDisplaySettings_ColorMode_ShowPageShadowStaysFalse()
    {
        SetImageBackgroundMode(ImageBackgroundMode.Color);
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.RefreshDisplaySettings();

        Assert.False(vm.ShowPageShadow);
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
    public void ResetZoomCommand_ReturnsToFit_AndItIsTheOnlyPresetLeft()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        vm.ZoomLevel = 3.1;

        vm.ResetZoomCommand.Execute(null);

        Assert.Equal(1.0, vm.ZoomLevel);
        Assert.DoesNotContain(vm.GetType().GetProperties(), p => p.Name.StartsWith("SetZoom"));
    }

    [Fact]
    public void ZoomInZoomOutCommands_AreProportionalSteps_AndClampAtTheRangeEnds()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);

        vm.ZoomInCommand.Execute(null);
        Assert.Equal(1.1, vm.ZoomLevel, 6);

        vm.ZoomOutCommand.Execute(null);
        Assert.Equal(1.0, vm.ZoomLevel, 6);

        for (int i = 0; i < 60; i++)
        {
            vm.ZoomOutCommand.Execute(null);
        }

        Assert.Equal(0.25, vm.ZoomLevel);
        for (int i = 0; i < 80; i++)
        {
            vm.ZoomInCommand.Execute(null);
        }

        Assert.Equal(4.0, vm.ZoomLevel);
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
        vm.EndCardContinueCommand.Execute(null); // forward crossing now goes through the end card (2026-09-21 design 3)

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
        vm.EndCardContinueCommand.Execute(null); // forward crossing now goes through the end card (2026-09-21 design 3)

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
        Assert.Equal(ChapterTransitionState.EndCard, vm.ChapterTransitionState);
        Assert.False(vm.EndCardHasNext); // the list ends here - no fallback to series order
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

    private sealed class FakeBatteryStatusService(BatteryStatusSample? sample) : IBatteryStatusService
    {
        public BatteryStatusSample? GetStatus() => sample;
    }

    [Fact]
    public void LoadIssue_WithBatteryPresent_FormatsPercentageLabel()
    {
        var vm = new ReaderScreenViewModel(() => { }, new KeyBindingService(), new FakeBatteryStatusService(new BatteryStatusSample(72, IsCharging: false)));

        vm.LoadIssue(_issue1Id);

        Assert.Equal("72%", vm.BatteryStatusLabel);
    }

    [Fact]
    public void LoadIssue_WithNoBatteryPresent_LeavesLabelNull()
    {
        var vm = new ReaderScreenViewModel(() => { }, new KeyBindingService(), new FakeBatteryStatusService(null));

        vm.LoadIssue(_issue1Id);

        Assert.Null(vm.BatteryStatusLabel);
    }

    /// <summary>2026-09-16: LoadIssue now leaves ShowChrome false (chrome starts hidden - the
    /// ambient reveal-on-any-movement behavior that used to make "true" the practical starting
    /// state was removed in favor of per-cluster hover, see ShowChrome's own doc comment), so this
    /// test now shows it first before exercising the actual "toggle hides it" behavior it's named
    /// for - previously LoadIssue itself left it true, no extra toggle needed to reach that state.</summary>
    [Fact]
    public void ToggleChromeCommand_WhenChromeShown_HidesIt()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        Assert.False(vm.ShowChrome);
        vm.ToggleChromeCommand.Execute(null);
        Assert.True(vm.ShowChrome);

        vm.ToggleChromeCommand.Execute(null);

        Assert.False(vm.ShowChrome);
    }

    /// <summary>2026-09-16: LoadIssue now leaves ShowChrome false already - no extra toggle needed
    /// to reach "hidden" first, unlike before. See the sibling test's identical note above.</summary>
    [Fact]
    public void ToggleChromeCommand_WhenChromeHidden_ShowsItAgain()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issue1Id);
        Assert.False(vm.ShowChrome);

        vm.ToggleChromeCommand.Execute(null);

        Assert.True(vm.ShowChrome);
    }

    // docs/superpowers/specs/2026-09-14-reader-chrome-redesign-design.md §3/§6 - FitModeLabel and the
    // rail/adjust-section toggles are the only genuinely new bindable surface from that redesign.

    [Theory]
    [InlineData(ImageFitMode.Original, "Original")]
    [InlineData(ImageFitMode.Fit, "Fit Page")]
    [InlineData(ImageFitMode.FitWidth, "Fit Width")]
    [InlineData(ImageFitMode.FitHeight, "Fit Height")]
    [InlineData(ImageFitMode.BestFit, "Best Fit")]
    public void FitModeLabel_ReturnsFriendlyStringForEachFitMode(ImageFitMode mode, string expectedLabel)
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });

        vm.FitMode = mode;

        Assert.Equal(expectedLabel, vm.FitModeLabel);
    }

    [Fact]
    public void ToggleRailCommand_DefaultsClosed_ThenTogglesOpen()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        Assert.False(vm.IsRailOpen);

        vm.ToggleRailCommand.Execute(null);
        Assert.True(vm.IsRailOpen);

        vm.ToggleRailCommand.Execute(null);
        Assert.False(vm.IsRailOpen);
    }

    [Fact]
    public void ToggleAdjustSectionCommand_DefaultsCollapsed_ThenTogglesExpanded()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        Assert.False(vm.IsAdjustSectionExpanded);

        vm.ToggleAdjustSectionCommand.Execute(null);
        Assert.True(vm.IsAdjustSectionExpanded);

        vm.ToggleAdjustSectionCommand.Execute(null);
        Assert.False(vm.IsAdjustSectionExpanded);
    }
}
