using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using System.Linq;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="MainViewModel.EscapeCommand"/> (P5, docs/alpha-roadmap.md) - the single
/// app-wide Esc-to-close/cancel routing, since none of Migration/Issue Properties/Bulk Editing are
/// real Avalonia Windows/Popups with native dialog-Escape behavior. Redirects
/// <see cref="PaperbunkrDbContext.DatabasePathOverride"/> to a temp SQLite file, same approach as
/// <see cref="DetailScreenViewModelTests"/>, since closing the Migration overlay reloads the
/// Library from the database. Joins <see cref="AvaloniaTestCollection"/> since that override is a
/// shared static other test classes also mutate.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class MainViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public MainViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_mainvm_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Escape_NoDialogActive_NoOps()
    {
        var vm = new MainViewModel();

        vm.EscapeCommand.Execute(null);

        // Home is the default launch screen now (docs/superpowers/specs/2026-08-18-home-screen-
        // design.md) - Escape with nothing open should still be a no-op, just on Home instead of
        // the old default of Library.
        Assert.True(vm.IsHome);
    }

    [Fact]
    public void Escape_MigrationOverlayOpen_ClosesIt()
    {
        var vm = new MainViewModel();
        vm.IsMigrationOverlayOpen = true;

        vm.EscapeCommand.Execute(null);

        Assert.False(vm.IsMigrationOverlayOpen);
    }

    /// <summary>
    /// Post-overlay-conversion (docs/superpowers/specs/2026-08-23-issue-editor-borderless-overlay-
    /// design.md): the editor is drawn on top of whatever screen is current rather than switching
    /// <see cref="MainViewModel.CurrentScreen"/> away from it, so closing it no longer needs to
    /// "return" anywhere - Home (the default launch screen) simply stays underneath.
    /// </summary>
    [Fact]
    public void Escape_IssuePropertiesOverlayOpen_ClosesOverlay_LeavingUnderlyingScreenAlone()
    {
        var vm = new MainViewModel();
        vm.IsIssuePropertiesOverlayOpen = true;

        vm.EscapeCommand.Execute(null);

        Assert.False(vm.IsIssueProperties);
        Assert.True(vm.IsHome);
    }

    [Fact]
    public void Escape_BulkIssuePropertiesOverlayOpen_ClosesOverlay_LeavingUnderlyingScreenAlone()
    {
        var vm = new MainViewModel();
        vm.IsBulkIssuePropertiesOverlayOpen = true;

        vm.EscapeCommand.Execute(null);

        Assert.False(vm.IsBulkIssueProperties);
        Assert.True(vm.IsHome);
    }

    /// <summary>
    /// The real reload need <c>GoDetailAfterIssueEdit</c> used to serve (an edited issue's Number/
    /// ContentType making Detail's already-loaded data stale) still has to work once closing the
    /// overlay stopped being a screen-swap - this exercises the real right-click "Edit Properties"
    /// entry point end-to-end (Detail -> overlay -> Escape/Cancel -> reload), not just the flag.
    /// </summary>
    [Fact]
    public void Escape_IssuePropertiesOverlayOpen_AfterOpeningFromDetail_ReloadsThatSeriesAndStaysOnDetail()
    {
        var (seriesId, issueId) = SeedSeriesWithIssue("Overlay Reload Series");
        var vm = new MainViewModel();
        vm.Detail.LoadSeries(seriesId);
        vm.CurrentScreen = "detail";

        vm.Detail.Tabs.EditIssuePropertiesCommand.Execute(new Paperbunkr.App.Models.IssueCardSample
        {
            Id = issueId,
            Title = "#1",
            CoverBrush = Avalonia.Media.Brushes.Gray,
        });
        Assert.True(vm.IsIssuePropertiesOverlayOpen);
        Assert.True(vm.IsDetail);

        vm.EscapeCommand.Execute(null);

        Assert.False(vm.IsIssuePropertiesOverlayOpen);
        Assert.True(vm.IsDetail);
    }

    /// <summary>
    /// Regression test for a real bug: Library loads once at MainViewModel construction and never
    /// again (unlike Smart/Reading, which reload via EnsureListLoaded on every navigation). Data
    /// that lands after construction - a Book Folders scan, a CE migration commit reached via any
    /// path other than the migration overlay's own "X" close button, a direct DB write - never
    /// appeared until GoLibrary itself started reloading too.
    /// </summary>
    [Fact]
    public void GoLibrary_ReloadsFromDatabase_PickingUpDataAddedAfterConstruction()
    {
        var vm = new MainViewModel();
        Assert.Equal(0, vm.Library.AllSeriesCount);

        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using (var context = new PaperbunkrDbContext(options))
        {
            context.Series.Add(new Paperbunkr.Data.Entities.Series { Name = "Late-Arriving Series" });
            context.SaveChanges();
        }

        vm.GoLibraryCommand.Execute(null);

        Assert.Equal(1, vm.Library.AllSeriesCount);
    }

    /// <summary>
    /// P6 follow-up (docs/alpha-todo.md): unlike CE's <c>ComicBookDialog</c> (a true modal that
    /// blocks all other interaction by construction), Issue Properties/Bulk Editing here are just
    /// an overlay within one window, so the rail nav stayed fully clickable mid-edit with no
    /// warning until <see cref="MainViewModel.TryLeaveCurrentEditor"/> was added.
    /// </summary>
    [Fact]
    public void GoLibrary_WithDirtyIssueProperties_PromptsInsteadOfNavigating()
    {
        var vm = new MainViewModel();
        vm.IsIssuePropertiesOverlayOpen = true;
        vm.IssueProperties.Title = "Unsaved Edit";

        vm.GoLibraryCommand.Execute(null);

        Assert.True(vm.IsDiscardConfirmOpen);
        Assert.True(vm.IsIssueProperties);
        Assert.False(vm.IsLibrary);
    }

    [Fact]
    public void GoLibrary_WithCleanIssueProperties_NavigatesImmediately_NoPrompt()
    {
        var vm = new MainViewModel();
        vm.IsIssuePropertiesOverlayOpen = true;

        vm.GoLibraryCommand.Execute(null);

        Assert.False(vm.IsDiscardConfirmOpen);
        Assert.True(vm.IsLibrary);
    }

    [Fact]
    public void ConfirmDiscard_RunsThePendingNavigation()
    {
        var vm = new MainViewModel();
        vm.IsIssuePropertiesOverlayOpen = true;
        vm.IssueProperties.Title = "Unsaved Edit";
        vm.GoLibraryCommand.Execute(null);
        Assert.True(vm.IsDiscardConfirmOpen);

        vm.ConfirmDiscardCommand.Execute(null);

        Assert.False(vm.IsDiscardConfirmOpen);
        Assert.True(vm.IsLibrary);
    }

    [Fact]
    public void CancelDiscard_StaysPut_AndDropsThePendingNavigation()
    {
        var vm = new MainViewModel();
        vm.IsIssuePropertiesOverlayOpen = true;
        vm.IssueProperties.Title = "Unsaved Edit";
        vm.GoLibraryCommand.Execute(null);
        Assert.True(vm.IsDiscardConfirmOpen);

        vm.CancelDiscardCommand.Execute(null);

        Assert.False(vm.IsDiscardConfirmOpen);
        Assert.True(vm.IsIssueProperties);
        Assert.False(vm.IsLibrary);
    }

    [Fact]
    public void GoLibrary_WithDirtyBulkIssueProperties_PromptsInsteadOfNavigating()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        int issueId;
        using (var context = new PaperbunkrDbContext(options))
        {
            var series = new Paperbunkr.Data.Entities.Series { Name = "Test Series" };
            context.Series.Add(series);
            context.SaveChanges();
            var issue = new Paperbunkr.Data.Entities.Issue { SeriesId = series.Id, Number = "1", Publisher = "DC Comics" };
            context.Issues.Add(issue);
            context.SaveChanges();
            issueId = issue.Id;
        }

        var vm = new MainViewModel();
        vm.BulkIssueProperties.Load(new[] { issueId });
        vm.BulkIssueProperties.MainFields.Single(f => f.Label == "Publisher").Value = "Vertigo";
        vm.IsBulkIssuePropertiesOverlayOpen = true;

        vm.GoLibraryCommand.Execute(null);

        Assert.True(vm.IsDiscardConfirmOpen);
        Assert.True(vm.IsBulkIssueProperties);
    }

    // --- Reader back-navigation (real bug found via user testing: Reader's back button used to
    // always hardcode a return to Detail, even when Reader was opened from Library or Home
    // directly - leaving an empty/stale Detail page instead of returning where the user actually
    // came from) ---

    private (int SeriesId, int IssueId) SeedSeriesWithIssue(string seriesName)
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        var series = new Paperbunkr.Data.Entities.Series { Name = seriesName };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Paperbunkr.Data.Entities.Issue { SeriesId = series.Id, Number = "1" };
        context.Issues.Add(issue);
        context.SaveChanges();
        return (series.Id, issue.Id);
    }

    [Fact]
    public void GoReaderForIssue_FromLibrary_BackReturnsToLibrary_NotDetail()
    {
        SeedSeriesWithIssue("Library Series");
        var vm = new MainViewModel();
        vm.GoLibraryCommand.Execute(null);
        var row = Assert.Single(vm.Library.IssueList.Rows);

        vm.Library.IssueList.OpenIssueCommand.Execute(row);
        Assert.True(vm.IsReader);

        vm.Reader.GoBackCommand.Execute(null);

        Assert.True(vm.IsLibrary);
        Assert.False(vm.IsDetail);
    }

    [Fact]
    public void GoReaderForIssue_FromHome_BackReturnsToHome_NotDetail()
    {
        var (_, issueId) = SeedSeriesWithIssue("Home Series");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using (var context = new PaperbunkrDbContext(options))
        {
            var issue = context.Issues.Find(issueId)!;
            issue.LastPageRead = 3;
            issue.PageCount = 10;
            issue.OpenedTime = DateTime.UtcNow;
            context.SaveChanges();
        }

        var vm = new MainViewModel();
        vm.GoHomeCommand.Execute(null);
        var card = Assert.Single(vm.Home.ContinueReading);

        vm.Home.OpenContinueReadingCommand.Execute(card.ResumeIssueId);
        Assert.True(vm.IsReader);

        vm.Reader.GoBackCommand.Execute(null);

        Assert.True(vm.IsHome);
        Assert.False(vm.IsDetail);
    }

    [Fact]
    public void GoReaderForIssue_FromDetail_BackStillReturnsToDetail()
    {
        var (seriesId, _) = SeedSeriesWithIssue("Detail Series");
        var vm = new MainViewModel();
        vm.Detail.LoadSeries(seriesId);
        vm.CurrentScreen = "detail";

        vm.Detail.ContinueCommand.Execute(null);
        Assert.True(vm.IsReader);

        vm.Reader.GoBackCommand.Execute(null);

        Assert.True(vm.IsDetail);
    }

    /// <summary>docs/superpowers/specs/2026-08-24-navigation-shell-motion-system-design.md - rail-order comparison driving the directional slide, pure C# with no Avalonia visual-tree dependency.</summary>
    [Fact]
    public void GoEvents_FromHome_IsForward_NotReversed()
    {
        var vm = new MainViewModel();
        Assert.True(vm.IsHome);

        vm.GoEventsCommand.Execute(null);

        Assert.True(vm.IsEvents);
        Assert.False(vm.IsTransitionReversed);
    }

    [Fact]
    public void GoHome_FromEvents_IsReversed()
    {
        var vm = new MainViewModel();
        vm.GoEventsCommand.Execute(null);
        Assert.True(vm.IsEvents);

        vm.GoHomeCommand.Execute(null);

        Assert.True(vm.IsHome);
        Assert.True(vm.IsTransitionReversed);
    }

    /// <summary>Reader/Detail/etc. aren't in the rail order (drill-down, not a lateral rail move) - IsTransitionReversed is irrelevant for them and must be left as whatever it already was, not silently reset.</summary>
    [Fact]
    public void GoReader_NotInRailOrder_LeavesIsTransitionReversedUnchanged()
    {
        var (seriesId, _) = SeedSeriesWithIssue("Reader Order Series");
        var vm = new MainViewModel();
        // Home -> Events (forward, False) -> Home (reversed, True) - leaves a known True value to
        // check Reader navigation doesn't silently reset.
        vm.GoEventsCommand.Execute(null);
        vm.GoHomeCommand.Execute(null);
        Assert.True(vm.IsTransitionReversed);

        vm.Detail.LoadSeries(seriesId);
        vm.CurrentScreen = "detail";
        vm.Detail.ContinueCommand.Execute(null);

        Assert.True(vm.IsReader);
        Assert.True(vm.IsTransitionReversed);
    }

    /// <summary>docs/superpowers/specs/2026-08-24-navigation-shell-motion-system-design.md - same persisted-preference shape as Phase 1's ReducedMotion.</summary>
    [Fact]
    public void ToggleNavRailPin_PersistsToAppSettings()
    {
        var vm = new MainViewModel();
        Assert.False(vm.NavRailPinned);

        vm.ToggleNavRailPinCommand.Execute(null);

        Assert.True(vm.NavRailPinned);
        Assert.True(vm.IsNavRailExpanded);

        using var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        Assert.True(context.GetOrCreateAppSettings().NavRailPinned);
    }
}
