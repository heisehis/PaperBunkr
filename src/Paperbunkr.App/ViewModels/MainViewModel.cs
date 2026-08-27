using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Drives the app shell's navigation (rail nav, contextual sidebar, active screen).
/// Layout/tokens follow the "Paperbunkr App" wireframe (Claude Design project 43c40b25),
/// default variant: rail nav, pills toolbar, stacked detail layout, separate lists.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    /// <summary>Undo/Redo for metadata edits (docs/ce-feature-inventory.md §A) - the same shared instance <see cref="IssueProperties"/>/<see cref="BulkIssueProperties"/> default to, held here too so the rail nav's Undo/Redo buttons can drive it.</summary>
    private readonly MetadataEditHistoryService _history = MetadataEditHistoryService.Shared;

    /// <summary>
    /// Rail position of each lateral top-level screen (docs/superpowers/specs/2026-08-24-
    /// navigation-shell-motion-system-design.md) - drives <see cref="IsTransitionReversed"/>.
    /// Screens not in this set (Reader/Detail/MangaDetail/BookReader/PdfReader - drill-down, not
    /// lateral rail moves) keep today's instant-cut behavior, deliberately out of scope for this phase.
    /// </summary>
    private static readonly Dictionary<string, int> RailOrder = new()
    {
        ["home"] = 0,
        ["library"] = 1,
        ["books"] = 2,
        ["smart"] = 3,
        ["reading"] = 4,
        ["events"] = 5,
        ["preferences"] = 6,
    };

    public MainViewModel()
    {
        Home = new HomeScreenViewModel(GoDetailForSeries, GoReaderForIssue, GoLibraryWithSearch, GoReaderForIssueInReadingList);
        Library = new LibraryScreenViewModel(GoDetailForSeries, GoReaderForIssue, GoNewIssuePropertiesForPlaceholder, OpenQuickRateOverlay, GoIssuePropertiesForIssue, GoBulkIssuePropertiesForIssues, ShowToast, GoBulkSeriesPropertiesForSeries, GoLibraryFoldersPreferences);
        Books = new BooksScreenViewModel(new FilePickerService(), new BookFolderScanService(), new BookCoverThumbnailService(), GoBookReaderForBook);
        BookReader = new BookReaderScreenViewModel(GoBooks);
        PdfReader = new PdfPageReaderScreenViewModel(GoBooks);
        Detail = new DetailScreenViewModel(GoLibrary, GoReaderForIssue, GoIssuePropertiesForIssue, GoBulkIssuePropertiesForIssues, GoDetailForSeries, GoLibraryWithSearch, OpenQuickRateOverlay);
        MangaDetail = new MangaDetailScreenViewModel(GoLibrary, GoReaderForIssue, GoIssuePropertiesForIssue, GoBulkIssuePropertiesForIssues, GoDetailForSeries, GoLibraryWithSearch);
        var keyBindingService = new KeyBindingService();
        Reader = new ReaderScreenViewModel(GoBackFromReader, keyBindingService);
        IssueProperties = new IssuePropertiesScreenViewModel(CloseIssuePropertiesOverlayAndReload, ShowToast);
        BulkIssueProperties = new BulkIssuePropertiesScreenViewModel(CloseBulkIssuePropertiesOverlayAndReload, ShowToast);
        BulkSeriesProperties = new BulkSeriesPropertiesScreenViewModel(CloseBulkSeriesPropertiesOverlayAndReload);
        Smart = new SmartScreenViewModel(GoDetailForSeries);
        Reading = new ReadingScreenViewModel(new FilePickerService(), GoReaderForIssueInReadingList, OpenReadingListPropertiesOverlay);
        Events = new EventsScreenViewModel();
        Plugin = new PluginScreenViewModel();
        Migration = new MigrationOverlayViewModel(new FilePickerService(), OpenSeriesDetailFromReview);
        ReadingListProperties = new ReadingListPropertiesScreenViewModel(CloseReadingListPropertiesOverlay);
        QuickRate = new QuickRateScreenViewModel(CloseQuickRateOverlay);
        DesignShowcase = new DesignShowcaseScreenViewModel();

        // Live folder-watch scanning (docs/superpowers/specs/
        // 2026-08-23-live-folder-watch-scanning-design.md) - constructed here alongside the app's
        // other manually-composed services (this codebase has no DI container). Its "already-open
        // UI should refresh" responsibility is just re-running Migration.NeedsReview's live query -
        // Library itself already reloads its data on every navigation (an earlier real bug fix), so
        // no separate push-refresh is needed there.
        LiveFolderWatch = new LiveFolderWatchService(ShowToast, () => Migration.NeedsReview.Refresh());
        LiveFolderWatch.Start();

        Preferences = new PreferencesScreenViewModel(
            new SkinService(),
            new FilePickerService(),
            new LibraryFolderScanner(),
            new FileAssociationService(),
            new BackupService(),
            keyBindingService,
            ShowToast,
            Migration,
            Plugin,
            OpenMigrationOverlay,
            ShowProgressToast,
            CloseProgressToast,
            LiveFolderWatch.Reload,
            OpenDesignShowcaseOverlay);

        // Real bug, found via manual testing: Reader.CanvasBackgroundBrush/PageMarginMultiplier
        // (docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md
        // §10) were only ever re-read inside ReaderScreenViewModel.Load - fine for a value read once
        // per book, but background/margin are edited from Preferences while a book may already be
        // open and staying open (the rail-nav screen switcher never destroys/recreates the Reader),
        // so cycling through colors there appeared to "get stuck" on whatever was set the last time
        // Load happened to run. Wired the same way as the toast plumbing above - Preferences raises a
        // plain event, the Reader refreshes its own snapshot in response, no shared mutable state.
        Preferences.ReaderDisplaySettingsChanged += Reader.RefreshDisplaySettings;

        using (var context = PaperbunkrDb.CreateContext())
        {
            _navRailPinned = context.GetOrCreateAppSettings().NavRailPinned;
        }
    }

    /// <summary>
    /// Toast plumbing (P6 follow-up) - kept as a plain event on this shell ViewModel rather than a
    /// singleton/static service, since the actual <c>WindowNotificationManager</c> can only be
    /// created once a real <c>Window</c> exists, which happens after this ViewModel is constructed
    /// (see App.axaml.cs). <see cref="Views.MainWindow"/> subscribes once its own DataContext is set.
    /// </summary>
    public event Action<string, string>? ToastRequested;

    private void ShowToast(string title, string message) => ToastRequested?.Invoke(title, message);

    /// <summary>Plugin-facing entry point for surfacing a broken/failed command (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §8) - same toast, reachable from <c>Paperbunkr.App.Plugins.PluginHostService</c>.</summary>
    public void ShowToastForPlugin(string title, string message) => ShowToast(title, message);

    /// <summary>
    /// Public entry point for <see cref="Views.MainWindow"/>'s minimize-to-tray code-behind to reuse
    /// this class's existing toast plumbing - see docs/superpowers/specs/
    /// 2026-08-23-app-chrome-crash-reporter-and-tray-design.md §4's "first-time explanation."
    /// <see cref="ShowToast"/> itself stays private (matches every other caller here, all internal
    /// navigation callbacks); this is the one intentional external seam.
    /// </summary>
    public void ShowMinimizeToTrayNotice() =>
        ShowToast("Still running", "Paperbunkr is still running in the tray. Right-click the tray icon to exit.");

    /// <summary>
    /// Live progress toast plumbing, same rationale as <see cref="ToastRequested"/> - a long-running
    /// library action (Generate Covers, Sync Metadata) shows one via <see cref="ShowProgressToast"/>,
    /// updates the same <see cref="ToastProgressViewModel"/> instance's Done/Total as it runs (the
    /// shown toast is live-bound to it, so no re-show needed), then calls
    /// <see cref="CloseProgressToast"/> when finished - typically followed by a normal
    /// <see cref="ShowToast"/> completion message.
    /// </summary>
    public event Action<ToastProgressViewModel>? ProgressToastRequested;

    public event Action<ToastProgressViewModel>? ProgressToastCloseRequested;

    private void ShowProgressToast(ToastProgressViewModel toast) => ProgressToastRequested?.Invoke(toast);

    private void CloseProgressToast(ToastProgressViewModel toast) => ProgressToastCloseRequested?.Invoke(toast);

    public HomeScreenViewModel Home { get; }
    public LibraryScreenViewModel Library { get; }
    public BooksScreenViewModel Books { get; }
    public BookReaderScreenViewModel BookReader { get; }
    public PdfPageReaderScreenViewModel PdfReader { get; }
    public DetailScreenViewModel Detail { get; }
    public MangaDetailScreenViewModel MangaDetail { get; }
    public ReaderScreenViewModel Reader { get; }
    public IssuePropertiesScreenViewModel IssueProperties { get; }
    public BulkIssuePropertiesScreenViewModel BulkIssueProperties { get; }

    public BulkSeriesPropertiesScreenViewModel BulkSeriesProperties { get; }
    public SmartScreenViewModel Smart { get; }
    public ReadingScreenViewModel Reading { get; }
    public EventsScreenViewModel Events { get; }
    public PluginScreenViewModel Plugin { get; }
    public PreferencesScreenViewModel Preferences { get; }
    public MigrationOverlayViewModel Migration { get; }
    public ReadingListPropertiesScreenViewModel ReadingListProperties { get; }
    public QuickRateScreenViewModel QuickRate { get; }

    public DesignShowcaseScreenViewModel DesignShowcase { get; }
    public LiveFolderWatchService LiveFolderWatch { get; }

    [ObservableProperty]
    private bool _isMigrationOverlayOpen;

    /// <summary>
    /// Borderless-overlay flags for Issue Properties/Bulk Editing (docs/superpowers/specs/2026-08-23-
    /// issue-editor-borderless-overlay-design.md), same shape as <see cref="IsReadingListPropertiesOverlayOpen"/>
    /// - these editors no longer switch <see cref="CurrentScreen"/> away from whatever's underneath
    /// (Detail/MangaDetail/Library), they're drawn as a dimmed-backdrop panel on top of it instead.
    /// </summary>
    [ObservableProperty]
    private bool _isIssuePropertiesOverlayOpen;

    [ObservableProperty]
    private bool _isBulkIssuePropertiesOverlayOpen;

    [ObservableProperty]
    private bool _isBulkSeriesPropertiesOverlayOpen;

    partial void OnIsIssuePropertiesOverlayOpenChanged(bool value) => OnPropertyChanged(nameof(IsIssueProperties));

    partial void OnIsBulkIssuePropertiesOverlayOpenChanged(bool value) => OnPropertyChanged(nameof(IsBulkIssueProperties));

    partial void OnIsBulkSeriesPropertiesOverlayOpenChanged(bool value) => OnPropertyChanged(nameof(IsBulkSeriesProperties));

    [ObservableProperty]
    private bool _isReadingListPropertiesOverlayOpen;

    [ObservableProperty]
    private bool _isQuickRateOverlayOpen;

    [ObservableProperty]
    private bool _isDesignShowcaseOverlayOpen;

    /// <summary>docs/superpowers/specs/2026-08-24-navigation-shell-motion-system-design.md - transient, set by MainWindow's rail PointerEntered/PointerExited handlers, not persisted.</summary>
    [ObservableProperty]
    private bool _isNavRailHoverExpanded;

    /// <summary>Persisted (AppSettings.NavRailPinned) - loaded in the constructor, saved by <see cref="ToggleNavRailPin"/>. Unlike <see cref="IsNavRailHoverExpanded"/>, pinning reflows the content area (real layout width, not a hover overlay).</summary>
    [ObservableProperty]
    private bool _navRailPinned;

    public bool IsNavRailExpanded => NavRailPinned || IsNavRailHoverExpanded;

    partial void OnIsNavRailHoverExpandedChanged(bool value) => OnPropertyChanged(nameof(IsNavRailExpanded));

    partial void OnNavRailPinnedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNavRailExpanded));

        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();
        settings.NavRailPinned = value;
        context.SaveChanges();
    }

    [RelayCommand]
    private void ToggleNavRailPin() => NavRailPinned = !NavRailPinned;

    /// <summary>Home is the app's launch screen (docs/superpowers/specs/2026-08-18-home-screen-
    /// design.md) - was "library" before this screen existed; Library remains one rail click away,
    /// unchanged otherwise.</summary>
    [ObservableProperty]
    private string _currentScreen = "home";

    /// <summary>docs/superpowers/specs/2026-08-24-navigation-shell-motion-system-design.md - drives the rail nav's directional slide. Set in <see cref="OnCurrentScreenChanging"/>, the only ObservableProperty hook with both the old and new <see cref="CurrentScreen"/> value.</summary>
    [ObservableProperty]
    private bool _isTransitionReversed;

    partial void OnCurrentScreenChanging(string oldValue, string newValue)
    {
        if (RailOrder.TryGetValue(oldValue, out int oldIndex) && RailOrder.TryGetValue(newValue, out int newIndex))
        {
            IsTransitionReversed = newIndex < oldIndex;
        }
    }

    /// <summary>The lateral rail-ordered screen's own ViewModel, or null when a drill-down screen (Reader/Detail/etc.) is active - bound to the shell's single <c>TransitioningContentControl</c>.</summary>
    public object? ActiveScreenContent => CurrentScreen switch
    {
        "home" => Home,
        "library" => Library,
        "books" => Books,
        "smart" => Smart,
        "reading" => Reading,
        "events" => Events,
        "preferences" => Preferences,
        _ => null,
    };

    public bool IsLateralScreen => RailOrder.ContainsKey(CurrentScreen);

    public bool IsHome => CurrentScreen == "home";
    public bool IsLibrary => CurrentScreen == "library";
    public bool IsBooks => CurrentScreen == "books";
    public bool IsBookReader => CurrentScreen == "bookReader";
    public bool IsPdfReader => CurrentScreen == "pdfReader";
    public bool IsDetail => CurrentScreen == "detail";
    public bool IsMangaDetail => CurrentScreen == "mangaDetail";
    public bool IsSmart => CurrentScreen == "smart";
    public bool IsReading => CurrentScreen == "reading";
    public bool IsEvents => CurrentScreen == "events";
    public bool IsPreferences => CurrentScreen == "preferences";
    public bool IsReader => CurrentScreen == "reader";
    /// <summary>Alias, not a distinct concept - kept so <see cref="Escape"/>/<see cref="TryLeaveCurrentEditor"/>
    /// didn't need renaming when this stopped being <see cref="CurrentScreen"/>-backed.</summary>
    public bool IsIssueProperties => IsIssuePropertiesOverlayOpen;

    public bool IsBulkIssueProperties => IsBulkIssuePropertiesOverlayOpen;

    public bool IsBulkSeriesProperties => IsBulkSeriesPropertiesOverlayOpen;

    public bool ShowContextualSidebar => IsLibrary || IsSmart || IsReading || IsEvents;

    partial void OnCurrentScreenChanged(string value)
    {
        OnPropertyChanged(nameof(ActiveScreenContent));
        OnPropertyChanged(nameof(IsLateralScreen));
        OnPropertyChanged(nameof(IsHome));
        OnPropertyChanged(nameof(IsLibrary));
        OnPropertyChanged(nameof(IsBooks));
        OnPropertyChanged(nameof(IsBookReader));
        OnPropertyChanged(nameof(IsPdfReader));
        OnPropertyChanged(nameof(IsDetail));
        OnPropertyChanged(nameof(IsMangaDetail));
        OnPropertyChanged(nameof(IsSmart));
        OnPropertyChanged(nameof(IsReading));
        OnPropertyChanged(nameof(IsEvents));
        OnPropertyChanged(nameof(IsPreferences));
        OnPropertyChanged(nameof(IsReader));
        OnPropertyChanged(nameof(ShowContextualSidebar));
    }

    [RelayCommand]
    private void GoHome() => TryLeaveCurrentEditor(() =>
    {
        Home.LoadFromDatabase();
        CurrentScreen = "home";
    });

    /// <summary>Home's search bar entry point (docs/superpowers/specs/2026-08-18-home-screen-
    /// design.md) - not a rail-nav command itself (no dedicated rail button calls this), just a
    /// callback <see cref="HomeScreenViewModel"/> holds. Sets <see cref="LibraryScreenViewModel.SearchQuery"/>
    /// before navigating, whose own setter already triggers a reload - <see cref="GoLibrary"/>'s own
    /// reload right after is a harmless redundant no-op, matching this codebase's existing tolerance
    /// for that (see <see cref="LibraryScreenViewModel.LoadLibrarySettings"/>'s own doc comment).</summary>
    private void GoLibraryWithSearch(string query) => TryLeaveCurrentEditor(() =>
    {
        Library.SearchQuery = query;
        Library.LoadFromDatabase();
        CurrentScreen = "library";
    });

    [RelayCommand]
    private void GoLibrary() => TryLeaveCurrentEditor(() =>
    {
        // Unlike Smart/Reading's EnsureListLoaded (guarded, load-once), Library genuinely reloads
        // every time - it's the one rail-nav screen with no lazy-load story yet, and the data can
        // change from several places while the user is elsewhere (Book Folders scan, CE migration
        // commit, Generate Covers). Reloading on every visit is simplest fix that covers all of
        // those instead of special-casing each caller to remember to refresh Library itself.
        Library.LoadFromDatabase();
        CurrentScreen = "library";
    });

    [RelayCommand]
    private void GoBooks() => TryLeaveCurrentEditor(() =>
    {
        Books.LoadFromDatabase();
        CurrentScreen = "books";
    });

    [RelayCommand]
    private void GoSmart() => TryLeaveCurrentEditor(() =>
    {
        Smart.EnsureListLoaded();
        CurrentScreen = "smart";
    });

    [RelayCommand]
    private void GoReading() => TryLeaveCurrentEditor(() =>
    {
        Reading.EnsureListLoaded();
        CurrentScreen = "reading";
    });

    [RelayCommand]
    private void GoEvents() => TryLeaveCurrentEditor(() =>
    {
        Events.EnsureEventLoaded();
        CurrentScreen = "events";
    });

    [RelayCommand]
    private void GoPreferences() => TryLeaveCurrentEditor(() =>
    {
        Preferences.EnsureLoaded();
        CurrentScreen = "preferences";
    });

    /// <summary>Library's empty-state "Scan folders" action (docs/superpowers/specs/2026-08-27-
    /// library-browsing-4b-toolbar-rework-design.md §9) - opens Preferences straight to the
    /// Libraries tab where folders are added/scanned.</summary>
    private void GoLibraryFoldersPreferences() => TryLeaveCurrentEditor(() =>
    {
        Preferences.EnsureLoaded();
        Preferences.ActiveTab = "libraries";
        CurrentScreen = "preferences";
    });

    [RelayCommand]
    private void OpenMigrationOverlay()
    {
        Migration.Open();
        IsMigrationOverlayOpen = true;
    }

    [RelayCommand]
    private void CloseMigrationOverlay()
    {
        IsMigrationOverlayOpen = false;
        Library.LoadFromDatabase();
    }

    private void OpenSeriesDetailFromReview(int seriesId)
    {
        IsMigrationOverlayOpen = false;
        GoDetailForSeries(seriesId);
    }

    /// <summary>Entry point wired into <see cref="Reading"/>'s Edit affordance (docs/superpowers/specs/2026-08-23-reading-list-tags-design.md) - same open shape as <see cref="OpenMigrationOverlay"/>.</summary>
    private void OpenReadingListPropertiesOverlay(int readingListId)
    {
        ReadingListProperties.Load(readingListId);
        IsReadingListPropertiesOverlayOpen = true;
    }

    /// <summary>Save/Cancel's shared <c>goBack</c> callback, and the explicit close button's command - reloads the reading list screen either way, same "cheap enough to always refresh" tolerance <see cref="CloseMigrationOverlay"/> already has.</summary>
    [RelayCommand]
    private void CloseReadingListPropertiesOverlay()
    {
        IsReadingListPropertiesOverlayOpen = false;
        Reading.EnsureListLoaded();
    }

    /// <summary>Quick Rating + free-text Review in one popup (docs/ce-feature-inventory.md §A) - entry point wired into <see cref="Library"/>'s right-click "Quick Rate..." menu item.</summary>
    private void OpenQuickRateOverlay(int issueId)
    {
        QuickRate.Load(issueId);
        IsQuickRateOverlayOpen = true;
    }

    /// <summary>Save/Cancel's shared close callback, and the explicit close button's command - reloads Library so a changed rating/review shows up if either is part of the current view (e.g. search matching Review text).</summary>
    [RelayCommand]
    private void CloseQuickRateOverlay()
    {
        IsQuickRateOverlayOpen = false;
        Library.LoadFromDatabase();
    }

    /// <summary>docs/superpowers/specs/2026-08-24-design-language-foundation-design.md "Internal showcase view" - entry point wired into Preferences' debug-only Developer group.</summary>
    private void OpenDesignShowcaseOverlay() => IsDesignShowcaseOverlayOpen = true;

    [RelayCommand]
    private void CloseDesignShowcaseOverlay() => IsDesignShowcaseOverlayOpen = false;

    /// <summary>
    /// Undo/Redo for metadata edits (docs/ce-feature-inventory.md §A) - app-wide rather than scoped
    /// to whichever editor made the edit, since <see cref="MetadataEditHistoryService.Shared"/> is
    /// shared between both. Refreshes whatever's currently showing that could be stale afterward:
    /// Detail/MangaDetail's already-loaded series (same reload <see cref="GoDetailAfterIssueEdit"/>
    /// does) or Library's grid - anything else naturally picks up the change next time it's visited.
    /// </summary>
    [RelayCommand]
    private void Undo()
    {
        string? description = _history.Undo(PaperbunkrDb.CreateContext);
        RefreshAfterHistoryChange();
        ShowToast(description is null ? "Nothing to undo" : "Undone", description ?? "There's no metadata edit left to undo.");
    }

    [RelayCommand]
    private void Redo()
    {
        string? description = _history.Redo(PaperbunkrDb.CreateContext);
        RefreshAfterHistoryChange();
        ShowToast(description is null ? "Nothing to redo" : "Redone", description ?? "There's no undone edit left to redo.");
    }

    private void RefreshAfterHistoryChange()
    {
        if ((IsDetail || IsMangaDetail) && _currentDetailSeriesId is int seriesId)
        {
            GoDetailForSeries(seriesId);
        }
        else if (IsLibrary)
        {
            Library.LoadFromDatabase();
        }
    }

    /// <summary>
    /// Real bug found via user testing: Reader's back button used to always return to Detail
    /// (<c>GoDetail</c>), hardcoded - correct when Reader was entered *from* Detail (its own issue
    /// tiles), but wrong for every other real entry point Reader now has: Library's per-issue
    /// tiles/cards (docs/superpowers/specs/2026-08-18-library-book-centric-redesign-design.md
    /// Slice 3) and Home's Continue Reading/Spotlight/Reading-List-spotlight cards (docs/
    /// superpowers/specs/2026-08-18-home-screen-design.md) all open Reader directly, bypassing
    /// Detail entirely - backing out from any of those landed on a stale or empty Detail page
    /// instead of returning where the user actually came from. Fixed by remembering the screen
    /// that was active right before Reader opened and returning there specifically.
    /// </summary>
    private string _screenBeforeReader = "library";

    /// <summary>Captures <see cref="CurrentScreen"/> just before switching to Reader - guarded
    /// against overwriting itself if already in Reader (e.g. <c>SetReadingMode</c>'s internal
    /// same-issue reload), so the remembered origin always survives any in-Reader navigation that
    /// doesn't itself leave and re-enter Reader from another screen.</summary>
    private void RememberScreenBeforeReader()
    {
        if (CurrentScreen != "reader")
        {
            _screenBeforeReader = CurrentScreen;
        }
    }

    private void GoBackFromReader()
    {
        switch (_screenBeforeReader)
        {
            case "library":
                Library.LoadFromDatabase();
                CurrentScreen = "library";
                break;
            case "home":
                Home.LoadFromDatabase();
                CurrentScreen = "home";
                break;
            case "mangaDetail":
                CurrentScreen = "mangaDetail";
                break;
            default:
                GoDetail();
                break;
        }
    }

    /// <summary>
    /// Guards the seven rail-nav destinations against silently discarding an in-progress Issue
    /// Properties/Bulk Editing edit (P6 follow-up, docs/alpha-todo.md) - CE's equivalent
    /// (<c>ComicBookDialog</c>) is a true modal Windows dialog that blocks all other interaction by
    /// construction, but these screens are just screen-swaps within one window, so without this the
    /// rail nav stays fully clickable mid-edit with no warning. Stashes <paramref name="navigate"/>
    /// and opens the confirm banner instead of running it immediately when the active editor
    /// reports unsaved changes; runs it straight through otherwise. Deliberately NOT applied to
    /// <see cref="Escape"/> - pressing Escape is already an explicit "cancel this" gesture, not an
    /// ambiguous navigation away from it.
    /// </summary>
    private void TryLeaveCurrentEditor(Action navigate)
    {
        bool hasUnsavedChanges = (IsIssueProperties && IssueProperties.HasUnsavedChanges())
            || (IsBulkIssueProperties && BulkIssueProperties.HasUnsavedChanges())
            || (IsBulkSeriesProperties && BulkSeriesProperties.HasUnsavedChanges());

        // Rail-nav destinations only ever set CurrentScreen, which no longer has anything to do with
        // whether an editor overlay is open (docs/superpowers/specs/2026-08-23-issue-editor-
        // borderless-overlay-design.md) - without closing it here explicitly, navigating away with a
        // *clean* (non-dirty) editor open would leave it floating on top of the destination screen.
        // Harmless no-op when neither is open (CommunityToolkit's generated setter skips the
        // notification when the value doesn't change).
        void LeaveAndNavigate()
        {
            IsIssuePropertiesOverlayOpen = false;
            IsBulkIssuePropertiesOverlayOpen = false;
            IsBulkSeriesPropertiesOverlayOpen = false;
            navigate();
        }

        if (!hasUnsavedChanges)
        {
            LeaveAndNavigate();
            return;
        }

        _pendingNavigation = LeaveAndNavigate;
        IsDiscardConfirmOpen = true;
    }

    private Action? _pendingNavigation;

    [ObservableProperty]
    private bool _isDiscardConfirmOpen;

    [RelayCommand]
    private void ConfirmDiscard()
    {
        IsDiscardConfirmOpen = false;
        var navigate = _pendingNavigation;
        _pendingNavigation = null;
        navigate?.Invoke();
    }

    [RelayCommand]
    private void CancelDiscard()
    {
        IsDiscardConfirmOpen = false;
        _pendingNavigation = null;
    }

    private void GoDetail() => CurrentScreen = "detail";

    /// <summary>
    /// The series id most recently routed to either Detail screen (docs/superpowers/specs/
    /// 2026-08-23-manga-detail-screen-design.md) - replaces the old <c>Detail.ReloadCurrentSeries()</c>
    /// round-trip (which only knew how to reload the Western screen) so
    /// <see cref="ReloadDetailAfterIssueEdit"/> can re-route through <see cref="GoDetailForSeries"/>
    /// regardless of which screen the series belongs on, including if the edit itself reclassified it.
    /// </summary>
    private int? _currentDetailSeriesId;

    private static bool IsMangaFamily(ContentType contentType) =>
        contentType is ContentType.Manga or ContentType.Manhua or ContentType.Manhwa;

    private static ContentType LookupContentType(int seriesId)
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.Series.Where(s => s.Id == seriesId).Select(s => s.ContentType).FirstOrDefault();
    }

    /// <summary>Loads <paramref name="seriesId"/> into whichever Detail screen its <see cref="ContentType"/>
    /// routes to, without changing <see cref="CurrentScreen"/> - the shared step behind
    /// <see cref="GoDetailForSeries"/> and <see cref="GoNewIssuePropertiesForPlaceholder"/> (the
    /// latter navigates to Issue Properties next, not either Detail screen directly). Returns
    /// <see langword="true"/> when routed to <see cref="MangaDetail"/>.</summary>
    private bool LoadDetailSeries(int seriesId)
    {
        _currentDetailSeriesId = seriesId;
        bool isManga = IsMangaFamily(LookupContentType(seriesId));
        if (isManga)
        {
            MangaDetail.LoadSeries(seriesId);
        }
        else
        {
            Detail.LoadSeries(seriesId);
        }

        return isManga;
    }

    /// <summary>
    /// Shared close-and-reload step behind both editors' Save/Cancel (docs/superpowers/specs/
    /// 2026-08-23-issue-editor-borderless-overlay-design.md §2) - the properties editor may have
    /// changed the currently-loaded series' data (e.g. an issue's <c>Number</c>, which the Chapters/
    /// Issues tab's tile label is derived from, or its <c>ContentType</c> itself), so Detail needs a
    /// real reload, not just leaving already-stale data on screen underneath the now-closed overlay.
    /// Closing the overlay is no longer "navigating back" - <see cref="CurrentScreen"/> was never
    /// left, so there's nothing to flip back to when no series was ever routed to (e.g. Escape
    /// reached directly in a test).
    /// </summary>
    private void ReloadDetailAfterIssueEdit()
    {
        if (_currentDetailSeriesId is int seriesId)
        {
            GoDetailForSeries(seriesId);
        }
    }

    private void CloseIssuePropertiesOverlayAndReload()
    {
        IsIssuePropertiesOverlayOpen = false;
        ReloadDetailAfterIssueEdit();
    }

    private void CloseBulkIssuePropertiesOverlayAndReload()
    {
        IsBulkIssuePropertiesOverlayOpen = false;
        ReloadDetailAfterIssueEdit();
    }

    /// <summary>
    /// Series-level counterpart (docs/superpowers/specs/2026-08-24-library-multiselect-slice3-design.md)
    /// - unlike <see cref="CloseBulkIssuePropertiesOverlayAndReload"/>, this reloads Library directly
    /// rather than routing through <see cref="ReloadDetailAfterIssueEdit"/>: series-level bulk edit
    /// is only ever reached from Library's own series-card selection, never from Detail.
    /// </summary>
    private void CloseBulkSeriesPropertiesOverlayAndReload()
    {
        IsBulkSeriesPropertiesOverlayOpen = false;
        Library.LoadFromDatabase();
        Library.ClearSeriesSelectionCommand.Execute(null);
    }

    private void GoDetailForSeries(int seriesId) => CurrentScreen = LoadDetailSeries(seriesId) ? "mangaDetail" : "detail";

    /// <summary>
    /// Manual "add a physical book" hand-off (docs/superpowers/specs/2026-08-16-reveal-in-explorer-
    /// and-fileless-entries-design.md §2/§3) - routes to Detail's target series first (now a real
    /// navigation, not just a data pre-load - the overlay sits on top of whatever screen is current,
    /// so this is what actually puts Detail underneath it), then opens the editor overlay on top.
    /// </summary>
    private void GoNewIssuePropertiesForPlaceholder(int issueId, int seriesId, bool deleteIfUnedited)
    {
        GoDetailForSeries(seriesId);
        IssueProperties.Load(issueId, deleteIfUnedited);
        IsIssuePropertiesOverlayOpen = true;
    }

    private void GoReaderForIssue(int issueId)
    {
        RememberScreenBeforeReader();
        Reader.LoadIssue(issueId);
        CurrentScreen = "reader";
    }

    /// <summary>Plugin-facing entry point for <c>Paperbunkr.Plugins.Automation.IOpenBooksManager.Open</c> (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §4) - same navigation as <see cref="GoReaderForIssue"/>, just reachable from outside this ViewModel.</summary>
    public void OpenReaderForPlugin(int issueId) => GoReaderForIssue(issueId);

    /// <summary>True only when <paramref name="issueId"/> is the book currently shown by the Reader screen - backs <c>IOpenBooksManager.IsOpen</c>.</summary>
    public bool IsIssueOpenInReaderForPlugin(int issueId) => CurrentScreen == "reader" && Reader.LoadedIssue?.Id == issueId;

    /// <summary>
    /// Same as <see cref="GoReaderForIssue"/> but anchors the Reader to a reading list's own order
    /// (docs/superpowers/specs/2026-08-23-cbl-manager-manual-editing-and-list-aware-reading-
    /// design.md §3) - used only by entry points that already know which list an issue was opened
    /// from (Reading Lists' click-to-read, Home's "Try This Reading List" card), so the far more
    /// common plain <see cref="GoReaderForIssue"/> stays untouched everywhere else.
    /// </summary>
    private void GoReaderForIssueInReadingList(int issueId, int readingListId)
    {
        RememberScreenBeforeReader();
        Reader.LoadIssue(issueId, readingListId);
        CurrentScreen = "reader";
    }

    private void GoBookReaderForBook(int bookId, BookFormat format)
    {
        if (format == BookFormat.Pdf)
        {
            PdfReader.LoadBook(bookId);
            CurrentScreen = "pdfReader";
        }
        else
        {
            BookReader.LoadBook(bookId);
            CurrentScreen = "bookReader";
        }
    }

    private void GoIssuePropertiesForIssue(int issueId)
    {
        IssueProperties.Load(issueId);
        IsIssuePropertiesOverlayOpen = true;
    }

    private void GoBulkIssuePropertiesForIssues(IReadOnlyList<int> issueIds)
    {
        BulkIssueProperties.Load(issueIds);
        IsBulkIssuePropertiesOverlayOpen = true;
    }

    private void GoBulkSeriesPropertiesForSeries(IReadOnlyList<int> seriesIds)
    {
        BulkSeriesProperties.Load(seriesIds);
        IsBulkSeriesPropertiesOverlayOpen = true;
    }

    /// <summary>Corner "X" button's command (docs/superpowers/specs/2026-08-23-issue-editor-
    /// borderless-overlay-design.md §3) - delegates to the same Cancel path the editor's own inline
    /// Cancel button already uses, rather than duplicating the close+reload logic here.</summary>
    [RelayCommand]
    private void CloseIssuePropertiesOverlay() => IssueProperties.CancelCommand.Execute(null);

    [RelayCommand]
    private void CloseBulkIssuePropertiesOverlay() => BulkIssueProperties.CancelCommand.Execute(null);

    [RelayCommand]
    private void CloseBulkSeriesPropertiesOverlay() => BulkSeriesProperties.CancelCommand.Execute(null);

    /// <summary>
    /// Esc-to-close/cancel (P5, docs/alpha-roadmap.md), routed here rather than per-screen
    /// KeyDown handlers so there's exactly one place that knows what "the current dialog" is -
    /// none of Migration/Issue Properties/Bulk Editing are real Avalonia Windows/Popups (they're
    /// all overlays within the single MainWindow), so there's no native
    /// dialog-Escape behavior to inherit. Preferences has no Cancel concept (every toggle
    /// persists immediately, docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md)
    /// so it's deliberately not in this list - Esc there would have nothing meaningful to cancel.
    /// </summary>
    [RelayCommand]
    private void Escape()
    {
        if (IsMigrationOverlayOpen)
        {
            CloseMigrationOverlay();
        }
        else if (IsReadingListPropertiesOverlayOpen)
        {
            ReadingListProperties.CancelCommand.Execute(null);
        }
        else if (IsQuickRateOverlayOpen)
        {
            QuickRate.CancelCommand.Execute(null);
        }
        else if (IsDesignShowcaseOverlayOpen)
        {
            CloseDesignShowcaseOverlay();
        }
        else if (IsIssueProperties)
        {
            IssueProperties.CancelCommand.Execute(null);
        }
        else if (IsBulkIssueProperties)
        {
            BulkIssueProperties.CancelCommand.Execute(null);
        }
        else if (IsBulkSeriesProperties)
        {
            BulkSeriesProperties.CancelCommand.Execute(null);
        }
    }
}
