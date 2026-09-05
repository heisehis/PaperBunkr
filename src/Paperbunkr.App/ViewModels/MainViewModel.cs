using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Drives the app shell's navigation (rail nav, contextual sidebar, active screen).
/// Layout/tokens follow the "Paperbunkr App" wireframe (Claude Design project 43c40b25),
/// default variant: rail nav, pills toolbar, stacked detail layout, separate lists.
/// </summary>
public partial class MainViewModel : ViewModelBase, IContextMenuProvider
{
    /// <summary>Right-click/Menu-key menu for the Events &amp; Continuity sidebar rows
    /// (docs/superpowers/specs/2026-08-31-keyboard-operability-design.md) - lives here rather than on
    /// <see cref="EventsScreenViewModel"/> because the sidebar itself is declared in
    /// <c>MainWindow.axaml</c> with this class as its <c>DataContext</c> (see
    /// <see cref="EventsCardContextMenuBuilder"/>'s own doc comment).</summary>
    IReadOnlyList<ContextMenuEntry>? IContextMenuProvider.BuildContextMenu(object? target) =>
        new EventsCardContextMenuBuilder(this).Build(target);

    /// <summary>Undo/Redo for metadata edits (docs/ce-feature-inventory.md §A) - the same shared instance <see cref="IssueProperties"/>/<see cref="BulkIssueProperties"/> default to, held here too so the rail nav's Undo/Redo buttons can drive it.</summary>
    private readonly MetadataEditHistoryService _history = MetadataEditHistoryService.Shared;

    /// <summary>Drill-down navigation history - back/forward/breadcrumbs/restore-on-launch/CLI deep-
    /// linking (docs/superpowers/specs/2026-08-30-app-shell-navigation-history-design.md). Named
    /// distinctly from <see cref="_history"/> above (the unrelated metadata-edit undo/redo history)
    /// to avoid a naming collision.</summary>
    private readonly NavigationHistoryService _navigationHistory = new();

    /// <summary>Ambient "Background upkeep" rollup row + its return-to-idle timer (docs/superpowers/specs/2026-09-03-activity-center-design.md).</summary>
    private readonly IActivityUpkeepHandle _upkeep = null!;
    private readonly DispatcherTimer _upkeepIdleTimer = null!;

    /// <summary>Auto-update (docs/superpowers/specs/2026-09-01-auto-update-and-changelog-design.md) - one instance, shared between the startup check and Preferences' own manual "Check for Updates" button.</summary>
    private readonly UpdateService _updateService = new();

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

    /// <summary><see cref="RailOrder"/>'s keys sorted by their index, for <see cref="CycleScreen"/> -
    /// computed once rather than re-sorting on every Ctrl+Tab press (docs/superpowers/specs/
    /// 2026-08-31-app-wide-and-library-keyboard-shortcuts-design.md).</summary>
    private static readonly string[] RailOrderKeys = RailOrder.OrderBy(kvp => kvp.Value).Select(kvp => kvp.Key).ToArray();

    /// <summary>Runs one drill-down navigation's cross-fade + optional shared-element cover flight
    /// (docs/superpowers/specs/2026-09-04-navigation-transition-system-design.md) - a real
    /// <see cref="NavigationTransitionCoordinator"/> at the App composition root, or this no-op
    /// default (tests, design-time, <c>&lt;vm:MainViewModel /&gt;</c>) that just runs the swap
    /// synchronously with no visual effect, same convention as <see cref="ShowToast"/>/
    /// <see cref="NavigateBack"/> already being plain constructor-supplied callbacks.</summary>
    private readonly Func<string?, Action, Task> _runDrillTransition;

    public MainViewModel(Func<string?, Action, Task>? runDrillTransition = null)
    {
        _runDrillTransition = runDrillTransition ?? ((_, swap) => { swap(); return Task.CompletedTask; });

        // Seed the read-only starter workspaces before the two screen VMs first list them
        // (docs/superpowers/specs/2026-09-03-library-saved-workspaces-design.md). Idempotent.
        new WorkspaceService().EnsureBuiltInsSeeded();
        WorkspaceName = new WorkspaceNameViewModel(CloseWorkspaceNameOverlay);
        QuickOpen = new QuickOpenViewModel(ActivateQuickOpenEntry, CloseQuickOpenOverlay);

        MetadataWriteBack = new MetadataWriteBackQueue(ShowToast);

        // Activity Center (docs/superpowers/specs/2026-09-03-activity-center-design.md) - the
        // app-wide background-job registry, constructed here alongside the other manually-composed
        // services. Screen VMs that start background work take Activity; the shell owns the two
        // presentation VMs and the link resolver.
        Activity = new ActivityService();
        ActivityCenter = new ActivityCenterViewModel(Activity, ResolveActivityLink);
        StatusBar = new StatusBarViewModel(Activity, QueryLibraryStats, () => ActivityCenter.TogglePeekCommand.Execute(null));
        Activity.CompletionToastRequested += (title, message) => ShowToast(title, message);

        // Ambient "Background upkeep" rollup (docs/superpowers/specs/2026-09-03-activity-center-
        // design.md) - one always-present job, flipped active while the live folder-watch is
        // reacting to a change, back to idle a few seconds later. v1 covers folder-watch only;
        // wiring the thumbnail decoders in too is a follow-up.
        _upkeep = Activity.RegisterUpkeep("Background upkeep");
        _upkeepIdleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _upkeepIdleTimer.Tick += (_, _) => { _upkeepIdleTimer.Stop(); _upkeep.SetIdle(); };

        Home = new HomeScreenViewModel(GoDetailForSeries, GoReaderForIssue, GoLibraryWithSearch, GoReaderForIssueInReadingList, GoBookReaderForBook, GoLibraryWithCollection);
        Library = new LibraryScreenViewModel(GoDetailForSeries, GoReaderForIssue, GoNewIssuePropertiesForPlaceholder, OpenQuickRateOverlay, GoIssuePropertiesForIssue, GoBulkIssuePropertiesForIssues, ShowToast, GoBulkSeriesPropertiesForSeries, GoLibraryFoldersPreferences, OpenCollectionPropertiesOverlay, GoBookDetailForBook, promptForName: PromptWorkspaceName, enqueueMetadataWriteBack: EnqueueMetadataWriteBack);
        Books = new BooksScreenViewModel(GoBookDetailForBook, GoBookSeriesDetailForSeries, GoBookPropertiesForBook, GoBulkBookPropertiesForBooks, GoBookSeriesPropertiesForSeries, GoLibraryFoldersPreferences, ShowToast, promptForName: PromptWorkspaceName);
        BookDetail = new BookDetailScreenViewModel(NavigateBack, GoBookReaderForBook, GoBookPropertiesForBook, GoBulkBookPropertiesForBooks, GoBookSeriesPropertiesForSeries);
        BookProperties = new BookPropertiesScreenViewModel(CloseBookPropertiesOverlay, ShowToast);
        BulkBookProperties = new BulkBookPropertiesScreenViewModel(CloseBulkBookPropertiesOverlay, ShowToast);
        BookSeriesProperties = new BookSeriesPropertiesScreenViewModel(CloseBookSeriesPropertiesOverlay, ShowToast);
        BookReader = new BookReaderScreenViewModel(NavigateBack);
        PdfReader = new PdfPageReaderScreenViewModel(NavigateBack);
        Detail = new DetailScreenViewModel(NavigateBack, GoReaderForIssue, GoIssuePropertiesForIssue, GoBulkIssuePropertiesForIssues, GoDetailForSeries, GoLibraryWithSearch, OpenQuickRateOverlay, GoLibraryWithCollection, id => EnqueueMetadataWriteBack(id));
        MangaDetail = new MangaDetailScreenViewModel(NavigateBack, GoReaderForIssue, GoIssuePropertiesForIssue, GoBulkIssuePropertiesForIssues, GoDetailForSeries, GoLibraryWithSearch, GoLibraryWithCollection, id => EnqueueMetadataWriteBack(id));
        var keyBindingService = new KeyBindingService();
        Reader = new ReaderScreenViewModel(NavigateBack, keyBindingService);
        // "Ask me to rate a comic when I finish it" (docs/superpowers/specs/2026-09-04-behavior-
        // settings-batch2-design.md §3.3) - the reader raises this at the true end of a book when
        // AppSettings.PromptReviewOnFinish is on; reuse the same Quick Rate overlay the Library /
        // Detail right-click item opens.
        Reader.ReviewPromptRequested += OpenQuickRateOverlay;
        IssueProperties = new IssuePropertiesScreenViewModel(CloseIssuePropertiesOverlayAndReload, ShowToast, enqueueMetadataWriteBack: id => EnqueueMetadataWriteBack(id));
        BulkIssueProperties = new BulkIssuePropertiesScreenViewModel(CloseBulkIssuePropertiesOverlayAndReload, ShowToast, enqueueMetadataWriteBack: id => EnqueueMetadataWriteBack(id));
        BulkSeriesProperties = new BulkSeriesPropertiesScreenViewModel(CloseBulkSeriesPropertiesOverlayAndReload, id => EnqueueMetadataWriteBack(id));
        Smart = new SmartScreenViewModel(GoDetailForSeries, GoBookDetailForBook);
        Reading = new ReadingScreenViewModel(new FilePickerService(), GoReaderForIssueInReadingList, OpenReadingListPropertiesOverlay, ShowToast);
        Events = new EventsScreenViewModel(GoDetailForSeries, GoReaderForIssue, GoReadingWithList, ShowToast);
        Plugin = new PluginScreenViewModel(new FilePickerService());
        Migration = new MigrationOverlayViewModel(new FilePickerService(), OpenSeriesDetailFromReview);
        // First-run onboarding (docs/superpowers/specs/2026-08-31-first-run-onboarding-design.md) -
        // constructed here like every other overlay VM; LiveFolderWatch.Reload/OpenMigrationOverlay
        // are the same callbacks Preferences already reuses for the identical folder-add/migration
        // actions, CloseWelcomeOverlay is this class's own close-and-persist method (defined below).
        Welcome = new WelcomeOverlayViewModel(new FilePickerService(), () => LiveFolderWatch.Reload(), OpenMigrationOverlay, CloseWelcomeOverlay);
        // Auto-update (docs/superpowers/specs/2026-09-01-auto-update-and-changelog-design.md) - same
        // small-overlay-VM shape as Welcome above.
        Update = new UpdateAvailableOverlayViewModel(DownloadUpdateAsync, CloseUpdateAvailableOverlay);
        WelcomeTour = new WelcomeTourOverlayViewModel(
            GoHomeCommand, GoLibraryCommand, GoBooksCommand, GoSmartCommand, GoReadingCommand, GoEventsCommand, GoPreferencesCommand,
            CloseWelcomeTourOverlay);
        ReadingListProperties = new ReadingListPropertiesScreenViewModel(CloseReadingListPropertiesOverlay);
        CollectionProperties = new CollectionPropertiesScreenViewModel(CloseCollectionPropertiesOverlay);
        NewReadingList = new NewReadingListViewModel(new FilePickerService(), OnNewReadingListCreated, CloseNewReadingListDialog);
        NewEventOrContinuity = new NewEventOrContinuityViewModel(OnEventOrContinuityCreated, CloseNewEventDialog);
        QuickRate = new QuickRateScreenViewModel(CloseQuickRateOverlay, id => EnqueueMetadataWriteBack(id));
        DesignShowcase = new DesignShowcaseScreenViewModel();

        // Live folder-watch scanning (docs/superpowers/specs/
        // 2026-08-23-live-folder-watch-scanning-design.md) - constructed here alongside the app's
        // other manually-composed services (this codebase has no DI container). Its "already-open
        // UI should refresh" responsibility is just re-running Migration.NeedsReview's live query -
        // Library itself already reloads its data on every navigation (an earlier real bug fix), so
        // no separate push-refresh is needed there.
        LiveFolderWatch = new LiveFolderWatchService(ShowToast, () =>
        {
            _upkeep.SetActive("Reacting to a watched-folder change");
            _upkeepIdleTimer.Stop();
            _upkeepIdleTimer.Start();
            Migration.NeedsReview.Refresh();
        });
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
            Activity,
            LiveFolderWatch.Reload,
            OpenDesignShowcaseOverlay,
            _updateService,
            enqueueMetadataWriteBack: EnqueueMetadataWriteBack);

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

        // Cover-cache reconciliation (docs/superpowers/specs/2026-08-27-cover-thumbnail-identity-
        // validation-design.md): on every launch, regenerate any cover whose fingerprint no longer
        // matches its issue/book (e.g. after a library rebuild reassigned ids) and sweep orphaned
        // files. Presence-based, so it's cheap when nothing changed. Fire-and-forget - a slow first
        // run after an upgrade must not block the shell.
        _ = ReconcileCoverCachesAsync();

        // Periodic content verification (docs/superpowers/specs/2026-08-30-cover-thumbnail-content-
        // verification-design.md): unlike the reconcile above (identity-fingerprint presence only),
        // this unconditionally re-derives every cover from source, catching a cache entry that was
        // simply wrong from the moment it was written. Heavier, so it's gated to once every 7 days
        // rather than every launch - independent fire-and-forget task, silent by design (no toast).
        _ = PeriodicCoverVerificationAsync();
    }

    private static async Task ReconcileCoverCachesAsync()
    {
        var noProgress = new Progress<(int Done, int Total)>();
        try
        {
            await new CoverThumbnailService().GenerateAllAsync(noProgress);
        }
        catch
        {
            // Best-effort - the "Generate Covers" button and per-screen reloads still self-heal.
        }

        try
        {
            await new BookCoverThumbnailService().GenerateAllAsync(noProgress);
        }
        catch
        {
        }
    }

    private static readonly TimeSpan CoverVerificationInterval = TimeSpan.FromDays(7);

    /// <summary>
    /// Whether a periodic cover-verification pass is due, given when it last completed
    /// (<paramref name="lastRunUtc"/>, null if never) and the current time. A pure function so the
    /// gating logic is testable without waiting out real elapsed time or constructing a full
    /// <see cref="MainViewModel"/>.
    /// </summary>
    internal static bool ShouldRunCoverVerification(DateTime? lastRunUtc, DateTime nowUtc) =>
        lastRunUtc is not DateTime last || nowUtc - last >= CoverVerificationInterval;

    private static async Task PeriodicCoverVerificationAsync()
    {
        DateTime? lastRunUtc;
        using (var context = PaperbunkrDb.CreateContext())
        {
            lastRunUtc = context.GetOrCreateAppSettings().LastCoverVerificationUtc;
        }

        if (!ShouldRunCoverVerification(lastRunUtc, DateTime.UtcNow))
        {
            return;
        }

        var noProgress = new Progress<(int Done, int Total)>();
        try
        {
            await new CoverThumbnailService().VerifyAllAsync(noProgress);
            await new BookCoverThumbnailService().VerifyAllAsync(noProgress);

            using var context = PaperbunkrDb.CreateContext();
            var settings = context.GetOrCreateAppSettings();
            settings.LastCoverVerificationUtc = DateTime.UtcNow;
            context.SaveChanges();
        }
        catch
        {
            // Best-effort, same rationale as ReconcileCoverCachesAsync - retried next launch either
            // way, since the timestamp only advances on full completion.
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
    /// "Update ready - restart to apply" toast plumbing, same event-pair pattern as
    /// <see cref="ProgressToastRequested"/>/<see cref="ProgressToastCloseRequested"/> above but a
    /// distinct type - <see cref="UpdateReadyToastViewModel"/> carries actions, not progress
    /// (docs/superpowers/specs/2026-09-01-auto-update-and-changelog-design.md).
    /// </summary>
    public event Action<UpdateReadyToastViewModel>? UpdateReadyToastRequested;

    public event Action<UpdateReadyToastViewModel>? UpdateReadyToastCloseRequested;

    public HomeScreenViewModel Home { get; }
    public LibraryScreenViewModel Library { get; }
    public BooksScreenViewModel Books { get; }
    public BookDetailScreenViewModel BookDetail { get; }
    public BookPropertiesScreenViewModel BookProperties { get; }
    public BulkBookPropertiesScreenViewModel BulkBookProperties { get; }
    public BookSeriesPropertiesScreenViewModel BookSeriesProperties { get; }
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
    public WelcomeOverlayViewModel Welcome { get; }
    public UpdateAvailableOverlayViewModel Update { get; }
    public WelcomeTourOverlayViewModel WelcomeTour { get; }
    public ReadingListPropertiesScreenViewModel ReadingListProperties { get; }
    public CollectionPropertiesScreenViewModel CollectionProperties { get; }
    public NewReadingListViewModel NewReadingList { get; }
    public NewEventOrContinuityViewModel NewEventOrContinuity { get; }
    public QuickRateScreenViewModel QuickRate { get; }
    public WorkspaceNameViewModel WorkspaceName { get; }
    public QuickOpenViewModel QuickOpen { get; }

    public DesignShowcaseScreenViewModel DesignShowcase { get; }
    public LiveFolderWatchService LiveFolderWatch { get; }

    /// <summary>App-wide background-job registry (docs/superpowers/specs/2026-09-03-activity-center-design.md).</summary>
    public IActivityService Activity { get; }

    /// <summary>Backs the persistent bottom status bar.</summary>
    public StatusBarViewModel StatusBar { get; }

    /// <summary>Backs the Activity Center peek + drawer.</summary>
    public ActivityCenterViewModel ActivityCenter { get; }

    /// <summary>
    /// File metadata write-back queue (docs/superpowers/specs/2026-09-03-file-metadata-write-back-
    /// design.md) - constructed here alongside <see cref="LiveFolderWatch"/>, the app's other
    /// manually-composed background service. Trigger ViewModels get its <see cref="EnqueueMetadataWriteBack"/>
    /// as a plain callback, threaded the same way <see cref="ShowToast"/> is.
    /// </summary>
    public MetadataWriteBackQueue MetadataWriteBack { get; }

    private void EnqueueMetadataWriteBack(int issueId, bool manual = false) => MetadataWriteBack.Enqueue(issueId, manual);

    [ObservableProperty]
    private bool _isMigrationOverlayOpen;

    [ObservableProperty]
    private bool _isWelcomeOverlayOpen;

    [ObservableProperty]
    private bool _isUpdateAvailableOverlayOpen;

    [ObservableProperty]
    private bool _isTourOfferOpen;

    [ObservableProperty]
    private bool _isWelcomeTourOverlayOpen;

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
    private bool _isCollectionPropertiesOverlayOpen;

    [ObservableProperty]
    private bool _isNewReadingListDialogOpen;

    [ObservableProperty]
    private bool _isWorkspaceNameOverlayOpen;

    [ObservableProperty]
    private bool _isQuickOpenOverlayOpen;

    [ObservableProperty]
    private bool _isNewEventDialogOpen;

    [ObservableProperty]
    private bool _isBookPropertiesOverlayOpen;

    partial void OnIsBookPropertiesOverlayOpenChanged(bool value) => OnPropertyChanged(nameof(IsBookProperties));

    [ObservableProperty]
    private bool _isBulkBookPropertiesOverlayOpen;

    partial void OnIsBulkBookPropertiesOverlayOpenChanged(bool value) => OnPropertyChanged(nameof(IsBulkBookProperties));

    [ObservableProperty]
    private bool _isBookSeriesPropertiesOverlayOpen;

    partial void OnIsBookSeriesPropertiesOverlayOpenChanged(bool value) => OnPropertyChanged(nameof(IsBookSeriesProperties));

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

    /// <summary>The drill-down screen's own ViewModel, or null when a lateral screen (Home/Library/
    /// etc.) is active - bound to the shell's drill-down <c>TransitioningContentControl</c> (docs/
    /// superpowers/specs/2026-09-04-navigation-transition-system-design.md), the sibling of
    /// <see cref="ActiveScreenContent"/>'s own lateral one.</summary>
    public object? ActiveDrillDownContent => CurrentScreen switch
    {
        "detail" => Detail,
        "mangaDetail" => MangaDetail,
        "reader" => Reader,
        "bookReader" => BookReader,
        "pdfReader" => PdfReader,
        "bookDetail" => BookDetail,
        _ => null,
    };

    /// <summary>Push (deeper) vs Pop (back) for the drill-down transition - set immediately before
    /// <see cref="CurrentScreen"/> changes (see the <c>RunDrill</c> helper below), so
    /// <see cref="IsDrillTransitionReversed"/> already has the right value by the time the shell's
    /// drill-down <c>TransitioningContentControl</c> reacts to the content swap.</summary>
    [ObservableProperty]
    private DrillTransitionKind _drillTransitionKind;

    partial void OnDrillTransitionKindChanged(DrillTransitionKind value) => OnPropertyChanged(nameof(IsDrillTransitionReversed));

    /// <summary>Direction for the drill-down <c>TransitioningContentControl</c>'s <c>PageSlide</c> -
    /// same "one shared transition resource, direction from a bool" pattern
    /// <see cref="IsTransitionReversed"/> already established for the lateral rail system, reused
    /// here instead of inventing a second selection mechanism.</summary>
    public bool IsDrillTransitionReversed => DrillTransitionKind == DrillTransitionKind.Pop;

    public bool IsHome => CurrentScreen == "home";
    public bool IsLibrary => CurrentScreen == "library";
    public bool IsBooks => CurrentScreen == "books";
    public bool IsBookDetail => CurrentScreen == "bookDetail";
    public bool IsBookReader => CurrentScreen == "bookReader";
    public bool IsPdfReader => CurrentScreen == "pdfReader";
    public bool IsDetail => CurrentScreen == "detail";
    public bool IsMangaDetail => CurrentScreen == "mangaDetail";
    public bool IsSmart => CurrentScreen == "smart";
    public bool IsReading => CurrentScreen == "reading";
    public bool IsEvents => CurrentScreen == "events";
    public bool IsPreferences => CurrentScreen == "preferences";
    public bool IsReader => CurrentScreen == "reader";

    /// <summary>
    /// True on any of the three reading screens (comic <see cref="IsReader"/>, <see cref="IsBookReader"/>,
    /// <see cref="IsPdfReader"/>). The nav rail and the bottom status bar hide while this is true -
    /// reading is a full-bleed, distraction-free mode, and each reader carries its own back/close
    /// chrome plus the app-wide Escape / browser-back / swipe-back gestures. Superseded the older
    /// rail binding to <c>Reader.IsFullscreen</c>, which only hid chrome in the comic reader's
    /// fullscreen mode.
    /// </summary>
    public bool IsInReader => IsReader || IsBookReader || IsPdfReader;

    /// <summary>Alias, not a distinct concept - kept so <see cref="Escape"/>/<see cref="TryLeaveCurrentEditor"/>
    /// didn't need renaming when this stopped being <see cref="CurrentScreen"/>-backed.</summary>
    public bool IsIssueProperties => IsIssuePropertiesOverlayOpen;

    public bool IsBulkIssueProperties => IsBulkIssuePropertiesOverlayOpen;

    public bool IsBulkSeriesProperties => IsBulkSeriesPropertiesOverlayOpen;

    public bool IsBookProperties => IsBookPropertiesOverlayOpen;

    public bool IsBulkBookProperties => IsBulkBookPropertiesOverlayOpen;

    public bool IsBookSeriesProperties => IsBookSeriesPropertiesOverlayOpen;

    public bool ShowContextualSidebar => IsLibrary || IsSmart || IsReading || IsEvents;

    partial void OnCurrentScreenChanged(string value)
    {
        OnPropertyChanged(nameof(ActiveScreenContent));
        OnPropertyChanged(nameof(ActiveDrillDownContent));
        OnPropertyChanged(nameof(IsLateralScreen));
        OnPropertyChanged(nameof(IsHome));
        OnPropertyChanged(nameof(IsLibrary));
        OnPropertyChanged(nameof(IsBooks));
        OnPropertyChanged(nameof(IsBookDetail));
        OnPropertyChanged(nameof(IsBookReader));
        OnPropertyChanged(nameof(IsPdfReader));
        OnPropertyChanged(nameof(IsInReader));
        OnPropertyChanged(nameof(IsDetail));
        OnPropertyChanged(nameof(IsMangaDetail));
        OnPropertyChanged(nameof(IsSmart));
        OnPropertyChanged(nameof(IsReading));
        OnPropertyChanged(nameof(IsEvents));
        OnPropertyChanged(nameof(IsPreferences));
        OnPropertyChanged(nameof(IsReader));
        OnPropertyChanged(nameof(ShowContextualSidebar));
        OnPropertyChanged(nameof(ShowBreadcrumb));
    }

    [RelayCommand]
    private void GoHome() => TryLeaveCurrentEditor(() =>
    {
        Home.LoadFromDatabase();
        CurrentScreen = "home";
        ResetHistoryRoot("home");
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
        ResetHistoryRoot("library");
    });

    /// <summary>Home's Collections shelf click-through (docs/superpowers/specs/2026-08-27-
    /// collections-design.md's own deferred "Home-feed shelf" follow-on) - same shape as
    /// <see cref="GoLibraryWithSearch"/>.</summary>
    private void GoLibraryWithCollection(int collectionId) => TryLeaveCurrentEditor(() =>
    {
        Library.LoadFromDatabase();
        Library.SelectCollectionById(collectionId);
        CurrentScreen = "library";
        ResetHistoryRoot("library");
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
        ResetHistoryRoot("library");
    });

    [RelayCommand]
    private void GoBooks() => TryLeaveCurrentEditor(() =>
    {
        Books.LoadFromDatabase();
        CurrentScreen = "books";
        ResetHistoryRoot("books");
    });

    [RelayCommand]
    private void GoSmart() => TryLeaveCurrentEditor(() =>
    {
        Smart.EnsureListLoaded();
        CurrentScreen = "smart";
        ResetHistoryRoot("smart");
    });

    [RelayCommand]
    private void GoReading() => TryLeaveCurrentEditor(() =>
    {
        Reading.EnsureListLoaded();
        CurrentScreen = "reading";
        ResetHistoryRoot("reading");
    });

    /// <summary>Opens the Reading screen on a specific list - used by the Story Events "create reading list from continuity" action (docs/superpowers/specs/2026-08-27-metadata-model-phase4f-continuity-browse-design.md).</summary>
    private void GoReadingWithList(int readingListId)
    {
        Reading.LoadReadingList(readingListId);
        CurrentScreen = "reading";
        ResetHistoryRoot("reading");
    }

    [RelayCommand]
    private void GoEvents() => TryLeaveCurrentEditor(() =>
    {
        Events.EnsureEventLoaded();
        CurrentScreen = "events";
        ResetHistoryRoot("events");
    });

    [RelayCommand]
    private void GoPreferences() => TryLeaveCurrentEditor(() =>
    {
        Preferences.EnsureLoaded();
        CurrentScreen = "preferences";
        ResetHistoryRoot("preferences");
    });

    /// <summary>
    /// <c>Ctrl+Tab</c>/<c>Ctrl+Shift+Tab</c> (docs/superpowers/specs/2026-08-31-app-wide-and-library-
    /// keyboard-shortcuts-design.md), bound in <c>MainWindow.axaml</c>'s <c>Window.KeyBindings</c>
    /// alongside the existing Escape/Back entries. Dispatches to the same per-screen <c>Go*</c>
    /// method each rail button already calls (not a raw <see cref="CurrentScreen"/> set) so cycling
    /// gets the same load/history-reset/unsaved-editor-guard behavior a rail click gets - deliberately
    /// not simplified to "just set CurrentScreen" even though that's cheaper to write.
    /// </summary>
    [RelayCommand]
    private void CycleScreenForward() => CycleScreen(1);

    [RelayCommand]
    private void CycleScreenBack() => CycleScreen(-1);

    private void CycleScreen(int direction)
    {
        if (!RailOrder.TryGetValue(CurrentScreen, out int currentIndex))
        {
            // A drill-down screen (Reader/Detail/etc.) is active, not one of the 7 lateral rail
            // screens - cycling "top-level views" doesn't mean anything from here, so no-op rather
            // than guessing which lateral screen to jump to.
            return;
        }

        int count = RailOrderKeys.Length;
        int nextIndex = ((currentIndex + direction) % count + count) % count;

        switch (RailOrderKeys[nextIndex])
        {
            case "home": GoHome(); break;
            case "library": GoLibrary(); break;
            case "books": GoBooks(); break;
            case "smart": GoSmart(); break;
            case "reading": GoReading(); break;
            case "events": GoEvents(); break;
            case "preferences": GoPreferences(); break;
        }
    }

    /// <summary>Library's empty-state "Scan folders" action (docs/superpowers/specs/2026-08-27-
    /// library-browsing-4b-toolbar-rework-design.md §9) - opens Preferences straight to the
    /// Library section where folders are added/scanned.</summary>
    private void GoLibraryFoldersPreferences() => TryLeaveCurrentEditor(() =>
    {
        Preferences.EnsureLoaded();
        Preferences.ActiveSection = Models.PreferencesSection.Library;
        CurrentScreen = "preferences";
        ResetHistoryRoot("preferences");
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

    /// <summary>First-run welcome screen (docs/superpowers/specs/2026-08-31-first-run-onboarding-
    /// design.md) - <paramref name="ceInstallDetected"/> is <c>App.axaml.cs</c>'s own
    /// <c>File.Exists(MigrationViewModel.GetDefaultCePath())</c> check, forwarded here rather than
    /// recomputed, since it's already been computed once at startup.</summary>
    [RelayCommand]
    private void OpenWelcomeOverlay(bool ceInstallDetected)
    {
        Welcome.CeInstallDetected = ceInstallDetected;
        IsWelcomeOverlayOpen = true;
    }

    /// <summary>The single close path every exit from <see cref="Welcome"/> routes through (a
    /// completed card action, Skip, or Esc/X) - persists <see cref="AppSettings.WelcomeScreenShown"/>
    /// once, then offers the one-time tour if it hasn't been offered yet. Both flags are written in
    /// the same context/SaveChanges call, one write path, no per-button duplication.</summary>
    private void CloseWelcomeOverlay()
    {
        IsWelcomeOverlayOpen = false;

        using var context = PaperbunkrDb.CreateContext();
        var appSettings = context.GetOrCreateAppSettings();
        appSettings.WelcomeScreenShown = true;

        if (!appSettings.WelcomeTourOffered)
        {
            appSettings.WelcomeTourOffered = true;
            IsTourOfferOpen = true;
        }

        context.SaveChanges();
    }

    [RelayCommand]
    private void TakeTour()
    {
        IsTourOfferOpen = false;
        OpenWelcomeTourOverlay();
    }

    [RelayCommand]
    private void DeclineTour() => IsTourOfferOpen = false;

    private void OpenWelcomeTourOverlay()
    {
        WelcomeTour.Open();
        IsWelcomeTourOverlayOpen = true;
    }

    private void CloseWelcomeTourOverlay() => IsWelcomeTourOverlayOpen = false;

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

    /// <summary>Entry point wired into the Library sidebar's "Edit…" row menu (docs/superpowers/specs/2026-08-27-collections-design.md, step 8) - same open shape as <see cref="OpenReadingListPropertiesOverlay"/>.</summary>
    private void OpenCollectionPropertiesOverlay(int collectionId)
    {
        CollectionProperties.Load(collectionId);
        IsCollectionPropertiesOverlayOpen = true;
    }

    /// <summary>Save/Cancel's shared <c>goBack</c> callback, and the explicit close button's command - reloads the Library sidebar either way.</summary>
    [RelayCommand]
    private void CloseCollectionPropertiesOverlay()
    {
        IsCollectionPropertiesOverlayOpen = false;
        Library.LoadFromDatabase();
    }

    /// <summary>The sidebar's "＋" opens this (docs/superpowers/specs/2026-08-28-reading-lists-screen-redesign-design.md → v2).</summary>
    [RelayCommand]
    private void OpenNewReadingListDialog()
    {
        NewReadingList.Reset();
        IsNewReadingListDialogOpen = true;
    }

    [RelayCommand]
    private void CloseNewReadingListDialog() => IsNewReadingListDialogOpen = false;

    /// <summary>The Library/Books workspace switchers' "Save current view as…" / "Rename" prompt
    /// (docs/superpowers/specs/2026-09-03-library-saved-workspaces-design.md) - opens the shared
    /// naming overlay, hands the entered name back to whichever screen asked, and closes.</summary>
    private void PromptWorkspaceName(string? initial, Action<string> onName)
    {
        WorkspaceName.Begin(initial, name =>
        {
            onName(name);
            IsWorkspaceNameOverlayOpen = false;
        });
        IsWorkspaceNameOverlayOpen = true;
    }

    [RelayCommand]
    private void CloseWorkspaceNameOverlay() => IsWorkspaceNameOverlayOpen = false;

    /// <summary>Any editor / dialog overlay that owns focus - Ctrl+P must not open the palette on top of one.</summary>
    private bool IsEditorOverlayOpen =>
        IsIssuePropertiesOverlayOpen || IsBulkIssuePropertiesOverlayOpen || IsBulkSeriesPropertiesOverlayOpen
        || IsBookPropertiesOverlayOpen || IsBulkBookPropertiesOverlayOpen || IsBookSeriesPropertiesOverlayOpen
        || IsReadingListPropertiesOverlayOpen || IsCollectionPropertiesOverlayOpen || IsWorkspaceNameOverlayOpen
        || IsNewReadingListDialogOpen || IsNewEventDialogOpen || IsMigrationOverlayOpen || IsQuickRateOverlayOpen
        || IsWelcomeOverlayOpen || IsWelcomeTourOverlayOpen;

    /// <summary>Ctrl+P (docs/superpowers/specs/2026-09-03-quick-open-command-palette-design.md) - opens
    /// the command palette. No-op while an editor overlay is up, or if it's already open. The
    /// reader carve-out is in <c>MainWindow.axaml.cs</c>'s key handler, not here.</summary>
    [RelayCommand]
    private void OpenQuickOpen()
    {
        if (IsEditorOverlayOpen || IsQuickOpenOverlayOpen)
        {
            return;
        }

        QuickOpen.Open();
        IsQuickOpenOverlayOpen = true;
    }

    [RelayCommand]
    private void CloseQuickOpenOverlay() => IsQuickOpenOverlayOpen = false;

    private void ActivateQuickOpenEntry(Paperbunkr.App.Models.QuickOpenEntry entry)
    {
        switch (entry.Kind)
        {
            case Paperbunkr.App.Models.QuickOpenKind.Series:
                GoDetailForSeries(entry.EntityId!.Value);
                break;
            case Paperbunkr.App.Models.QuickOpenKind.Issue:
                GoReaderForIssue(entry.EntityId!.Value);
                break;
            case Paperbunkr.App.Models.QuickOpenKind.Book:
                GoBookDetailForBook(entry.EntityId!.Value);
                break;
            case Paperbunkr.App.Models.QuickOpenKind.Collection:
                GoLibraryWithCollection(entry.EntityId!.Value);
                break;
            case Paperbunkr.App.Models.QuickOpenKind.ReadingList:
                GoReadingWithList(entry.EntityId!.Value);
                break;
            case Paperbunkr.App.Models.QuickOpenKind.SmartList:
                GoSmartCommand.Execute(null);
                Smart.LoadSmartList(entry.EntityId!.Value);
                break;
            case Paperbunkr.App.Models.QuickOpenKind.StoryEvent:
                GoEventsCommand.Execute(null);
                Events.LoadEvent(entry.EntityId!.Value);
                break;
            case Paperbunkr.App.Models.QuickOpenKind.Continuity:
                GoEventsCommand.Execute(null);
                Events.LoadContinuity(entry.EntityId!.Value);
                break;
            case Paperbunkr.App.Models.QuickOpenKind.Screen:
                (entry.Key switch
                {
                    "library" => GoLibraryCommand,
                    "books" => GoBooksCommand,
                    "smart" => GoSmartCommand,
                    "reading" => GoReadingCommand,
                    "events" => GoEventsCommand,
                    "preferences" => GoPreferencesCommand,
                    _ => GoHomeCommand,
                }).Execute(null);
                break;
            case Paperbunkr.App.Models.QuickOpenKind.Action:
                RunQuickOpenAction(entry.Key!);
                break;
        }
    }

    private void RunQuickOpenAction(string key)
    {
        switch (key)
        {
            case "addFolder":
                GoLibraryFoldersPreferences();
                break;
            case "addIssue":
                GoLibraryCommand.Execute(null);
                Library.OpenAddIssueCommand.Execute(null);
                break;
            case "newReadingList":
                OpenNewReadingListDialog();
                break;
            case "importCe":
                OpenMigrationOverlay();
                break;
        }
    }

    private void OnNewReadingListCreated(int listId)
    {
        IsNewReadingListDialogOpen = false;
        Reading.LoadReadingList(listId);
        CurrentScreen = "reading";
        ResetHistoryRoot("reading");
    }

    /// <summary>Sidebar "＋" on the Events &amp; Continuity screen (docs/superpowers/specs/2026-08-28-events-continuity-screen-redesign-design.md).</summary>
    [RelayCommand]
    private void OpenNewEventDialog(string kind)
    {
        NewEventOrContinuity.Reset(kind == "Continuity"
            ? NewEventOrContinuityViewModel.Kind.Continuity
            : NewEventOrContinuityViewModel.Kind.Event);
        IsNewEventDialogOpen = true;
    }

    /// <summary>"Edit details" from the Events &amp; Continuity screen's ⋯ Manage menu
    /// (docs/superpowers/specs/2026-08-28-continuity-editing-design.md).</summary>
    [RelayCommand]
    private void OpenEditEventDialog()
    {
        if (Events.ActiveEventId is not int id)
        {
            return;
        }

        NewEventOrContinuity.LoadForEdit(NewEventOrContinuityViewModel.Kind.Event, id);
        IsNewEventDialogOpen = true;
    }

    [RelayCommand]
    private void OpenEditContinuityDialog()
    {
        if (Events.ActiveContinuityId is not int id)
        {
            return;
        }

        NewEventOrContinuity.LoadForEdit(NewEventOrContinuityViewModel.Kind.Continuity, id);
        IsNewEventDialogOpen = true;
    }

    /// <summary>"Edit details" from a sidebar row's right-click menu (docs/superpowers/specs/
    /// 2026-08-31-keyboard-operability-design.md) - composes the sidebar's own existing
    /// <see cref="EventsScreenViewModel.SelectEventCommand"/> with <see cref="OpenEditEventDialog"/>
    /// (which only ever acts on "whatever's currently active", no by-id overload) so a right-click
    /// on any row, not just the already-active one, opens that row's own edit dialog.</summary>
    [RelayCommand]
    private void EditEventFromContextMenu(StoryEventSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        Events.SelectEventCommand.Execute(summary);
        OpenEditEventDialog();
    }

    /// <summary>Continuity counterpart to <see cref="EditEventFromContextMenu"/>.</summary>
    [RelayCommand]
    private void EditContinuityFromContextMenu(ContinuitySummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        Events.SelectContinuityCommand.Execute(summary);
        OpenEditContinuityDialog();
    }

    [RelayCommand]
    private void CloseNewEventDialog() => IsNewEventDialogOpen = false;

    private void OnEventOrContinuityCreated(NewEventOrContinuityViewModel.Kind kind, int id)
    {
        IsNewEventDialogOpen = false;
        if (kind == NewEventOrContinuityViewModel.Kind.Continuity)
        {
            Events.LoadContinuity(id);
        }
        else
        {
            Events.LoadEvent(id);
        }

        CurrentScreen = "events";
        ResetHistoryRoot("events");
    }

    /// <summary>Book Properties editor entry point (docs/superpowers/specs/2026-08-27-book-properties-
    /// editor-design.md) - the B1 Details "Edit" button, the Books grid card context menu, and the
    /// Book Details Series-mode card context menu all route here.</summary>
    private void GoBookPropertiesForBook(int bookId)
    {
        BookProperties.Load(bookId);
        IsBookPropertiesOverlayOpen = true;
    }

    /// <summary>Save/Cancel's shared <c>goBack</c> callback and the corner "X" command - repaints
    /// whatever's underneath (Book Details or the Books grid), same "cheap enough to always refresh"
    /// tolerance as <see cref="CloseReadingListPropertiesOverlay"/>.</summary>
    [RelayCommand]
    private void CloseBookPropertiesOverlay()
    {
        IsBookPropertiesOverlayOpen = false;
        ReloadBooksSurfaceUnderneath();
    }

    /// <summary>Bulk book editor entry point (docs/superpowers/specs/2026-08-27-books-bulk-series-
    /// editing-design.md) - from the Books grid selection bar or Book Details Series mode.</summary>
    private void GoBulkBookPropertiesForBooks(IReadOnlyList<int> bookIds)
    {
        if (bookIds.Count == 0)
        {
            return;
        }

        BulkBookProperties.Load(bookIds);
        IsBulkBookPropertiesOverlayOpen = true;
    }

    [RelayCommand]
    private void CloseBulkBookPropertiesOverlay()
    {
        IsBulkBookPropertiesOverlayOpen = false;
        ReloadBooksSurfaceUnderneath();
    }

    /// <summary>BookSeries editor entry point - from Book Details Series mode or the Books grid's
    /// grouped-by-Series section-header context menu.</summary>
    private void GoBookSeriesPropertiesForSeries(int bookSeriesId)
    {
        BookSeriesProperties.Load(bookSeriesId);
        IsBookSeriesPropertiesOverlayOpen = true;
    }

    [RelayCommand]
    private void CloseBookSeriesPropertiesOverlay()
    {
        IsBookSeriesPropertiesOverlayOpen = false;
        ReloadBooksSurfaceUnderneath();
    }

    private void ReloadBooksSurfaceUnderneath()
    {
        if (IsBookDetail)
        {
            BookDetail.ReloadCurrent();
        }
        else
        {
            Books.LoadFromDatabase();
        }
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
            // Reloads the same series already on screen - not a real navigation, so this goes
            // through the Core method (no history push) rather than the wrapper.
            NavigateToDetailCore(seriesId);
        }
        else if (IsLibrary)
        {
            Library.LoadFromDatabase();
        }
        else if (IsBookDetail)
        {
            BookDetail.ReloadCurrentBook();
        }
        else if (IsBooks)
        {
            Books.LoadFromDatabase();
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
    /// instead of returning where the user actually came from.
    ///
    /// Originally fixed with a single-slot "remember the one prior screen" hack
    /// (<c>_screenBeforeReader</c>/<c>GoBackFromReader</c>), which only ever supported exactly one
    /// level and didn't generalize to Detail/MangaDetail/BookDetail (which had no back concept at
    /// all beyond hardcoded <see cref="GoLibrary"/>/<see cref="GoBooks"/> callbacks). Superseded by
    /// <see cref="_navigationHistory"/> (docs/superpowers/specs/2026-08-30-app-shell-navigation-
    /// history-design.md) - see <see cref="NavigateBack"/>/<see cref="GoToRootScreen"/> below, which
    /// this comment's problem statement describes the fix for across all six drill-down screens, not
    /// just Reader.
    /// </summary>

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
            || (IsBulkSeriesProperties && BulkSeriesProperties.HasUnsavedChanges())
            || (IsBookProperties && BookProperties.HasUnsavedChanges())
            || (IsBulkBookProperties && BulkBookProperties.HasUnsavedChanges())
            || (IsBookSeriesProperties && BookSeriesProperties.HasUnsavedChanges());

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
            IsBookPropertiesOverlayOpen = false;
            IsBulkBookPropertiesOverlayOpen = false;
            IsBookSeriesPropertiesOverlayOpen = false;
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

    /// <summary>
    /// The series id most recently routed to either Detail screen (docs/superpowers/specs/
    /// 2026-08-23-manga-detail-screen-design.md) - replaces the old <c>Detail.ReloadCurrentSeries()</c>
    /// round-trip (which only knew how to reload the Western screen) so
    /// <see cref="ReloadDetailAfterIssueEdit"/> can re-route through <see cref="GoDetailForSeries"/>
    /// regardless of which screen the series belongs on, including if the edit itself reclassified it.
    /// </summary>
    private int? _currentDetailSeriesId;

    /// <summary>Mirrors <see cref="_currentDetailSeriesId"/>'s role for the other four drill-down
    /// screens (docs/superpowers/specs/2026-08-30-app-shell-navigation-history-design.md) - the
    /// currently-loaded entity id, used by <see cref="PersistLastScreen"/> for restore-on-launch.
    /// Null on <see cref="_currentBookDetailBookId"/> specifically means "BookDetail is in Series
    /// mode", not "nothing loaded" - see <see cref="NavigateToBookSeriesDetailCore"/>.</summary>
    private int? _currentReaderIssueId;

    private int? _currentBookDetailBookId;

    private int? _currentBookReaderBookId;

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
            // Reloads the same series, not a real navigation - Core method, no history push.
            NavigateToDetailCore(seriesId);
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

    private void GoDetailForSeries(int seriesId) => RunDrill(DrillTransitionKind.Push, $"series-cover:{seriesId}", () =>
    {
        NavigateToDetailCore(seriesId);
        PushHistory(BuildDetailEntry(seriesId));
    });

    /// <summary>The reusable half of <see cref="GoDetailForSeries"/> - no history side effect, so
    /// <see cref="RefreshAfterHistoryChange"/>/<see cref="ReloadDetailAfterIssueEdit"/> (reloading the
    /// same series, not a real navigation) and <see cref="ReplayEntry"/> (Back/Forward/breadcrumb
    /// jump, which move the history cursor themselves) can both call it directly.</summary>
    private void NavigateToDetailCore(int seriesId) => CurrentScreen = LoadDetailSeries(seriesId) ? "mangaDetail" : "detail";

    /// <summary>Builds the breadcrumb/history entry for whichever Detail screen <see cref="CurrentScreen"/>
    /// was just routed to - call only after <see cref="NavigateToDetailCore"/> has already run.</summary>
    private NavigationEntry BuildDetailEntry(int seriesId) => new(
        CurrentScreen,
        CurrentScreen == "mangaDetail" ? NavigationEntryKind.MangaSeries : NavigationEntryKind.Series,
        seriesId,
        CurrentScreen == "mangaDetail" ? MangaDetail.HeaderTitle : Detail.HeaderTitle);

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

    private void GoReaderForIssue(int issueId) => RunDrill(DrillTransitionKind.Push, $"issue-cover:{issueId}", () =>
    {
        NavigateToReaderCore(issueId, readingListId: null);
        PushHistory(new NavigationEntry("reader", NavigationEntryKind.Issue, issueId, Reader.IssueTitle));
    });

    /// <summary>The reusable half of <see cref="GoReaderForIssue"/>/<see cref="GoReaderForIssueInReadingList"/> -
    /// no history side effect, so <see cref="ReplayEntry"/> can call it directly during Back/Forward/
    /// breadcrumb replay. Replay never restores which reading list a session was anchored to (that
    /// context isn't part of <see cref="NavigationEntry"/>) - an acceptable simplification, low-risk
    /// since it only affects Reader's own "up/down next in list" affordance while paging back through
    /// history, not the page content itself.</summary>
    private void NavigateToReaderCore(int issueId, int? readingListId)
    {
        _currentReaderIssueId = issueId;
        if (readingListId is int listId)
        {
            Reader.LoadIssue(issueId, listId);
        }
        else
        {
            Reader.LoadIssue(issueId);
        }

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
    private void GoReaderForIssueInReadingList(int issueId, int readingListId) => RunDrill(DrillTransitionKind.Push, $"issue-cover:{issueId}", () =>
    {
        NavigateToReaderCore(issueId, readingListId);
        PushHistory(new NavigationEntry("reader", NavigationEntryKind.Issue, issueId, Reader.IssueTitle));
    });

    /// <summary>Book Details entry point (docs/superpowers/specs/2026-08-27-book-details-screen-
    /// design.md) - the Books grid card click lands here now, not straight in the reader.</summary>
    private void GoBookDetailForBook(int bookId) => RunDrill(DrillTransitionKind.Push, sharedKey: null, () =>
    {
        NavigateToBookDetailCore(bookId);
        PushHistory(new NavigationEntry("bookDetail", NavigationEntryKind.Book, bookId, BookDetail.HeaderTitle));
    });

    private void NavigateToBookDetailCore(int bookId)
    {
        _currentBookDetailBookId = bookId;
        BookDetail.LoadBook(bookId);
        CurrentScreen = "bookDetail";
    }

    /// <summary>Grouped-by-Series section header click on the Books grid - opens Series mode of the same screen.</summary>
    private void GoBookSeriesDetailForSeries(int bookSeriesId) => RunDrill(DrillTransitionKind.Push, sharedKey: null, () =>
    {
        NavigateToBookSeriesDetailCore(bookSeriesId);
        PushHistory(new NavigationEntry("bookDetail", NavigationEntryKind.BookSeries, bookSeriesId, BookDetail.HeaderTitle));
    });

    private void NavigateToBookSeriesDetailCore(int bookSeriesId)
    {
        // Series mode isn't restore-on-launch/CLI-addressable (no "bookSeries" CLI kind, per the
        // design doc's CLI section) - null is a deliberate "not resumable" marker, not an oversight.
        _currentBookDetailBookId = null;
        BookDetail.LoadSeries(bookSeriesId);
        CurrentScreen = "bookDetail";
    }

    private void GoBookReaderForBook(int bookId, BookFormat format) => GoBookReaderForBook(bookId, format, null);

    /// <param name="startAt">A chapter or bookmark jump from the Book Details screen; null resumes
    /// from the book's saved position.</param>
    private void GoBookReaderForBook(int bookId, BookFormat format, BookPosition? startAt) => RunDrill(DrillTransitionKind.Push, sharedKey: null, () =>
    {
        NavigateToBookReaderCore(bookId, format, startAt);
        PushHistory(new NavigationEntry(CurrentScreen, NavigationEntryKind.Book, bookId, LookupBookTitle(bookId)));
    });

    /// <summary>The reusable half of <see cref="GoBookReaderForBook(int, BookFormat, BookPosition?)"/> -
    /// no history side effect, so <see cref="ReplayEntry"/> can call it directly. <paramref name="startAt"/>
    /// is always null on replay (same simplification as <see cref="NavigateToReaderCore"/> dropping
    /// reading-list context) - Back/Forward resumes from the book's saved position, not the original
    /// chapter/bookmark jump that first opened it.</summary>
    private void NavigateToBookReaderCore(int bookId, BookFormat format, BookPosition? startAt)
    {
        _currentBookReaderBookId = bookId;
        if (format == BookFormat.Pdf)
        {
            PdfReader.LoadBook(bookId, startAt);
            CurrentScreen = "pdfReader";
        }
        else
        {
            BookReader.LoadBook(bookId, startAt);
            CurrentScreen = "bookReader";
        }
    }

    private static string LookupBookTitle(int bookId)
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.Books.Where(b => b.Id == bookId).Select(b => b.Title).FirstOrDefault() ?? "Unknown";
    }

    // --- Navigation history: back/forward, breadcrumbs, restore-on-launch, CLI deep-linking ---
    // docs/superpowers/specs/2026-08-30-app-shell-navigation-history-design.md

    /// <summary>Every lateral rail GoX() calls this instead of touching <see cref="_navigationHistory"/>
    /// directly - clears any in-progress drill-down chain and establishes the new root, then raises
    /// the UI/persistence side effects. Call after <c>CurrentScreen</c> has already been assigned, so
    /// <see cref="PersistLastScreen"/> reads the correct value.</summary>
    private void ResetHistoryRoot(string railScreenKey)
    {
        _navigationHistory.ResetRoot(railScreenKey);
        AfterNavigationChanged();
    }

    /// <summary>Every drill-down wrapper (GoDetailForSeries, GoReaderForIssue, etc.) calls this after
    /// its Core method has already set <c>CurrentScreen</c> and the relevant <c>_current...Id</c>
    /// tracking field.</summary>
    private void PushHistory(NavigationEntry entry)
    {
        _navigationHistory.Push(entry);
        AfterNavigationChanged();
    }

    /// <summary>The single entry point every drill-down navigation (push, pop, and forward-redeepen)
    /// routes through (docs/superpowers/specs/2026-09-04-navigation-transition-system-design.md) -
    /// sets <see cref="DrillTransitionKind"/> (driving <see cref="IsDrillTransitionReversed"/>) then
    /// hands <paramref name="swap"/> - exactly what the call site's body used to run directly - to
    /// <see cref="_runDrillTransition"/>. Fire-and-forget is fine: <paramref name="swap"/> itself runs
    /// synchronously before any await inside the coordinator, so CurrentScreen/history/
    /// CanNavigateBack etc. are already correct by the time this method returns, regardless of how
    /// long (or whether) a shared-element flight plays afterward.</summary>
    private void RunDrill(DrillTransitionKind kind, string? sharedKey, Action swap)
    {
        DrillTransitionKind = kind;
        _ = _runDrillTransition(sharedKey, swap);
    }

    /// <summary>The shared-element key for whichever drill-down screen is on screen <em>right now</em>,
    /// used by <see cref="NavigateBack"/>/<see cref="NavigateForward"/>/<see cref="NavigateToBreadcrumbIndex"/>
    /// - call BEFORE moving the history cursor/swapping content, since it reads <see cref="CurrentScreen"/>
    /// and the <c>_current...Id</c> fields as they stand for the screen about to become the outgoing
    /// one. Keys are derived purely from ids already in hand (no DB lookup) so both ends of a flight
    /// - e.g. a Library series card and Detail's hero - can agree on the same string independently:
    /// "series-cover:{seriesId}" for Detail/MangaDetail, "issue-cover:{issueId}" for Reader. Books have
    /// no Library-grid cover tile to morph from/to (per the design doc), so BookDetail/BookReader/
    /// PdfReader resolve to null - a plain cross-fade, no flight.</summary>
    private string? CurrentDrillSharedKey() => CurrentScreen switch
    {
        "detail" or "mangaDetail" when _currentDetailSeriesId is int seriesId => $"series-cover:{seriesId}",
        "reader" when _currentReaderIssueId is int issueId => $"issue-cover:{issueId}",
        _ => null,
    };

    /// <summary>Refreshes everything derived from <see cref="_navigationHistory"/>'s state and
    /// persists <see cref="AppSettings.LastScreenKey"/>/<see cref="AppSettings.LastScreenEntityId"/> -
    /// called after every navigation, not gated on <c>CurrentScreen</c> actually changing value
    /// (unlike <see cref="OnCurrentScreenChanged"/>'s hook, which CommunityToolkit's generated setter
    /// skips when the new value equals the old one - e.g. Detail→Detail for a *different* series
    /// leaves <c>CurrentScreen</c> at "detail" both before and after, but the entity id still needs
    /// re-persisting).</summary>
    private void AfterNavigationChanged()
    {
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(CanNavigateForward));
        OnPropertyChanged(nameof(BreadcrumbTrail));
        OnPropertyChanged(nameof(RootScreenLabel));
        PersistLastScreen();
    }

    private void PersistLastScreen()
    {
        int? entityId = CurrentScreen switch
        {
            "detail" or "mangaDetail" => _currentDetailSeriesId,
            "reader" => _currentReaderIssueId,
            "bookDetail" => _currentBookDetailBookId,
            "bookReader" or "pdfReader" => _currentBookReaderBookId,
            _ => null,
        };

        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();
        settings.LastScreenKey = CurrentScreen;
        settings.LastScreenEntityId = entityId;
        context.SaveChanges();
    }

    /// <summary>
    /// Startup update check (docs/superpowers/specs/2026-09-01-auto-update-and-changelog-design.md) -
    /// called once from <c>App.axaml.cs</c> after the main window is shown, guarded there against
    /// running the same launch the welcome overlay opens (avoid stacking two first-look modals).
    /// Ask-before-download, matching CE's own confirm-first flow
    /// (_reference/ComicRackCE/ComicRack/MainForm.cs:4510-4522) - this only ever opens the prompt,
    /// never downloads anything itself. No install-state gate (unlike the earlier Velopack version) -
    /// NetSparkle's appcast check works regardless of how the app was launched; wrapped in try/catch
    /// since a startup check silently failing on a network hiccup should never surface an error to
    /// the user, just skip quietly (same instinct as CE's own try/catch in GithubAPI.GetResponse).
    ///
    /// Called via <c>Task.Run</c> from <c>App.axaml.cs</c> (see that call site's own doc comment for
    /// why - a real UI-thread-freeze bug this fixes), so this method's body, including everything
    /// after the <c>await</c> below, runs on a threadpool thread with no UI SynchronizationContext to
    /// resume on. <see cref="Update"/>/<see cref="IsUpdateAvailableOverlayOpen"/> are bound into the
    /// overlay's UI, so setting them off the UI thread would be a cross-thread violation (the same
    /// class of bug the WindowNotificationManager.Show crash logs already on file for
    /// LiveFolderWatchService caught) - <see cref="Dispatcher.UIThread"/>.Post marshals just that
    /// tail back, matching this codebase's established pattern (e.g. AsyncCoverImage.cs).
    /// </summary>
    public async Task CheckForUpdatesOnStartupAsync()
    {
        bool shouldCheck;
        using (var context = PaperbunkrDb.CreateContext())
        {
            shouldCheck = context.GetOrCreateAppSettings().CheckForUpdatesOnStartup;
        }

        if (!shouldCheck)
        {
            return;
        }

        UpdateInfo info;
        try
        {
            info = await _updateService.CheckForUpdatesAsync();
        }
        catch (Exception)
        {
            return;
        }

        if (info.Status != UpdateStatus.UpdateAvailable || info.Updates.Count == 0)
        {
            return;
        }

        string? changelogBody = LoadNewestChangelogBody();
        Dispatcher.UIThread.Post(() =>
        {
            Update.Show(info.Updates[0], changelogBody);
            IsUpdateAvailableOverlayOpen = true;
        });
    }

    private static string? LoadNewestChangelogBody()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
        if (!File.Exists(path))
        {
            return null;
        }

        var entries = ChangelogParser.Parse(File.ReadAllText(path));
        return entries.Count > 0 ? entries[0].Body : null;
    }

    private void CloseUpdateAvailableOverlay() => IsUpdateAvailableOverlayOpen = false;

    /// <summary>
    /// Download+apply flow, started from <see cref="Update"/>'s Download button. The download runs
    /// as an Activity Center job (a 0-100 percent callback maps cleanly onto <c>Report</c>). On
    /// completion, fires the <see cref="UpdateReadyToastRequested"/> toast; <c>ApplyUpdatesAndRestart</c>
    /// only ever runs from that toast's own explicit Restart-now action - never automatically here.
    /// </summary>
    private async Task DownloadUpdateAsync(AppCastItem item)
    {
        using var job = Activity.StartJob(ActivityJobKind.Update, "Downloading update", cancellable: false);

        string downloadPath = await _updateService.DownloadUpdatesAsync(item, pct => job.Report(pct, 100, $"{pct}%"));

        job.Succeed("Update downloaded", new ActivityLink(ActivityLinkKind.UpdateChangelog));

        UpdateReadyToastViewModel? readyToast = null;
        readyToast = new UpdateReadyToastViewModel(
            item,
            downloadPath,
            _updateService,
            onClose: () => UpdateReadyToastCloseRequested?.Invoke(readyToast!),
            onWhatsNew: () =>
            {
                GoPreferencesCommand.Execute(null);
                Preferences.GoAboutCommand.Execute(null);
            });
        UpdateReadyToastRequested?.Invoke(readyToast);
    }

    /// <summary>Where <see cref="NavigateBack"/>/<see cref="NavigateToBreadcrumbIndex"/> land when the
    /// history cursor moves past the start of the current drill-down chain - reloads and switches to
    /// whichever lateral screen is <see cref="NavigationHistoryService.RootScreenKey"/>, the same data
    /// refresh each corresponding GoX() does, just without re-resetting the history root (that would
    /// wipe the forward stack this method's own callers are about to preserve).</summary>
    private void GoToRootScreen(string rootScreenKey, string? sharedKey = null)
    {
        switch (rootScreenKey)
        {
            case "library":
                Library.LoadFromDatabase();
                // docs/superpowers/specs/2026-09-04-navigation-transition-system-design.md - back-
                // trip realization: scroll the flight's destination tile into view before
                // NavigationTransitionCoordinator's poll starts looking for it. A no-op when
                // sharedKey is null or doesn't resolve to a currently-visible tile (grouped,
                // filtered out, wrong granularity) - falls through to the coordinator's existing
                // "destination never registers -> plain cross-fade" edge case.
                Library.RequestScrollIntoView(sharedKey);
                CurrentScreen = "library";
                break;
            case "books":
                Books.LoadFromDatabase();
                CurrentScreen = "books";
                break;
            case "smart":
                Smart.EnsureListLoaded();
                CurrentScreen = "smart";
                break;
            case "reading":
                Reading.EnsureListLoaded();
                CurrentScreen = "reading";
                break;
            case "events":
                Events.EnsureEventLoaded();
                CurrentScreen = "events";
                break;
            case "preferences":
                Preferences.EnsureLoaded();
                CurrentScreen = "preferences";
                break;
            default:
                Home.LoadFromDatabase();
                CurrentScreen = "home";
                break;
        }
    }

    /// <summary>Re-navigates to a <see cref="NavigationEntry"/> without pushing a new one - used by
    /// <see cref="NavigateBack"/>/<see cref="NavigateForward"/>/<see cref="NavigateToBreadcrumbIndex"/>,
    /// which have already moved the history cursor themselves.</summary>
    private void ReplayEntry(NavigationEntry entry)
    {
        switch (entry.ScreenKey)
        {
            case "detail":
            case "mangaDetail":
                NavigateToDetailCore(entry.EntityId);
                break;
            case "reader":
                NavigateToReaderCore(entry.EntityId, readingListId: null);
                break;
            case "bookDetail":
                if (entry.Kind == NavigationEntryKind.BookSeries)
                {
                    NavigateToBookSeriesDetailCore(entry.EntityId);
                }
                else
                {
                    NavigateToBookDetailCore(entry.EntityId);
                }

                break;
            case "bookReader":
                NavigateToBookReaderCore(entry.EntityId, BookFormat.Epub, startAt: null);
                break;
            case "pdfReader":
                NavigateToBookReaderCore(entry.EntityId, BookFormat.Pdf, startAt: null);
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanNavigateBack))]
    private void NavigateBack() => TryLeaveCurrentEditor(() =>
    {
        var sharedKey = CurrentDrillSharedKey();
        RunDrill(DrillTransitionKind.Pop, sharedKey, () =>
        {
            var entry = _navigationHistory.Back();
            if (entry is null)
            {
                GoToRootScreen(_navigationHistory.RootScreenKey, sharedKey);
            }
            else
            {
                ReplayEntry(entry);
            }

            AfterNavigationChanged();
        });
    });

    /// <summary>Forward has no keyboard binding by design (docs/superpowers/specs/2026-08-30-app-
    /// shell-navigation-history-design.md's Input section) - reachable only via a breadcrumb click or
    /// trackpad swipe-forward.</summary>
    [RelayCommand(CanExecute = nameof(CanNavigateForward))]
    private void NavigateForward() => TryLeaveCurrentEditor(() =>
    {
        var sharedKey = CurrentDrillSharedKey();
        RunDrill(DrillTransitionKind.Push, sharedKey, () =>
        {
            var entry = _navigationHistory.Forward();
            if (entry is not null)
            {
                ReplayEntry(entry);
            }

            AfterNavigationChanged();
        });
    });

    public bool CanNavigateBack => _navigationHistory.CanGoBack;

    public bool CanNavigateForward => _navigationHistory.CanGoForward;

    /// <summary>Drives the breadcrumb bar's visibility. Originally shown on all six drill-down
    /// screens per the design doc's "drill-down screens only" call; narrowed to just the three
    /// metadata-browsing screens (Detail/MangaDetail/BookDetail) after real on-screen feedback - the
    /// three reader screens (Reader/BookReader/PdfReader) are immersive, full-page reading views
    /// where a persistent top bar is a real UX cost in a way it isn't on the browsing screens, and
    /// Reader already has an established "hidden by default, chrome reveals on hover" convention
    /// (the thumbnail rail) that a permanent breadcrumb bar cuts against. Back/Backspace navigation
    /// is unaffected either way - this only controls the bar's visibility, not NavigateBack itself.
    /// The seven lateral rail screens keep today's slide-transition system unchanged, no breadcrumb.</summary>
    public bool ShowBreadcrumb => IsDetail || IsMangaDetail || IsBookDetail;

    public IReadOnlyList<NavigationEntry> BreadcrumbTrail => _navigationHistory.BreadcrumbTrail;

    /// <summary>Mirrors the rail's own <c>railLabel</c> text in <c>MainWindow.axaml</c> - the
    /// breadcrumb bar's root segment label.</summary>
    private static readonly Dictionary<string, string> RailScreenLabels = new()
    {
        ["home"] = "Home",
        ["library"] = "Library",
        ["books"] = "Books",
        ["smart"] = "Smart Lists",
        ["reading"] = "Reading Lists",
        ["events"] = "Continuity",
        ["preferences"] = "Preferences",
    };

    public string RootScreenLabel => RailScreenLabels.TryGetValue(_navigationHistory.RootScreenKey, out string? label) ? label : "Home";

    /// <summary>A breadcrumb segment click - jumps directly to that index (possibly several levels
    /// at once), truncating anything past it exactly like a fresh navigation would. Index -1 is the
    /// root segment.</summary>
    [RelayCommand]
    private void NavigateToBreadcrumbIndex(int index) => TryLeaveCurrentEditor(() =>
    {
        var sharedKey = CurrentDrillSharedKey();
        RunDrill(DrillTransitionKind.Pop, sharedKey, () =>
        {
            var entry = _navigationHistory.JumpTo(index);
            if (entry is null)
            {
                GoToRootScreen(_navigationHistory.RootScreenKey, sharedKey);
            }
            else
            {
                ReplayEntry(entry);
            }

            AfterNavigationChanged();
        });
    });

    /// <summary>CLI-argument deep-linking (<c>--open &lt;kind&gt;:&lt;id&gt;</c>) - called once from
    /// <c>App.axaml.cs</c> at startup when <see cref="NavigationCliArgs.TryParseOpenArg"/> found a
    /// target, taking priority over <see cref="RestoreLastScreen"/>. Establishes a sensible root for
    /// each kind (matching how that screen is normally reached) before the drill-down wrapper pushes
    /// its own entry - "collection" is the one kind that isn't a drill-down screen at all, so it just
    /// delegates straight to the existing lateral entry point.</summary>
    public void OpenDeepLink(NavigationCliTarget target)
    {
        switch (target.Kind)
        {
            case "series":
                _navigationHistory.ResetRoot("library");
                GoDetailForSeries(target.Id);
                break;
            case "issue":
                _navigationHistory.ResetRoot("library");
                GoReaderForIssue(target.Id);
                break;
            case "book":
                _navigationHistory.ResetRoot("books");
                GoBookDetailForBook(target.Id);
                break;
            case "collection":
                GoLibraryWithCollection(target.Id);
                break;
        }
    }

    /// <summary>Restore-on-launch - called once from <c>App.axaml.cs</c> at startup when no CLI deep
    /// link was present. Only the last screen is restored, not the full history stack (which starts
    /// empty each launch, per the design doc's explicit call). Falls back to Home, logged rather than
    /// thrown, when the referenced entity no longer exists (deleted since last session) - same
    /// posture as <see cref="AppSettings.LibraryActiveCollectionId"/>'s existing "falls back to All
    /// Series if deleted" handling. Gated by <see cref="AppSettings.RestoreSessionOnStartup"/>
    /// (docs/superpowers/specs/2026-09-04-behavior-settings-batch2-design.md §3.1, CE
    /// <c>Settings.OpenLastFile</c>) - off means a clean Home every launch.</summary>
    public void RestoreLastScreen()
    {
        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();

        if (!settings.RestoreSessionOnStartup)
        {
            GoHome();
            return;
        }

        try
        {
            switch (settings.LastScreenKey)
            {
                case "detail" or "mangaDetail" when settings.LastScreenEntityId is int seriesId
                    && context.Series.Any(s => s.Id == seriesId):
                    _navigationHistory.ResetRoot("library");
                    GoDetailForSeries(seriesId);
                    return;
                case "reader" when settings.LastScreenEntityId is int issueId
                    && context.Issues.Any(i => i.Id == issueId):
                    _navigationHistory.ResetRoot("library");
                    GoReaderForIssue(issueId);
                    return;
                case "bookDetail" when settings.LastScreenEntityId is int detailBookId
                    && context.Books.Any(b => b.Id == detailBookId):
                    _navigationHistory.ResetRoot("books");
                    GoBookDetailForBook(detailBookId);
                    return;
                case "bookReader" when settings.LastScreenEntityId is int epubId
                    && context.Books.Any(b => b.Id == epubId):
                    _navigationHistory.ResetRoot("books");
                    GoBookReaderForBook(epubId, BookFormat.Epub);
                    return;
                case "pdfReader" when settings.LastScreenEntityId is int pdfId
                    && context.Books.Any(b => b.Id == pdfId):
                    _navigationHistory.ResetRoot("books");
                    GoBookReaderForBook(pdfId, BookFormat.Pdf);
                    return;
                case "library":
                    GoLibrary();
                    return;
                case "books":
                    GoBooks();
                    return;
                case "smart":
                    GoSmart();
                    return;
                case "reading":
                    GoReading();
                    return;
                case "events":
                    GoEvents();
                    return;
                case "preferences":
                    GoPreferences();
                    return;
                default:
                    DiagnosticsService.LogMilestone(
                        $"RestoreLastScreen: no usable last screen ('{settings.LastScreenKey}', entity {settings.LastScreenEntityId}) - defaulting to Home.");
                    GoHome();
                    return;
            }
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"RestoreLastScreen failed ({ex.GetType().Name}: {ex.Message}) - defaulting to Home.");
            GoHome();
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
    /// Esc-to-close/cancel (P5, docs/Paperbunkr-Roadmap.md), routed here rather than per-screen
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
        if (ActivityCenter.IsOpen)
        {
            ActivityCenter.CloseCommand.Execute(null);
            return;
        }

        if (IsQuickOpenOverlayOpen)
        {
            CloseQuickOpenOverlay();
        }
        else if (IsMigrationOverlayOpen)
        {
            CloseMigrationOverlay();
        }
        else if (IsWelcomeOverlayOpen)
        {
            Welcome.SkipCommand.Execute(null);
        }
        else if (IsTourOfferOpen)
        {
            DeclineTour();
        }
        else if (IsWelcomeTourOverlayOpen)
        {
            WelcomeTour.SkipCommand.Execute(null);
        }
        else if (IsNewReadingListDialogOpen)
        {
            CloseNewReadingListDialog();
        }
        else if (IsNewEventDialogOpen)
        {
            CloseNewEventDialog();
        }
        else if (IsReadingListPropertiesOverlayOpen)
        {
            ReadingListProperties.CancelCommand.Execute(null);
        }
        else if (IsCollectionPropertiesOverlayOpen)
        {
            CollectionProperties.CancelCommand.Execute(null);
        }
        else if (IsBookProperties)
        {
            BookProperties.CancelCommand.Execute(null);
        }
        else if (IsBulkBookProperties)
        {
            BulkBookProperties.CancelCommand.Execute(null);
        }
        else if (IsBookSeriesProperties)
        {
            BookSeriesProperties.CancelCommand.Execute(null);
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
        // Real bug, found via manual testing: MainWindow's Escape KeyDown handler (docs/superpowers/
        // specs/2026-08-31-app-wide-and-library-keyboard-shortcuts-design.md) always calls this
        // command and marks the key event Handled regardless of whether any branch above actually
        // matched - which silently blocked LibraryScreenViewModel's own Add-issue overlay from ever
        // seeing Escape (its close handler lived in LibraryScreen's own KeyDown handler, downstream
        // of MainWindow's in the Tunnel routing order, so it never got a chance to run once this
        // method had already consumed the key). Centralizing that case here instead, matching every
        // other overlay this method already owns, rather than leaving two uncoordinated Escape
        // handlers to fight over precedence.
        else if (Library.IsAddIssueOpen)
        {
            Library.CloseAddIssueCommand.Execute(null);
        }
    }

    /// <summary>
    /// Central resolver for a finished job's / alert's <see cref="ActivityLink"/>
    /// (docs/superpowers/specs/2026-09-03-activity-center-design.md). New link kinds are added here,
    /// not in the panel. Best-effort - an unparseable payload just no-ops.
    /// </summary>
    private void ResolveActivityLink(ActivityLink link)
    {
        switch (link.Kind)
        {
            case ActivityLinkKind.SeriesDetail when int.TryParse(link.Payload, out int seriesId):
                GoDetailForSeries(seriesId);
                break;
            case ActivityLinkKind.LibrarySavedFilter:
                GoLibraryWithSearch(link.Payload);
                break;
            case ActivityLinkKind.UpdateChangelog:
                if (Update.Info is not null)
                {
                    IsUpdateAvailableOverlayOpen = true;
                }

                break;
            case ActivityLinkKind.MigrationReview:
                OpenMigrationOverlay();
                break;
            case ActivityLinkKind.Preferences:
                GoPreferencesCommand.Execute(null);
                break;
        }
    }

    /// <summary>Cheap library-total aggregate for the status bar's left region. Runs on a background thread (see <see cref="StatusBarViewModel"/>).</summary>
    private static (int Comics, long Bytes) QueryLibraryStats()
    {
        using var context = PaperbunkrDb.CreateContext();
        int comics = context.Issues.Count();
        long bytes = context.Issues.Sum(i => (long?)i.FileSize) ?? 0L;
        return (comics, bytes);
    }
}
