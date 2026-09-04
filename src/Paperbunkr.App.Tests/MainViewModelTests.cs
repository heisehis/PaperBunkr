using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using System.Linq;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="MainViewModel.EscapeCommand"/> (P5, docs/Paperbunkr-Roadmap.md) - the single
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
        // Goes through the real GoToSeries -> GoDetailForSeries navigation path (unlike the
        // Detail.LoadSeries + raw CurrentScreen poke other tests in this file use) - this test
        // specifically exercises Back (docs/superpowers/specs/2026-08-30-app-shell-navigation-
        // history-design.md), which depends on a real history entry having been pushed; the raw
        // poke bypasses that entirely and isn't representative of how Detail is ever actually
        // reached in production.
        vm.Library.GoToSeriesCommand.Execute(seriesId);
        Assert.True(vm.IsDetail);

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

    /// <summary>The nav rail + bottom status bar bind their visibility to <c>!IsInReader</c> - it must be true on all three reading screens and false everywhere else.</summary>
    [Theory]
    [InlineData("reader", true)]
    [InlineData("bookReader", true)]
    [InlineData("pdfReader", true)]
    [InlineData("library", false)]
    [InlineData("detail", false)]
    [InlineData("home", false)]
    public void IsInReader_TrueOnEveryReadingScreen_FalseElsewhere(string screen, bool expected)
    {
        var vm = new MainViewModel();

        vm.CurrentScreen = screen;

        Assert.Equal(expected, vm.IsInReader);
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

    // --- App shell navigation history: back/forward, breadcrumbs, restore-on-launch, CLI deep-
    // linking (docs/superpowers/specs/2026-08-30-app-shell-navigation-history-design.md) ---

    private int SeedBook(string title)
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        var book = new Paperbunkr.Data.Entities.Book { Title = title };
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private int SeedCollection(string name)
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        var collection = new Paperbunkr.Data.Entities.Collection { Name = name };
        context.Collections.Add(collection);
        context.SaveChanges();
        return collection.Id;
    }

    [Fact]
    public void NavigateBack_InitialState_CanNotGoBack()
    {
        var vm = new MainViewModel();

        Assert.False(vm.CanNavigateBack);
        Assert.False(vm.CanNavigateForward);
    }

    [Fact]
    public void GoDetailForSeries_TwiceInARow_PushesTwoEntries_BackReturnsToFirst()
    {
        var (seriesA, _) = SeedSeriesWithIssue("Series A");
        var (seriesB, _) = SeedSeriesWithIssue("Series B");
        var vm = new MainViewModel();
        vm.GoLibraryCommand.Execute(null);

        vm.Library.GoToSeriesCommand.Execute(seriesA);
        // Already true here - one push means "there's a root (Library) to go back to", not "there
        // are 2+ entries". CanGoBack only turns false once the cursor moves past the root.
        Assert.True(vm.CanNavigateBack);
        vm.Library.GoToSeriesCommand.Execute(seriesB);
        Assert.True(vm.CanNavigateBack);

        vm.NavigateBackCommand.Execute(null);

        Assert.True(vm.IsDetail);
        Assert.Equal("Series A", vm.Detail.HeaderTitle);
        Assert.True(vm.CanNavigateForward);
        // Still true - one more Back() step remains (to Library, the root).
        Assert.True(vm.CanNavigateBack);
    }

    [Fact]
    public void NavigateBack_PastFirstDrillDownEntry_ReturnsToRootScreen()
    {
        var (seriesId, _) = SeedSeriesWithIssue("Root Return Series");
        var vm = new MainViewModel();
        vm.GoLibraryCommand.Execute(null);
        vm.Library.GoToSeriesCommand.Execute(seriesId);
        Assert.True(vm.IsDetail);

        vm.NavigateBackCommand.Execute(null);

        Assert.True(vm.IsLibrary);
        Assert.False(vm.CanNavigateBack);
        Assert.True(vm.CanNavigateForward);
    }

    [Fact]
    public void NavigateForward_AfterBack_ReturnsToTheSameEntry()
    {
        var (seriesId, _) = SeedSeriesWithIssue("Forward Series");
        var vm = new MainViewModel();
        vm.GoLibraryCommand.Execute(null);
        vm.Library.GoToSeriesCommand.Execute(seriesId);
        vm.NavigateBackCommand.Execute(null);
        Assert.True(vm.IsLibrary);

        vm.NavigateForwardCommand.Execute(null);

        Assert.True(vm.IsDetail);
        Assert.Equal("Forward Series", vm.Detail.HeaderTitle);
        Assert.False(vm.CanNavigateForward);
    }

    [Fact]
    public void LateralNavigation_ResetsHistory_EvenWithADrillDownChainInProgress()
    {
        var (seriesId, _) = SeedSeriesWithIssue("Reset Series");
        var vm = new MainViewModel();
        vm.GoLibraryCommand.Execute(null);
        vm.Library.GoToSeriesCommand.Execute(seriesId);
        Assert.True(vm.CanNavigateBack);

        vm.GoBooksCommand.Execute(null);

        Assert.False(vm.CanNavigateBack);
        Assert.False(vm.CanNavigateForward);
    }

    [Fact]
    public void BreadcrumbTrail_ReflectsPushedEntries()
    {
        var (seriesId, _) = SeedSeriesWithIssue("Breadcrumb Series");
        var vm = new MainViewModel();
        vm.GoLibraryCommand.Execute(null);

        vm.Library.GoToSeriesCommand.Execute(seriesId);

        Assert.Equal("Library", vm.RootScreenLabel);
        var entry = Assert.Single(vm.BreadcrumbTrail);
        Assert.Equal(seriesId, entry.EntityId);
        Assert.True(vm.ShowBreadcrumb);
    }

    [Fact]
    public void ShowBreadcrumb_IsFalse_OnLateralScreens()
    {
        var vm = new MainViewModel();

        Assert.False(vm.ShowBreadcrumb);

        vm.GoLibraryCommand.Execute(null);

        Assert.False(vm.ShowBreadcrumb);
    }

    /// <summary>Real on-screen feedback: a persistent breadcrumb bar in Reader fought the immersive,
    /// full-page reading experience (and Reader's own existing "hidden by default, chrome reveals on
    /// hover" convention for the thumbnail rail) in a way it doesn't on the metadata-browsing detail
    /// screens - narrowed <see cref="MainViewModel.ShowBreadcrumb"/> to exclude the three reader
    /// screens after shipping with all six drill-down screens included.</summary>
    [Fact]
    public void ShowBreadcrumb_IsFalse_InReader_EvenThoughItIsADrillDownScreen()
    {
        var (_, issueId) = SeedSeriesWithIssue("No Breadcrumb In Reader Series");
        var vm = new MainViewModel();
        vm.GoLibraryCommand.Execute(null);

        vm.OpenDeepLink(new Paperbunkr.App.Services.NavigationCliTarget("issue", issueId));

        Assert.True(vm.IsReader);
        Assert.False(vm.ShowBreadcrumb);
    }

    [Fact]
    public void OpenDeepLink_Series_NavigatesToDetail()
    {
        var (seriesId, _) = SeedSeriesWithIssue("Deep Link Series");
        var vm = new MainViewModel();

        vm.OpenDeepLink(new Paperbunkr.App.Services.NavigationCliTarget("series", seriesId));

        Assert.True(vm.IsDetail);
        Assert.Equal("Deep Link Series", vm.Detail.HeaderTitle);
    }

    [Fact]
    public void OpenDeepLink_Issue_NavigatesToReader()
    {
        var (_, issueId) = SeedSeriesWithIssue("Deep Link Issue Series");
        var vm = new MainViewModel();

        vm.OpenDeepLink(new Paperbunkr.App.Services.NavigationCliTarget("issue", issueId));

        Assert.True(vm.IsReader);
    }

    [Fact]
    public void OpenDeepLink_Book_NavigatesToBookDetail()
    {
        int bookId = SeedBook("Deep Link Book");
        var vm = new MainViewModel();

        vm.OpenDeepLink(new Paperbunkr.App.Services.NavigationCliTarget("book", bookId));

        Assert.True(vm.IsBookDetail);
    }

    [Fact]
    public void OpenDeepLink_Collection_NavigatesToLibraryWithCollectionSelected()
    {
        int collectionId = SeedCollection("Deep Link Collection");
        var vm = new MainViewModel();

        vm.OpenDeepLink(new Paperbunkr.App.Services.NavigationCliTarget("collection", collectionId));

        Assert.True(vm.IsLibrary);
    }

    [Fact]
    public void RestoreLastScreen_NoPriorSession_DefaultsToHome()
    {
        var vm = new MainViewModel();

        vm.RestoreLastScreen();

        Assert.True(vm.IsHome);
    }

    [Fact]
    public void RestoreLastScreen_LastScreenWasDetailOfDeletedSeries_FallsBackToHome()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using (var context = new PaperbunkrDbContext(options))
        {
            var settings = context.GetOrCreateAppSettings();
            settings.LastScreenKey = "detail";
            settings.LastScreenEntityId = 99999; // never seeded - simulates a deleted series
            context.SaveChanges();
        }

        var vm = new MainViewModel();
        vm.RestoreLastScreen();

        Assert.True(vm.IsHome);
    }

    [Fact]
    public void RestoreLastScreen_LastScreenWasDetailOfExistingSeries_RestoresIt()
    {
        var (seriesId, _) = SeedSeriesWithIssue("Restore Series");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using (var context = new PaperbunkrDbContext(options))
        {
            var settings = context.GetOrCreateAppSettings();
            settings.LastScreenKey = "detail";
            settings.LastScreenEntityId = seriesId;
            context.SaveChanges();
        }

        var vm = new MainViewModel();
        vm.RestoreLastScreen();

        Assert.True(vm.IsDetail);
        Assert.Equal("Restore Series", vm.Detail.HeaderTitle);
    }

    /// <summary>docs/superpowers/specs/2026-09-04-behavior-settings-batch2-design.md §3.1 (CE
    /// Settings.OpenLastFile) - a valid persisted last screen is ignored when the toggle is off.</summary>
    [Fact]
    public void RestoreLastScreen_WithRestoreSessionOnStartupOff_GoesHome_IgnoringPersistedScreen()
    {
        var (seriesId, _) = SeedSeriesWithIssue("Ignored Restore Series");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using (var context = new PaperbunkrDbContext(options))
        {
            var settings = context.GetOrCreateAppSettings();
            settings.LastScreenKey = "detail";
            settings.LastScreenEntityId = seriesId;
            settings.RestoreSessionOnStartup = false;
            context.SaveChanges();
        }

        var vm = new MainViewModel();
        vm.RestoreLastScreen();

        Assert.True(vm.IsHome);
        Assert.False(vm.IsDetail);
    }

    [Fact]
    public void GoDetailForSeries_PersistsLastScreenToAppSettings()
    {
        var (seriesId, _) = SeedSeriesWithIssue("Persist Series");
        var vm = new MainViewModel();
        vm.GoLibraryCommand.Execute(null);

        vm.Library.GoToSeriesCommand.Execute(seriesId);

        using var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var settings = context.GetOrCreateAppSettings();
        Assert.Equal("detail", settings.LastScreenKey);
        Assert.Equal(seriesId, settings.LastScreenEntityId);
    }

    [Fact]
    public void NavigateBack_WithUnsavedIssuePropertiesEdit_PromptsInsteadOfNavigatingImmediately()
    {
        var (seriesId, issueId) = SeedSeriesWithIssue("Guarded Series");
        var vm = new MainViewModel();
        vm.GoLibraryCommand.Execute(null);
        vm.Library.GoToSeriesCommand.Execute(seriesId);
        vm.IssueProperties.Load(issueId);
        vm.IsIssuePropertiesOverlayOpen = true;
        vm.IssueProperties.Title = "Changed Title";
        Assert.True(vm.IssueProperties.HasUnsavedChanges());

        vm.NavigateBackCommand.Execute(null);

        Assert.True(vm.IsDiscardConfirmOpen);
        Assert.True(vm.IsDetail);
    }

    // ===================== First-run onboarding (docs/superpowers/specs/2026-08-31-first-run-
    // onboarding-design.md) =====================

    [Fact]
    public void OpenWelcomeOverlay_SetsIsOpenAndForwardsCeDetected()
    {
        var vm = new MainViewModel();

        vm.OpenWelcomeOverlayCommand.Execute(true);

        Assert.True(vm.IsWelcomeOverlayOpen);
        Assert.True(vm.Welcome.CeInstallDetected);
    }

    [Fact]
    public void CloseWelcomeOverlay_PersistsWelcomeScreenShown_AndOpensTourOfferFirstTimeOnly()
    {
        var vm = new MainViewModel();
        vm.OpenWelcomeOverlayCommand.Execute(false);

        vm.Welcome.SkipCommand.Execute(null);

        Assert.False(vm.IsWelcomeOverlayOpen);
        Assert.True(vm.IsTourOfferOpen);
        using (var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options))
        {
            var settings = context.GetOrCreateAppSettings();
            Assert.True(settings.WelcomeScreenShown);
            Assert.True(settings.WelcomeTourOffered);
        }

        // A second close (e.g. Preferences reopening it someday) must not re-offer the tour.
        vm.IsTourOfferOpen = false;
        vm.OpenWelcomeOverlayCommand.Execute(false);
        vm.Welcome.SkipCommand.Execute(null);

        Assert.False(vm.IsTourOfferOpen);
    }

    [Fact]
    public void TakeTour_ClosesOfferAndOpensWelcomeTour()
    {
        var vm = new MainViewModel();
        vm.IsTourOfferOpen = true;

        vm.TakeTourCommand.Execute(null);

        Assert.False(vm.IsTourOfferOpen);
        Assert.True(vm.IsWelcomeTourOverlayOpen);
        Assert.True(vm.IsHome); // WelcomeTour.Open() navigates to its first step
    }

    [Fact]
    public void DeclineTour_ClosesOfferWithoutOpeningWelcomeTour()
    {
        var vm = new MainViewModel();
        vm.IsTourOfferOpen = true;

        vm.DeclineTourCommand.Execute(null);

        Assert.False(vm.IsTourOfferOpen);
        Assert.False(vm.IsWelcomeTourOverlayOpen);
    }

    [Fact]
    public void Escape_WelcomeOverlayOpen_ClosesIt()
    {
        var vm = new MainViewModel();
        vm.OpenWelcomeOverlayCommand.Execute(false);

        vm.EscapeCommand.Execute(null);

        Assert.False(vm.IsWelcomeOverlayOpen);
    }

    [Fact]
    public void Escape_TourOfferOpen_DeclinesIt()
    {
        var vm = new MainViewModel();
        vm.IsTourOfferOpen = true;

        vm.EscapeCommand.Execute(null);

        Assert.False(vm.IsTourOfferOpen);
    }

    [Fact]
    public void Escape_WelcomeTourOverlayOpen_ClosesIt()
    {
        var vm = new MainViewModel();
        vm.TakeTourCommand.Execute(null);

        vm.EscapeCommand.Execute(null);

        Assert.False(vm.IsWelcomeTourOverlayOpen);
    }

    /// <summary>
    /// Ctrl+Tab/Ctrl+Shift+Tab (docs/superpowers/specs/2026-08-31-app-wide-and-library-keyboard-
    /// shortcuts-design.md) - forward/back through the same 7-screen RailOrder that already drives
    /// the rail nav's slide direction, including wraparound at both ends.
    /// </summary>
    [Fact]
    public void CycleScreenForward_FromHome_GoesToLibrary()
    {
        var vm = new MainViewModel();

        vm.CycleScreenForwardCommand.Execute(null);

        Assert.True(vm.IsLibrary);
    }

    [Fact]
    public void CycleScreenForward_FromLastScreen_WrapsToFirst()
    {
        var vm = new MainViewModel();
        vm.GoPreferencesCommand.Execute(null);

        vm.CycleScreenForwardCommand.Execute(null);

        Assert.True(vm.IsHome);
    }

    [Fact]
    public void CycleScreenBack_FromHome_WrapsToLastScreen()
    {
        var vm = new MainViewModel();

        vm.CycleScreenBackCommand.Execute(null);

        Assert.True(vm.IsPreferences);
    }

    [Fact]
    public void CycleScreenBack_FromLibrary_ReturnsToHome()
    {
        var vm = new MainViewModel();
        vm.GoLibraryCommand.Execute(null);

        vm.CycleScreenBackCommand.Execute(null);

        Assert.True(vm.IsHome);
    }

    [Fact]
    public void CycleScreenForward_OnDrillDownScreen_NoOps()
    {
        var (seriesId, _) = SeedSeriesWithIssue("Series A");
        var vm = new MainViewModel();
        vm.GoLibraryCommand.Execute(null);
        vm.Library.GoToSeriesCommand.Execute(seriesId);
        Assert.True(vm.IsDetail);

        vm.CycleScreenForwardCommand.Execute(null);

        // Detail isn't in RailOrder - cycling top-level views doesn't mean anything from a
        // drill-down screen, so it should stay put rather than guessing a target.
        Assert.True(vm.IsDetail);
    }

    // --- Quick Open command palette (docs/superpowers/specs/2026-09-03-quick-open-command-palette-design.md) ---

    [Fact]
    public void OpenQuickOpen_ShowsTheOverlay_AndPopulatesResults()
    {
        SeedSeriesWithIssue("Palette Series");
        var vm = new MainViewModel();

        vm.OpenQuickOpenCommand.Execute(null);

        Assert.True(vm.IsQuickOpenOverlayOpen);
        Assert.NotEmpty(vm.QuickOpen.Results);
    }

    [Fact]
    public void OpenQuickOpen_NoOps_WhileAnEditorOverlayIsUp()
    {
        var vm = new MainViewModel();
        vm.OpenNewReadingListDialogCommand.Execute(null);
        Assert.True(vm.IsNewReadingListDialogOpen);

        vm.OpenQuickOpenCommand.Execute(null);

        Assert.False(vm.IsQuickOpenOverlayOpen);
    }

    [Fact]
    public void QuickOpen_ActivatingASeries_NavigatesToItsDetailScreen_AndClosesTheOverlay()
    {
        var (seriesId, _) = SeedSeriesWithIssue("Batman Beyond");
        var vm = new MainViewModel();
        vm.OpenQuickOpenCommand.Execute(null);
        vm.QuickOpen.Query = "batman beyond";

        var seriesRow = vm.QuickOpen.Results.First(r => r.Kind == Paperbunkr.App.Models.QuickOpenKind.Series);
        vm.QuickOpen.SelectedIndex = vm.QuickOpen.Results.IndexOf(seriesRow);
        vm.QuickOpen.ActivateSelected();

        Assert.True(vm.IsDetail);
        Assert.False(vm.IsQuickOpenOverlayOpen);
    }

    [Fact]
    public void QuickOpen_ActivatingAScreenRow_NavigatesThere()
    {
        var vm = new MainViewModel();
        vm.OpenQuickOpenCommand.Execute(null);
        vm.QuickOpen.Query = "preferences";

        var row = vm.QuickOpen.Results.First(r => r.Kind == Paperbunkr.App.Models.QuickOpenKind.Screen);
        vm.QuickOpen.SelectedIndex = vm.QuickOpen.Results.IndexOf(row);
        vm.QuickOpen.ActivateSelected();

        Assert.True(vm.IsPreferences);
    }

    [Fact]
    public void QuickOpen_ActivatingAnActionRow_RunsIt()
    {
        var vm = new MainViewModel();
        vm.OpenQuickOpenCommand.Execute(null);
        vm.QuickOpen.Query = "new reading list";

        var row = vm.QuickOpen.Results.First(r => r.Kind == Paperbunkr.App.Models.QuickOpenKind.Action);
        vm.QuickOpen.SelectedIndex = vm.QuickOpen.Results.IndexOf(row);
        vm.QuickOpen.ActivateSelected();

        Assert.True(vm.IsNewReadingListDialogOpen);
    }

    [Fact]
    public void Escape_ClosesQuickOpen()
    {
        var vm = new MainViewModel();
        vm.OpenQuickOpenCommand.Execute(null);
        Assert.True(vm.IsQuickOpenOverlayOpen);

        vm.EscapeCommand.Execute(null);

        Assert.False(vm.IsQuickOpenOverlayOpen);
    }
}
