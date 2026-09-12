using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using NetSparkleUpdater.Enums;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.Data;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.Tracking;
using Paperbunkr.Data.Tracking.Adapters;
using Paperbunkr.Data.VirtualTags;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Preferences screen tab-strip (docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md
/// §5), following the same mode-enum + computed Is*Tab pattern <see cref="DetailTabsViewModel"/>
/// already established. Appearance, Behavior (docs/superpowers/specs/
/// 2026-08-07-preferences-behavior-tab-design.md), Libraries (docs/superpowers/specs/
/// 2026-08-07-preferences-libraries-tab-design.md), Reader (docs/superpowers/specs/
/// 2026-08-07-reader-rtl-navigation-design.md), and Advanced (docs/superpowers/specs/
/// 2026-08-07-preferences-advanced-tab-design.md) are real today - Scripts was confirmed a
/// zero-real-surface dead end via CE-source triage and deliberately has no tab.
/// </summary>
public partial class PreferencesScreenViewModel : ViewModelBase
{
    private readonly SkinService _skinService;
    private readonly IFilePickerService _filePicker;
    private readonly LibraryFolderScanner _libraryScanner;
    private readonly LibraryHealthService _libraryHealth;
    private readonly FileAssociationService _fileAssociationService;
    private readonly BackupService _backupService;
    private readonly KeyBindingService _keyBindingService;
    private readonly UpdateService _updateService;
    private readonly Action<string, string> _showToast;
    private readonly Action<int, bool> _enqueueMetadataWriteBack;
    private readonly Action _openMigration;
    private readonly Action _openDesignShowcase;
    private readonly IActivityService _activity;
    private readonly IDialogService _dialogService;
    private readonly Action _reloadFolderWatch;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private bool _isLoaded;
    private bool _suppressFontApply;
    private bool _suppressMotionApply;
    private bool _suppressBehaviorApply;
    private bool _suppressVirtualTagApply;
    private bool _suppressBackupSettingsApply;
    private Issue _previewIssue = SampleIssue();
    private Series? _previewSeries = new() { Name = "Sample Series" };

    public PreferencesScreenViewModel(
        SkinService skinService,
        IFilePickerService filePicker,
        LibraryFolderScanner libraryScanner,
        FileAssociationService fileAssociationService,
        BackupService backupService,
        KeyBindingService keyBindingService,
        Action<string, string> showToast,
        MigrationOverlayViewModel migration,
        PluginScreenViewModel plugin,
        Action openMigration,
        IActivityService activity,
        IDialogService dialogService,
        Action reloadFolderWatch,
        Action openDesignShowcase,
        UpdateService updateService,
        Action<int, bool>? enqueueMetadataWriteBack = null,
        LibraryHealthService? libraryHealth = null)
        : this(skinService, filePicker, libraryScanner, fileAssociationService, backupService, keyBindingService, showToast, migration, plugin, openMigration, activity, dialogService, reloadFolderWatch, openDesignShowcase, updateService, PaperbunkrDb.CreateContext, enqueueMetadataWriteBack, libraryHealth)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal PreferencesScreenViewModel(
        SkinService skinService,
        IFilePickerService filePicker,
        LibraryFolderScanner libraryScanner,
        FileAssociationService fileAssociationService,
        BackupService backupService,
        KeyBindingService keyBindingService,
        Action<string, string> showToast,
        MigrationOverlayViewModel migration,
        PluginScreenViewModel plugin,
        Action openMigration,
        IActivityService activity,
        IDialogService dialogService,
        Action reloadFolderWatch,
        Action openDesignShowcase,
        UpdateService updateService,
        Func<PaperbunkrDbContext> contextFactory,
        Action<int, bool>? enqueueMetadataWriteBack = null,
        LibraryHealthService? libraryHealth = null)
    {
        _enqueueMetadataWriteBack = enqueueMetadataWriteBack ?? ((_, _) => { });
        _openDesignShowcase = openDesignShowcase;
        _skinService = skinService;
        _filePicker = filePicker;
        _libraryScanner = libraryScanner;
        _libraryHealth = libraryHealth ?? new LibraryHealthService(contextFactory);
        _fileAssociationService = fileAssociationService;
        _backupService = backupService;
        _keyBindingService = keyBindingService;
        _updateService = updateService;
        _showToast = showToast;
        Migration = migration;
        Plugin = plugin;
        _openMigration = openMigration;
        _activity = activity;
        _dialogService = dialogService;
        _reloadFolderWatch = reloadFolderWatch;
        _contextFactory = contextFactory;
        Skins = new ObservableCollection<SkinSummary>();
        FontFamilies = new ObservableCollection<string>();
        VirtualTags = new ObservableCollection<VirtualTagSummary>();
        WatchedFolders = new ObservableCollection<WatchedFolderSummary>();
        BookFolders = new ObservableCollection<BookFolderSummary>();
        FileAssociations = new ObservableCollection<FileAssociationSummary>();
        Backups = new ObservableCollection<BackupRowViewModel>();
        NavigationKeyBindings = new ObservableCollection<KeyBindingRowViewModel>();
        ZoomFitKeyBindings = new ObservableCollection<KeyBindingRowViewModel>();
        DisplayKeyBindings = new ObservableCollection<KeyBindingRowViewModel>();
        ChangelogEntries = new ObservableCollection<ChangelogEntry>();
        ScheduledTasks = new ObservableCollection<ScheduledTaskRow>();
        MissingFileItems = new ObservableCollection<MissingFileRowViewModel>();
        RecentlyRemovedItems = new ObservableCollection<RemovedLibraryEntryRowViewModel>();

        // Connections list+dialog (docs/superpowers/specs/2026-09-06-connections-tracker-dialog-
        // redesign-design.md) - fresh row instances per VM instance, never the shared static catalog
        // (see ConnectionProviderRow's own doc comment on why).
        SourceProviderRows = new ObservableCollection<ConnectionProviderRow>(ConnectionProviderRow.CreateSourceProviders());
        TrackerProviderRows = new ObservableCollection<ConnectionProviderRow>(ConnectionProviderRow.CreateTrackerProviders());

        // Wire each row's command references to the matching [RelayCommand]-generated command
        // (docs/superpowers/specs/2026-09-07-connections-redesign-design.md) - the dialog's generic
        // per-Kind template binds to these instead of a hardcoded per-provider command name. Command
        // bodies are unchanged.
        ComicVineRow.SaveCommand = SaveComicVineCredentialsCommand;
        ComicVineRow.DisconnectCommand = DisconnectComicVineCommand;
        MetronRow.PrimaryCommand = SaveMetronCredentialsCommand;
        MetronRow.DisconnectCommand = DisconnectMetronCommand;

        AniListRow.ConnectCommand = ConnectAniListCommand;
        AniListRow.CompleteCommand = CompleteAniListConnectCommand;
        AniListRow.DisconnectCommand = DisconnectAniListCommand;
        MyAnimeListRow.ConnectCommand = ConnectMyAnimeListCommand;
        MyAnimeListRow.CompleteCommand = CompleteMyAnimeListConnectCommand;
        MyAnimeListRow.DisconnectCommand = DisconnectMyAnimeListCommand;
        ShikimoriRow.ConnectCommand = ConnectShikimoriCommand;
        ShikimoriRow.CompleteCommand = CompleteShikimoriConnectCommand;
        ShikimoriRow.DisconnectCommand = DisconnectShikimoriCommand;
        BangumiRow.SaveCommand = SaveBangumiTokenCommand;
        BangumiRow.DisconnectCommand = DisconnectBangumiCommand;
        MangaBakaRow.SaveCommand = SaveMangaBakaTokenCommand;
        MangaBakaRow.DisconnectCommand = DisconnectMangaBakaCommand;
        MangaUpdatesRow.PrimaryCommand = ConnectMangaUpdatesCommand;
        MangaUpdatesRow.DisconnectCommand = DisconnectMangaUpdatesCommand;
        KitsuRow.PrimaryCommand = ConnectKitsuCommand;
        KitsuRow.DisconnectCommand = DisconnectKitsuCommand;

        // Clear Cover Cache (docs/superpowers/specs/2026-08-30-cover-thumbnail-content-
        // verification-design.md) - manual escape hatch, independent of VerifyCovers' detection
        // logic. Two-step inline confirm, same pattern every other destructive action in this
        // codebase uses (docs/superpowers/specs/2026-08-22-delete-functionality-design.md), not a
        // modal dialog.
        ClearComicCoverCacheConfirm = new TwoStepConfirm(
            onConfirmed: () => _ = ClearComicCoverCacheAsync(),
            idleLabel: "Clear Comic Cover Cache",
            armedLabel: "Confirm clear?");
        ClearBookCoverCacheConfirm = new TwoStepConfirm(
            onConfirmed: () => _ = ClearBookCoverCacheAsync(),
            idleLabel: "Clear Book Cover Cache",
            armedLabel: "Confirm clear?");

        // Removed-files blacklist escape hatch (docs/superpowers/specs/2026-09-06-scan-missing-
        // file-handling-design.md) - same two-step inline confirm pattern as the cover-cache clears
        // above; no per-path browse/unblock UI in v1, just a wipe-the-whole-table button.
        ClearRemovedFilesListConfirm = new TwoStepConfirm(
            onConfirmed: ClearRemovedFilesList,
            idleLabel: "Clear Removed-Files List",
            armedLabel: "Confirm clear?");
    }

    private static Issue SampleIssue() => new()
    {
        Number = "1",
        Volume = "1",
        Year = 2024,
        Title = "Sample Issue",
        Publisher = "Sample Publisher",
        Writer = "Sample Writer",
        Penciller = "Sample Penciller",
    };

    public ObservableCollection<SkinSummary> Skins { get; }

    public ObservableCollection<string> FontFamilies { get; }

    // --- SuggestBox string projections (docs/superpowers/specs/2026-09-10-suggestbox-migration-
    // plan.md). SuggestBox is string-only; each closed-enum picker gets a Text view + a Names list,
    // following the TagEditRowViewModel.WeightText/WeightNames pattern. The XAML sets IsStrict, so a
    // setter only ever receives a member of the matching *Names list; anything else is ignored.
    public string DefaultPageFitModeText
    {
        get => DefaultPageFitMode.ToString();
        set { if (Enum.TryParse<ImageFitMode>(value, out var parsed)) DefaultPageFitMode = parsed; }
    }

    public string[] FitModeNames { get; } = Enum.GetNames<ImageFitMode>();

    public string DefaultPageLayoutModeText
    {
        get => DefaultPageLayoutMode.ToString();
        set { if (Enum.TryParse<PageLayoutMode>(value, out var parsed)) DefaultPageLayoutMode = parsed; }
    }

    public string[] PageLayoutModeNames { get; } = Enum.GetNames<PageLayoutMode>();

    public string PageTransitionStyleText
    {
        get => PageTransitionStyle.ToString();
        set { if (Enum.TryParse<PageTransitionStyle>(value, out var parsed)) PageTransitionStyle = parsed; }
    }

    public string[] PageTransitionStyleNames { get; } = Enum.GetNames<PageTransitionStyle>();

    public string ImageBackgroundModeText
    {
        get => ImageBackgroundMode.ToString();
        set { if (Enum.TryParse<ImageBackgroundMode>(value, out var parsed)) ImageBackgroundMode = parsed; }
    }

    public string[] BackgroundModeNames { get; } = Enum.GetNames<ImageBackgroundMode>();

    public string RenderingBackendText
    {
        get => RenderingBackend.ToString();
        set { if (Enum.TryParse<RenderBackend>(value, out var parsed)) RenderingBackend = parsed; }
    }

    public string[] RenderBackendNames { get; } = Enum.GetNames<RenderBackend>();

    /// <summary>Instance passthrough of <see cref="BackgroundColorPresets"/> - the preset picker is
    /// non-strict, so a hex value typed into it still round-trips through <c>BackgroundColor</c>.</summary>
    public string[] BackgroundColorPresetNames => BackgroundColorPresets;

    /// <summary>docs/superpowers/specs/2026-09-01-auto-update-and-changelog-design.md - newest first, parsed from the bundled CHANGELOG.md.</summary>
    public ObservableCollection<ChangelogEntry> ChangelogEntries { get; }

    /// <summary>The release-style version string for the About section - <c>"0.3.0-beta"</c>. Matches
    /// the <c>CHANGELOG.md</c> <c>## [x.y.z-beta]</c> headings (so the changelog accordion's exact-match
    /// "Current" badge actually lights - it never did while this returned the four-part <c>x.y.z.w</c>)
    /// and the CI-derived release tag. docs/superpowers/specs/2026-09-10-versioning-convention-design.md Q5.</summary>
    public string CurrentVersion => ReleaseVersion.DisplayString;

    /// <summary>Short git commit hash of this build (<c>"9cc0b62"</c>), <c>"dev"</c> off a non-git
    /// build, or <see langword="null"/>. Shown as a faint secondary line under the version.</summary>
    public string? BuildLabel => ReleaseVersion.BuildMetadata;

    [ObservableProperty]
    private string? _updateCheckResultText;

    /// <summary>
    /// Manual re-check from the About section - unlike the startup check, this always sets
    /// <see cref="UpdateCheckResultText"/> (hit or "up to date"), matching CE's own alwaysCheck
    /// branch (_reference/ComicRackCE/ComicRack/MainForm.cs:4522-4526). No install-state gate here
    /// (unlike the earlier Velopack version) - NetSparkle's appcast check works regardless of how the
    /// app was launched, so a network/appcast failure is the only real failure mode to guard.
    /// </summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        UpdateCheckResultText = "Checking...";
        try
        {
            var info = await _updateService.CheckForUpdatesAsync();
            UpdateCheckResultText = info.Status == UpdateStatus.UpdateAvailable
                ? $"Update available: v{info.Updates[0].Version}"
                : "You're up to date.";
        }
        catch (Exception ex)
        {
            UpdateCheckResultText = $"Check failed: {ex.Message}";
        }
    }

    public ObservableCollection<VirtualTagSummary> VirtualTags { get; }

    public ObservableCollection<WatchedFolderSummary> WatchedFolders { get; }

    /// <summary>Novel (EPUB/PDF) source folders - moved here from the Books screen so all
    /// "populate my library" folder management lives on the Libraries tab.</summary>
    public ObservableCollection<BookFolderSummary> BookFolders { get; }

    /// <summary>
    /// The same <see cref="MigrationOverlayViewModel"/> instance <see cref="MainViewModel"/> owns -
    /// exposed here so the Libraries tab's "Migrate from ComicRack CE" entry point (docs/superpowers/specs/
    /// 2026-08-09-embedded-metadata-and-migration-relocation-design.md §2) can bind to
    /// <c>Migration.NeedsReview.HasPendingItems</c> for its badge without duplicating that state.
    /// </summary>
    public MigrationOverlayViewModel Migration { get; }

    /// <summary>docs/superpowers/specs/2026-08-24-navigation-shell-motion-system-design.md - the same instance <see cref="MainViewModel"/> owns (constructed there, passed in) - Plugins moved from a standalone rail screen to a Preferences tab, this ViewModel doesn't own the lifetime.</summary>
    public PluginScreenViewModel Plugin { get; }

    /// <summary>
    /// Opens the migration overlay, which still renders at the shell root (unchanged) - this just
    /// relays to <see cref="MainViewModel"/>'s own open logic (<c>Migration.Open()</c> +
    /// <c>IsMigrationOverlayOpen = true</c>), which this ViewModel has no direct way to set itself.
    /// </summary>
    [RelayCommand]
    private void OpenMigration() => _openMigration();

    /// <summary>docs/superpowers/specs/2026-08-24-design-language-foundation-design.md - debug-only, see <see cref="IsDebugBuild"/>.</summary>
    [RelayCommand]
    private void OpenDesignShowcase() => _openDesignShowcase();

#if DEBUG
    public bool IsDebugBuild => true;
#else
    public bool IsDebugBuild => false;
#endif

    /// <summary>Reverted back to the sidebar + hard-switch pane (docs/superpowers/specs/2026-09-07-
    /// preferences-tile-hub-redesign-design.md's single-scroll shell turned out too annoying to
    /// navigate once actually tried - user feedback, same session) - one section visible at a time,
    /// same as the original 2026-08-28 preferences rework.</summary>
    [ObservableProperty]
    private PreferencesSection _activeSection = PreferencesSection.General;

    public bool IsGeneralSection => ActiveSection == PreferencesSection.General;
    public bool IsAppearanceSection => ActiveSection == PreferencesSection.Appearance;
    public bool IsLibrarySection => ActiveSection == PreferencesSection.Library;
    public bool IsAutomationSection => ActiveSection == PreferencesSection.Automation;
    public bool IsReaderSection => ActiveSection == PreferencesSection.Reader;
    public bool IsKeyboardShortcutsSection => ActiveSection == PreferencesSection.KeyboardShortcuts;
    public bool IsConnectionsSection => ActiveSection == PreferencesSection.Connections;
    public bool IsPluginsSection => ActiveSection == PreferencesSection.Plugins;
    public bool IsAdvancedSection => ActiveSection == PreferencesSection.Advanced;
    public bool IsAboutSection => ActiveSection == PreferencesSection.About;

    /// <summary>Sidebar order for the shell (docs/superpowers/specs/2026-08-28-preferences-rework-design.md).</summary>
    public static IReadOnlyList<PreferencesSection> Sections => PreferencesSectionMeta.Order;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>Group-card hits for the current <see cref="SearchQuery"/> - see <see cref="PreferenceIndex"/>.</summary>
    public ObservableCollection<PreferenceSearchResultViewModel> SearchResults { get; } = new();

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchQuery);

    /// <summary>True while a search is active but nothing matched - drives the sidebar's empty note.</summary>
    public bool NoSearchResults => IsSearching && SearchResults.Count == 0;

    /// <summary>Raised when a search result is opened - the shell scrolls the group with this anchor
    /// <c>Tag</c> into view and pulses it. Argument is <see cref="PreferenceIndexEntry.AnchorKey"/>.</summary>
    public event Action<string>? ScrollToAnchorRequested;

    /// <summary>Public wrapper so callers outside this class (MainViewModel's deep-links) can trigger
    /// the same scroll+pulse a search result or a tile click does - an event can't be raised from
    /// outside its declaring class.</summary>
    public void RequestScrollToAnchor(string anchorKey) => ScrollToAnchorRequested?.Invoke(anchorKey);

    /// <summary>Raised by the About section's "What's New" button - MainViewModel opens the
    /// WhatsNewOverlay scoped to the current release (docs/superpowers/specs/2026-09-09-startup-
    /// onboarding-whats-new-design.md, Decision 6). Same wire-up pattern as
    /// <see cref="ReaderDisplaySettingsChanged"/>.</summary>
    public event Action? WhatsNewRequested;

    [RelayCommand]
    private void OpenWhatsNew() => WhatsNewRequested?.Invoke();

    partial void OnSearchQueryChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearching));

        SearchResults.Clear();
        string q = value?.Trim() ?? string.Empty;
        if (q.Length > 0)
        {
            foreach (var entry in PreferenceIndex.Entries)
            {
                if (MatchesSearch(entry, q))
                {
                    SearchResults.Add(new PreferenceSearchResultViewModel(entry));
                }
            }
        }

        OnPropertyChanged(nameof(NoSearchResults));
    }

    private static bool MatchesSearch(PreferenceIndexEntry entry, string query)
    {
        const StringComparison ci = StringComparison.OrdinalIgnoreCase;
        if (PreferencesSectionMeta.Label(entry.Section).Contains(query, ci)
            || entry.GroupTitle.Contains(query, ci)
            || entry.Title.Contains(query, ci))
        {
            return true;
        }

        foreach (string keyword in entry.Keywords)
        {
            if (keyword.Contains(query, ci))
            {
                return true;
            }
        }

        return false;
    }

    [RelayCommand]
    private void OpenSearchResult(PreferenceSearchResultViewModel? result)
    {
        if (result is null)
        {
            return;
        }

        ActiveSection = result.Section;
        SearchQuery = string.Empty;
        ScrollToAnchorRequested?.Invoke(result.AnchorKey);
    }

    [RelayCommand]
    private void ClearSearch() => SearchQuery = string.Empty;

    [ObservableProperty]
    private string? _selectedFontFamily;

    /// <summary>docs/superpowers/specs/2026-08-24-design-language-foundation-design.md - shortens UI transitions to effectively instant when true.</summary>
    [ObservableProperty]
    private bool _reducedMotion;

    [ObservableProperty]
    private bool _openLastPage;

    [ObservableProperty]
    private bool _autoNavigateComics;

    [ObservableProperty]
    private bool _reverseRtlNavigation;

    // Behavior settings, second batch (docs/superpowers/specs/2026-09-04-behavior-settings-batch2-
    // design.md) - General tab. Same immediate-persist shape as the toggles above.

    /// <summary>CE <c>Settings.OpenLastFile</c> - gates <see cref="MainViewModel.RestoreLastScreen"/>.</summary>
    [ObservableProperty]
    private bool _restoreSessionOnStartup;

    /// <summary>CE <c>Settings.AutoShowQuickReview</c> - auto-opens Quick Rate at the end of a book.</summary>
    [ObservableProperty]
    private bool _promptReviewOnFinish;

    /// <summary>CE <c>Settings.DisableDragDrop</c> (inverted) - gates the Library / Reading List drop handlers.</summary>
    [ObservableProperty]
    private bool _enableDragDropImport;

    /// <summary>
    /// Appearance tab toggle (docs/superpowers/specs/2026-08-24-navigation-shell-motion-system-
    /// design.md's hover-expand mechanism) - gates whether hovering the collapsed nav rail
    /// temporarily expands it. Read fresh from AppSettings by MainWindow.axaml.cs's
    /// RailPointerEntered on every hover rather than proxied through this VM, so it's correct
    /// immediately even if Preferences has never been opened this session (this property is only
    /// for the checkbox's own display/edit, not the live gate). Default true preserves today's
    /// behavior for existing installs.
    /// </summary>
    [ObservableProperty]
    private bool _navRailHoverExpandEnabled;

    /// <summary>docs/superpowers/specs/2026-09-01-auto-update-and-changelog-design.md - persisted opt-out, also settable from the update-available overlay's own checkbox.</summary>
    [ObservableProperty]
    private bool _checkForUpdatesOnStartup;

    /// <summary>
    /// Advanced tab toggle (docs/superpowers/specs/2026-08-23-app-chrome-crash-reporter-and-tray-
    /// design.md §4) - lives here alongside the other AppSettings-backed toggles even though its UI
    /// checkbox is on the Advanced tab, not Behavior, matching this property block's existing
    /// "grouped by storage, not by tab" shape. CE default false (<c>Settings.MinimizeToTray</c>).
    /// </summary>
    [ObservableProperty]
    private bool _minimizeToTray;

    [ObservableProperty]
    private bool _highQualityPageDisplay;

    /// <summary>docs/superpowers/specs/2026-08-10-preferences-reader-tab-design.md - CE: <c>Settings.ResetZoomOnPageChange</c>, default false.</summary>
    [ObservableProperty]
    private bool _resetZoomOnPageChange;

    /// <summary>CE: <c>Settings.MouseWheelSpeed</c> ("lines per mouse scrolling"), default 2.0, CE's own UI range 0.5-5.0.</summary>
    [ObservableProperty]
    private double _mouseWheelSpeed = 2.0;

    /// <summary>Global default for a book with no <see cref="Issue.PageFitModeOverride"/> - not a CE setting, closes the TODO docs/superpowers/specs/2026-08-10-reader-polish-core-viewing-controls-design.md §3 left pending this tab's existence.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefaultPageFitModeText))]
    private ImageFitMode _defaultPageFitMode = ImageFitMode.FitWidth;

    [ObservableProperty]
    private bool _defaultAutoRotate;

    /// <summary>Global default for a book with no <see cref="Series.PageLayoutMode"/>/<see cref="Issue.PageLayoutModeOverride"/> set (docs/superpowers/specs/2026-08-15-reader-double-page-spread-design.md §2).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefaultPageLayoutModeText))]
    private PageLayoutMode _defaultPageLayoutMode = PageLayoutMode.Single;

    /// <summary>docs/superpowers/specs/2026-08-13-reader-page-transition-animations-design.md §2 - default <see cref="PageTransitionStyle.None"/>, matching CE's own <c>BlendWhilePaging</c> default of <c>false</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTransitionStyleText))]
    private PageTransitionStyle _pageTransitionStyle = PageTransitionStyle.None;

    [ObservableProperty]
    private int _pageTransitionDurationMs = 250;

    /// <summary>Global default live-adjustment values (docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §9), additive with each Issue's own override - see ReaderScreenViewModel.Brightness's own doc comment for the split. -100..100, CE's exact PreferencesDialog trackbar range.</summary>
    [ObservableProperty]
    private double _defaultBrightness;

    [ObservableProperty]
    private double _defaultContrast;

    [ObservableProperty]
    private double _defaultSaturation;

    [ObservableProperty]
    private double _defaultGamma;

    /// <summary>Global-only (docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §10) - no per-Issue override, CE default <c>Color</c> (confirmed from <c>DisplayWorkspace.cs</c>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImageBackgroundModeText))]
    private ImageBackgroundMode _imageBackgroundMode = ImageBackgroundMode.Color;

    /// <summary>CE default "WhiteSmoke" (<c>DisplayWorkspace.BackgroundColor</c>) - a named or hex color string, parsed by <c>ReaderScreenViewModel.ComputeCanvasBackgroundBrush</c>.</summary>
    [ObservableProperty]
    private string _backgroundColor = "WhiteSmoke";

    /// <summary>
    /// Which bundled texture backs <see cref="ImageBackgroundMode.Texture"/> (docs/superpowers/specs/
    /// 2026-09-10-reader-backlog-batch-b-design.md Item 1) - a texture id, resolved via
    /// <see cref="ReaderBackgroundTextures.Resolve"/> (null/unknown -&gt; the first texture).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextureNeutralDark))]
    [NotifyPropertyChangedFor(nameof(IsTextureCarbon))]
    [NotifyPropertyChangedFor(nameof(IsTextureLinen))]
    private string? _backgroundTexture;

    public bool IsTextureNeutralDark => ReaderBackgroundTextures.Resolve(BackgroundTexture).Id == "neutral-dark";
    public bool IsTextureCarbon => ReaderBackgroundTextures.Resolve(BackgroundTexture).Id == "carbon";
    public bool IsTextureLinen => ReaderBackgroundTextures.Resolve(BackgroundTexture).Id == "linen";

    [RelayCommand]
    private void SetBackgroundTexture(string id)
    {
        if (BackgroundTexture == id)
        {
            return;
        }

        BackgroundTexture = id; // generated setter raises the three NotifyPropertyChangedFor
        PersistBehaviorSetting(s => s.BackgroundTexture = id);
        ReaderDisplaySettingsChanged?.Invoke();
    }

    /// <summary>CE default false (<c>DisplayWorkspace.PageMargin</c>).</summary>
    [ObservableProperty]
    private bool _pageMarginEnabled;

    /// <summary>CE default 0.05 (<c>DisplayWorkspace.PageMarginPercentWidth</c>), CE's own trackbar likely a 0..1 fraction - matches <see cref="MouseWheelSpeed"/>'s "raw AppSettings unit, no UI-side rescale" precedent.</summary>
    [ObservableProperty]
    private double _pageMarginPercentWidth = 0.05;

    /// <summary>
    /// Avalonia GPU rendering backend (docs/superpowers/specs/2026-08-27-hardware-accelerated-
    /// rendering-design.md). Restart-only: changing it persists to <see cref="AppSettings"/> and
    /// the <c>graphics.json</c> bootstrap cache immediately, but only takes effect on next launch.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RenderingBackendText))]
    private RenderBackend _renderingBackend = RenderBackend.Auto;

    /// <summary>See <see cref="RenderingBackend"/> - tries native OpenGL (WGL) before ANGLE when true.</summary>
    [ObservableProperty]
    private bool _preferNativeOpenGl;

    /// <summary>
    /// User direction: CE's own background-color picker (<c>ComicDisplaySettingsDialog</c>'s
    /// <c>cpBackgroundColor.FillKnownColors(includingSystem: false)</c>) fills a full swatch list off
    /// every named .NET color - a curated subset here instead of porting that whole list verbatim, as
    /// a dropdown rather than swatch buttons (user direction). A full color-picker control is a
    /// reasonable future addition but out of scope for this pass; the free-text field alongside this
    /// dropdown still accepts any named or hex color directly, including one not in this preset list.
    /// </summary>
    public static readonly string[] BackgroundColorPresets = ["White", "WhiteSmoke", "Beige", "Wheat", "LightGray", "Gray", "DarkSlateGray", "Black"];

    /// <summary>
    /// Every registered <see cref="KeyboardCommandRegistry"/> command, split into three UI
    /// sections by <see cref="KeyboardCommandDescriptor.Group"/> - data-driven so a future command
    /// needs no Preferences-side change beyond a new registry entry - see
    /// <see cref="KeyboardCommandRegistry"/>'s remarks.
    /// </summary>
    public ObservableCollection<KeyBindingRowViewModel> NavigationKeyBindings { get; }

    public ObservableCollection<KeyBindingRowViewModel> ZoomFitKeyBindings { get; }

    public ObservableCollection<KeyBindingRowViewModel> DisplayKeyBindings { get; }

    [ObservableProperty]
    private string? _keyBindingConflictError;

    public bool HasKeyBindingConflictError => !string.IsNullOrEmpty(KeyBindingConflictError);

    partial void OnKeyBindingConflictErrorChanged(string? value) => OnPropertyChanged(nameof(HasKeyBindingConflictError));

    [ObservableProperty]
    private string? _installSkinError;

    public bool HasInstallSkinError => !string.IsNullOrEmpty(InstallSkinError);

    partial void OnInstallSkinErrorChanged(string? value) => OnPropertyChanged(nameof(HasInstallSkinError));

    partial void OnActiveSectionChanged(PreferencesSection value)
    {
        OnPropertyChanged(nameof(IsGeneralSection));
        OnPropertyChanged(nameof(IsAppearanceSection));
        OnPropertyChanged(nameof(IsLibrarySection));
        OnPropertyChanged(nameof(IsAutomationSection));
        OnPropertyChanged(nameof(IsReaderSection));
        OnPropertyChanged(nameof(IsKeyboardShortcutsSection));
        OnPropertyChanged(nameof(IsConnectionsSection));
        OnPropertyChanged(nameof(IsPluginsSection));
        OnPropertyChanged(nameof(IsAdvancedSection));
        OnPropertyChanged(nameof(IsAboutSection));
    }

    [RelayCommand]
    private void GoGeneral() => ActiveSection = PreferencesSection.General;

    [RelayCommand]
    private void GoAppearance() => ActiveSection = PreferencesSection.Appearance;

    [RelayCommand]
    private void GoLibrary() => ActiveSection = PreferencesSection.Library;

    [RelayCommand]
    private void GoAutomation() => ActiveSection = PreferencesSection.Automation;

    /// <summary>
    /// Library Health lives inside the Library tab, not as its own section (per user decision,
    /// 2026-09-06) - deep-links (the missing-files alert, the startup path-repair alert) still need
    /// somewhere to land, so this switches to Library and scrolls/pulses the Library Health group,
    /// same mechanism <see cref="OpenSearchResult"/> uses for a search hit.
    /// </summary>
    [RelayCommand]
    private void GoLibraryHealth()
    {
        ActiveSection = PreferencesSection.Library;
        ScrollToAnchorRequested?.Invoke("library.health");
    }

    [RelayCommand]
    private void GoReader() => ActiveSection = PreferencesSection.Reader;

    [RelayCommand]
    private void GoKeyboardShortcuts() => ActiveSection = PreferencesSection.KeyboardShortcuts;

    [RelayCommand]
    private void GoConnections() => ActiveSection = PreferencesSection.Connections;

    [RelayCommand]
    private void GoPlugins() => ActiveSection = PreferencesSection.Plugins;

    [RelayCommand]
    private void GoAdvanced() => ActiveSection = PreferencesSection.Advanced;

    [RelayCommand]
    private void GoAbout() => ActiveSection = PreferencesSection.About;

    /// <summary>Lazily loads skins/fonts the first time the screen is navigated to, same pattern as SmartScreenViewModel/ReadingScreenViewModel's EnsureListLoaded.</summary>
    public void EnsureLoaded()
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        Reload();
    }

    private void Reload()
    {
        RefreshChangelog();
        RefreshSkins();

        FontFamilies.Clear();
        foreach (string family in _skinService.GetInstalledFontFamilies())
        {
            FontFamilies.Add(family);
        }

        _suppressFontApply = true;
        SelectedFontFamily = _skinService.GetSelectedFontFamily() ?? "System Default";
        _suppressFontApply = false;

        _suppressMotionApply = true;
        ReducedMotion = _skinService.GetReducedMotion();
        _suppressMotionApply = false;

        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        _suppressBehaviorApply = true;
        OpenLastPage = settings.OpenLastPage;
        AutoNavigateComics = settings.AutoNavigateComics;
        ReverseRtlNavigation = settings.ReverseRtlNavigation;
        RestoreSessionOnStartup = settings.RestoreSessionOnStartup;
        PromptReviewOnFinish = settings.PromptReviewOnFinish;
        EnableDragDropImport = settings.EnableDragDropImport;
        NavRailHoverExpandEnabled = settings.NavRailHoverExpandEnabled;
        CheckForUpdatesOnStartup = settings.CheckForUpdatesOnStartup;
        MinimizeToTray = settings.MinimizeToTray;
        HighQualityPageDisplay = settings.HighQualityPageDisplay;
        ResetZoomOnPageChange = settings.ResetZoomOnPageChange;
        MouseWheelSpeed = settings.MouseWheelSpeed;
        DefaultPageFitMode = settings.DefaultPageFitMode;
        DefaultAutoRotate = settings.DefaultAutoRotate;
        DefaultPageLayoutMode = settings.DefaultPageLayoutMode;
        PageTransitionStyle = settings.PageTransitionStyle;
        PageTransitionDurationMs = settings.PageTransitionDurationMs;
        DefaultBrightness = settings.DefaultBrightness;
        DefaultContrast = settings.DefaultContrast;
        DefaultSaturation = settings.DefaultSaturation;
        DefaultGamma = settings.DefaultGamma;
        ImageBackgroundMode = settings.ImageBackgroundMode;
        BackgroundColor = settings.BackgroundColor;
        BackgroundTexture = settings.BackgroundTexture;
        PageMarginEnabled = settings.PageMarginEnabled;
        PageMarginPercentWidth = settings.PageMarginPercentWidth;
        RenderingBackend = settings.RenderingBackend;
        PreferNativeOpenGl = settings.PreferNativeOpenGl;
        WriteMetadataToFiles = settings.WriteMetadataToFiles;
        WriteMetadataAutomatically = settings.WriteMetadataAutomatically;
        WriteNativeSidecar = settings.WriteNativeSidecar;
        AutoRemoveMissingOnScan = settings.AutoRemoveMissingOnScan;
        DontReimportRemovedFiles = settings.DontReimportRemovedFiles;
        LibraryHealthConfirmedMissingThreshold = settings.LibraryHealthConfirmedMissingThreshold;
        _suppressBehaviorApply = false;

        var firstIssue = context.Issues.Include(i => i.Series).OrderBy(i => i.Id).FirstOrDefault();
        if (firstIssue is not null)
        {
            _previewIssue = firstIssue;
            _previewSeries = firstIssue.Series;
        }

        RefreshVirtualTags();
        RefreshWatchedFolders();
        RefreshBookFolders();
        RefreshFileAssociations();

        _suppressBackupSettingsApply = true;
        BackupLocation = _backupService.GetBackupLocation();
        BackupsToKeep = _backupService.GetBackupsToKeep();
        AutoBackupEnabled = _backupService.GetAutoBackupEnabled();
        AutoBackupMinIntervalHours = _backupService.GetAutoBackupMinIntervalHours();
        _suppressBackupSettingsApply = false;
        RefreshBackups();

        RefreshKeyBindings();
        RefreshSourceCredentials(context);
        RefreshTrackerConnectionState(context);
        RefreshLibraryHealth(context);
    }

    // ===================== Keyboard Shortcuts (docs/Paperbunkr-Roadmap.md P5 follow-up) =====================

    private void RefreshKeyBindings()
    {
        NavigationKeyBindings.Clear();
        ZoomFitKeyBindings.Clear();
        DisplayKeyBindings.Clear();

        foreach (var (command, currentKeys) in _keyBindingService.GetAllBindings())
        {
            var row = new KeyBindingRowViewModel(command, currentKeys, _keyBindingService, RecomputeKeyBindingConflict);
            var targetCollection = command.Group switch
            {
                KeyboardCommandRegistry.NavigationGroup => NavigationKeyBindings,
                KeyboardCommandRegistry.ZoomFitGroup => ZoomFitKeyBindings,
                KeyboardCommandRegistry.DisplayGroup => DisplayKeyBindings,
                _ => throw new InvalidOperationException($"Unrecognized keyboard command group \"{command.Group}\" - add a matching Preferences section for it."),
            };
            targetCollection.Add(row);
        }

        RecomputeKeyBindingConflict();
    }

    /// <summary>Real gap closed, not a restyle (docs/superpowers/specs/2026-08-25-reader-chrome-
    /// design.md) - confirmed via grep this never existed anywhere in the codebase before. Mirrors
    /// ReadingScreenViewModel's ImportCbl/ExportCbl shape exactly (same _filePicker calls, same
    /// open-context-then-call-IO-class structure).</summary>
    [RelayCommand]
    private async Task ImportKeyBindings()
    {
        string? path = await _filePicker.PickOpenFileAsync("Import Keyboard Shortcuts", "json", "Keyboard Shortcut Layout");
        if (path is null)
        {
            return;
        }

        int applied;
        try
        {
            applied = KeyBindingIO.Import(_keyBindingService, path);
        }
        catch (InvalidDataException ex)
        {
            _showToast("Couldn't import keyboard shortcuts", ex.Message);
            return;
        }

        RefreshKeyBindings();
        _showToast("Keyboard shortcuts imported", $"Applied {applied} binding{(applied == 1 ? "" : "s")}.");
    }

    [RelayCommand]
    private async Task ExportKeyBindings()
    {
        string? path = await _filePicker.PickSaveFileAsync("Export Keyboard Shortcuts", "paperbunkr-shortcuts.json", "json", "Keyboard Shortcut Layout");
        if (path is null)
        {
            return;
        }

        KeyBindingIO.Export(_keyBindingService, path);
        _showToast("Keyboard shortcuts exported", $"Saved to {path}.");
    }

    /// <summary>
    /// Whole-layout revert (docs/superpowers/specs/2026-09-07-keyboard-shortcuts-redesign-design.md)
    /// - matches CE's own "Restore Default Keyboard Layout" menu action. No confirmation dialog:
    /// this section already applies every change immediately with no Save/Cancel step, and the
    /// action is fully recoverable via Import if the user has a previously-exported layout.
    /// </summary>
    [RelayCommand]
    private void ResetKeyBindings()
    {
        _keyBindingService.ResetToDefaults();
        RefreshKeyBindings();
        _showToast("Keyboard shortcuts reset", "Every shortcut is back to its default.");
    }

    /// <summary>
    /// Soft validation, not a hard block - the row already persisted its new key by the time this
    /// runs (matches every other Preferences toggle's immediate-persist behavior). Two commands
    /// conflict iff any of their gestures are equal AND (their <see cref="ConflictContext"/>s match
    /// OR either is <see cref="ConflictContext.Always"/>) - see that enum's own doc comment for why:
    /// mode-specific contexts (paged/zoomed/continuous) are mutually exclusive at runtime, so
    /// sharing a gesture across two of them is never actually reachable, but an Always command
    /// unconditionally shadows every mode-specific one it collides with. Every conflicting row gets
    /// <see cref="KeyBindingRowViewModel.IsConflicted"/> flagged (docs/superpowers/specs/2026-09-07-
    /// keyboard-shortcuts-redesign-design.md §4) - the loop doesn't return on the first match so a
    /// row conflicting with more than one other row (more likely now that multi-binding exists)
    /// still ends up correctly flagged, even though only the first conflict's message shows in the
    /// single-line banner.
    /// </summary>
    private void RecomputeKeyBindingConflict()
    {
        var all = NavigationKeyBindings.Concat(ZoomFitKeyBindings).Concat(DisplayKeyBindings).ToList();
        foreach (var row in all)
        {
            row.IsConflicted = false;
        }

        string? firstConflict = null;
        for (int i = 0; i < all.Count; i++)
        {
            for (int j = i + 1; j < all.Count; j++)
            {
                var a = all[i];
                var b = all[j];
                if (a.Context != ConflictContext.Always && b.Context != ConflictContext.Always && a.Context != b.Context)
                {
                    continue;
                }

                foreach (var keyA in a.BoundKeys)
                {
                    foreach (var keyB in b.BoundKeys)
                    {
                        if (keyA.Gesture != keyB.Gesture)
                        {
                            continue;
                        }

                        a.IsConflicted = true;
                        b.IsConflicted = true;
                        firstConflict ??= $"\"{keyA.Label}\" is assigned to both \"{a.Label}\" and \"{b.Label}\".";
                    }
                }
            }
        }

        KeyBindingConflictError = firstConflict;
    }

    /// <summary>
    /// Parses the bundled CHANGELOG.md (copied to output next to the exe - see the csproj's
    /// CopyToOutputDirectory item) via <see cref="ChangelogParser"/>. Missing file (e.g. a dev build
    /// run before the csproj copy step) just leaves the list empty rather than throwing - the About
    /// section still shows the current version either way.
    /// </summary>
    private void RefreshChangelog()
    {
        ChangelogEntries.Clear();
        string path = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var entry in ChangelogParser.Parse(File.ReadAllText(path)))
        {
            ChangelogEntries.Add(entry);
        }
    }

    [ObservableProperty]
    private string? _selectedLegalDocumentTitle;

    [ObservableProperty]
    private IReadOnlyList<LegalDocumentBlock> _selectedLegalDocumentBlocks = Array.Empty<LegalDocumentBlock>();

    [ObservableProperty]
    private bool _isLegalDocumentViewerOpen;

    /// <summary>
    /// Opens one of the repo-root legal/community documents (LICENSE, PRIVACY.md, TERMS.md,
    /// COMICVINE_NOTICE.md) bundled next to the exe (see the csproj's CopyToOutputDirectory items)
    /// in the in-app viewer overlay (docs/superpowers/specs/2026-09-07-about-redesign-design.md) -
    /// no external-launch path is kept. Same "missing file just does nothing" tolerance as
    /// <see cref="RefreshChangelog"/> - a dev build run before the csproj copy step shouldn't crash.
    /// </summary>
    [RelayCommand]
    private void OpenLegalDocument(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
        {
            return;
        }

        SelectedLegalDocumentTitle = fileName switch
        {
            "LICENSE" => "License",
            "PRIVACY.md" => "Privacy notice",
            "TERMS.md" => "Terms of use",
            "COMICVINE_NOTICE.md" => "ComicVine API notice",
            _ => fileName,
        };
        SelectedLegalDocumentBlocks = LegalDocumentParser.Parse(File.ReadAllText(path));
        IsLegalDocumentViewerOpen = true;
    }

    [RelayCommand]
    private void CloseLegalDocumentViewer() => IsLegalDocumentViewerOpen = false;

    private void RefreshSkins()
    {
        Skins.Clear();
        foreach (var skin in _skinService.GetAvailableSkins())
        {
            Skins.Add(skin);
        }
    }

    [RelayCommand]
    private void SelectSkin(SkinSummary skin)
    {
        _skinService.ApplySkin(skin.Key);
        RefreshSkins();
    }

    partial void OnSelectedFontFamilyChanged(string? value)
    {
        if (_suppressFontApply || value is null)
        {
            return;
        }

        _skinService.ApplyFont(value);
    }

    partial void OnReducedMotionChanged(bool value)
    {
        if (_suppressMotionApply)
        {
            return;
        }

        _skinService.ApplyReducedMotion(value);
    }

    partial void OnOpenLastPageChanged(bool value) => PersistBehaviorSetting(s => s.OpenLastPage = value);

    partial void OnAutoNavigateComicsChanged(bool value) => PersistBehaviorSetting(s => s.AutoNavigateComics = value);

    partial void OnReverseRtlNavigationChanged(bool value) => PersistBehaviorSetting(s => s.ReverseRtlNavigation = value);

    partial void OnRestoreSessionOnStartupChanged(bool value) => PersistBehaviorSetting(s => s.RestoreSessionOnStartup = value);

    partial void OnPromptReviewOnFinishChanged(bool value) => PersistBehaviorSetting(s => s.PromptReviewOnFinish = value);

    partial void OnEnableDragDropImportChanged(bool value) => PersistBehaviorSetting(s => s.EnableDragDropImport = value);

    partial void OnNavRailHoverExpandEnabledChanged(bool value) => PersistBehaviorSetting(s => s.NavRailHoverExpandEnabled = value);

    partial void OnCheckForUpdatesOnStartupChanged(bool value) => PersistBehaviorSetting(s => s.CheckForUpdatesOnStartup = value);

    partial void OnMinimizeToTrayChanged(bool value) => PersistBehaviorSetting(s => s.MinimizeToTray = value);

    partial void OnHighQualityPageDisplayChanged(bool value) => PersistBehaviorSetting(s => s.HighQualityPageDisplay = value);

    partial void OnResetZoomOnPageChangeChanged(bool value) => PersistBehaviorSetting(s => s.ResetZoomOnPageChange = value);

    partial void OnMouseWheelSpeedChanged(double value) => PersistBehaviorSetting(s => s.MouseWheelSpeed = value);

    partial void OnDefaultAutoRotateChanged(bool value) => PersistBehaviorSetting(s => s.DefaultAutoRotate = value);

    partial void OnDefaultPageLayoutModeChanged(PageLayoutMode value) => PersistBehaviorSetting(s => s.DefaultPageLayoutMode = value);

    /// <summary>
    /// Real bug, found via manual testing: without <see cref="ReaderDisplaySettingsChanged"/>, this
    /// setting only took effect on the *next* book opened (or by switching reading mode, which forces
    /// a fresh <c>Load</c> as a side effect) - same live-while-open treatment as
    /// <see cref="OnPageMarginEnabledChanged"/>/<see cref="OnBackgroundColorChanged"/> below, since a
    /// book can already be open in the Reader (screens aren't destroyed by the rail-nav switcher) when
    /// this is changed.
    /// </summary>
    partial void OnPageTransitionStyleChanged(PageTransitionStyle value)
    {
        PersistBehaviorSetting(s => s.PageTransitionStyle = value);
        ReaderDisplaySettingsChanged?.Invoke();
    }

    partial void OnPageTransitionDurationMsChanged(int value)
    {
        PersistBehaviorSetting(s => s.PageTransitionDurationMs = value);
        ReaderDisplaySettingsChanged?.Invoke();
    }

    partial void OnDefaultBrightnessChanged(double value) => PersistBehaviorSetting(s => s.DefaultBrightness = value);

    partial void OnDefaultContrastChanged(double value) => PersistBehaviorSetting(s => s.DefaultContrast = value);

    partial void OnDefaultSaturationChanged(double value) => PersistBehaviorSetting(s => s.DefaultSaturation = value);

    partial void OnDefaultGammaChanged(double value) => PersistBehaviorSetting(s => s.DefaultGamma = value);

    /// <summary>
    /// Real bug, found via manual testing: <see cref="ReaderScreenViewModel"/> only ever read
    /// background/margin inside its own <c>Load</c> - fine for a value scoped to opening a book, but
    /// these four are edited from here while the Reader may already have a book open and staying
    /// open (the rail-nav switcher never destroys/recreates screens), so cycling through colors
    /// appeared to "get stuck" without this. <see cref="MainViewModel"/> wires
    /// <see cref="ReaderScreenViewModel.RefreshDisplaySettings"/> to this event once, at construction.
    /// </summary>
    public event Action? ReaderDisplaySettingsChanged;

    partial void OnImageBackgroundModeChanged(ImageBackgroundMode value)
    {
        PersistBehaviorSetting(s => s.ImageBackgroundMode = value);
        ReaderDisplaySettingsChanged?.Invoke();
    }

    partial void OnBackgroundColorChanged(string value)
    {
        PersistBehaviorSetting(s => s.BackgroundColor = value);
        ReaderDisplaySettingsChanged?.Invoke();
    }

    partial void OnPageMarginEnabledChanged(bool value)
    {
        PersistBehaviorSetting(s => s.PageMarginEnabled = value);
        ReaderDisplaySettingsChanged?.Invoke();
    }

    partial void OnPageMarginPercentWidthChanged(double value)
    {
        PersistBehaviorSetting(s => s.PageMarginPercentWidth = value);
        ReaderDisplaySettingsChanged?.Invoke();
    }

    /// <summary>Plain <c>ComboBox</c> + changed-hook, matching this class's existing <c>SelectedFontFamily</c> picker shape rather than the Reader screen's flyout-of-buttons (that shape fits a toolbar button, not a Preferences row).</summary>
    partial void OnDefaultPageFitModeChanged(ImageFitMode value) => PersistBehaviorSetting(s => s.DefaultPageFitMode = value);

    private void PersistBehaviorSetting(Action<AppSettings> apply)
    {
        if (_suppressBehaviorApply)
        {
            return;
        }

        using var context = _contextFactory();
        apply(context.GetOrCreateAppSettings());
        context.SaveChanges();
    }

    partial void OnRenderingBackendChanged(RenderBackend value) => PersistRenderingSetting();

    partial void OnPreferNativeOpenGlChanged(bool value) => PersistRenderingSetting();

    // --- File metadata write-back (docs/superpowers/specs/2026-09-03-file-metadata-write-back-
    // design.md). CE parity: three checkboxes, all default off; the lower two are only meaningful
    // when the master is on (the XAML binds their IsEnabled to WriteMetadataToFiles). ---

    [ObservableProperty]
    private bool _writeMetadataToFiles;

    [ObservableProperty]
    private bool _writeMetadataAutomatically;

    [ObservableProperty]
    private bool _writeNativeSidecar;

    [ObservableProperty]
    private string _writeAllMetadataStatus = string.Empty;

    partial void OnWriteMetadataToFilesChanged(bool value) => PersistBehaviorSetting(s => s.WriteMetadataToFiles = value);

    partial void OnWriteMetadataAutomaticallyChanged(bool value) => PersistBehaviorSetting(s => s.WriteMetadataAutomatically = value);

    partial void OnWriteNativeSidecarChanged(bool value) => PersistBehaviorSetting(s => s.WriteNativeSidecar = value);

    /// <summary>
    /// CE's <c>MainForm.UpdateComics()</c> - queue a write for every filed issue in the library,
    /// regardless of the automatic toggle. The write-back queue serialises them and shows its own
    /// summary toast when done.
    /// </summary>
    [RelayCommand]
    private void WriteAllMetadataToFiles()
    {
        if (!WriteMetadataToFiles)
        {
            return;
        }

        int count;
        using (var context = _contextFactory())
        {
            var ids = context.Issues.Where(i => i.FilePath != null && !i.IsPlaceholder).Select(i => i.Id).ToList();
            count = ids.Count;
            foreach (int id in ids)
            {
                _enqueueMetadataWriteBack(id, true);
            }
        }

        WriteAllMetadataStatus = count == 0
            ? "No files to write."
            : $"Queued {count} file{(count == 1 ? "" : "s")} - you'll get a summary when it finishes.";
    }

    /// <summary>
    /// Persists both rendering fields to <see cref="AppSettings"/> and immediately syncs the
    /// <c>graphics.json</c> bootstrap cache (docs/superpowers/specs/2026-08-27-hardware-accelerated-
    /// rendering-design.md §2) so there's no launch lag - the change still only takes effect on the
    /// next launch, which the UI states.
    /// </summary>
    private void PersistRenderingSetting()
    {
        if (_suppressBehaviorApply)
        {
            return;
        }

        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        settings.RenderingBackend = RenderingBackend;
        settings.PreferNativeOpenGl = PreferNativeOpenGl;
        context.SaveChanges();

        GraphicsBootstrap.SyncCache(RenderingBackend, PreferNativeOpenGl);
    }

    [RelayCommand]
    private async Task BrowseForSkin()
    {
        string? path = await _filePicker.PickOpenFileAsync("Install Skin", "crpck", "Paperbunkr Skin");
        if (path is null)
        {
            return;
        }

        if (_skinService.TryInstallSkin(path, out string? error))
        {
            InstallSkinError = null;
            RefreshSkins();
        }
        else
        {
            InstallSkinError = error;
        }
    }

    [RelayCommand]
    private void OpenSkinsFolder() => _skinService.OpenSkinsFolder();

    // ===================== Virtual Tags (docs/superpowers/specs/2026-08-07-preferences-libraries-tab-design.md §1) =====================

    [ObservableProperty]
    private int? _selectedVirtualTagId;

    [ObservableProperty]
    private string _virtualTagName = string.Empty;

    [ObservableProperty]
    private string _virtualTagCaptionFormat = string.Empty;

    [ObservableProperty]
    private bool _virtualTagIsEnabled = true;

    [ObservableProperty]
    private string _virtualTagPreview = string.Empty;

    public bool HasSelectedVirtualTag => SelectedVirtualTagId is not null;

    public bool HasVirtualTags => VirtualTags.Count > 0;

    partial void OnSelectedVirtualTagIdChanged(int? value) => OnPropertyChanged(nameof(HasSelectedVirtualTag));

    private void RefreshVirtualTags()
    {
        using var context = _contextFactory();
        VirtualTags.Clear();
        foreach (var tag in context.VirtualTagDefinitions.OrderBy(v => v.SortOrder))
        {
            VirtualTags.Add(new VirtualTagSummary
            {
                Id = tag.Id,
                Name = tag.Name,
                IsEnabled = tag.IsEnabled,
                IsSelected = tag.Id == SelectedVirtualTagId,
            });
        }

        OnPropertyChanged(nameof(HasVirtualTags));
    }

    private void RefreshVirtualTagPreview() => VirtualTagPreview = VirtualTagTemplateEvaluator.Evaluate(VirtualTagCaptionFormat, _previewIssue, _previewSeries);

    [RelayCommand]
    private void SelectVirtualTag(VirtualTagSummary tag)
    {
        using var context = _contextFactory();
        var full = context.VirtualTagDefinitions.FirstOrDefault(v => v.Id == tag.Id);
        if (full is null)
        {
            return;
        }

        _suppressVirtualTagApply = true;
        SelectedVirtualTagId = full.Id;
        VirtualTagName = full.Name;
        VirtualTagCaptionFormat = full.CaptionFormat;
        VirtualTagIsEnabled = full.IsEnabled;
        _suppressVirtualTagApply = false;

        RefreshVirtualTagPreview();
        RefreshVirtualTags();
    }

    [RelayCommand]
    private void AddVirtualTag()
    {
        using (var context = _contextFactory())
        {
            int nextSort = context.VirtualTagDefinitions.Any() ? context.VirtualTagDefinitions.Max(v => v.SortOrder) + 1 : 0;
            var tag = new VirtualTagDefinition { Name = "New Tag", CaptionFormat = "{Series}", IsEnabled = true, SortOrder = nextSort };
            context.VirtualTagDefinitions.Add(tag);
            context.SaveChanges();
        }

        RefreshVirtualTags();
        SelectVirtualTag(VirtualTags.Last());
    }

    [RelayCommand]
    private void DeleteVirtualTag()
    {
        if (SelectedVirtualTagId is int id)
        {
            using var context = _contextFactory();
            var tag = context.VirtualTagDefinitions.FirstOrDefault(v => v.Id == id);
            if (tag is not null)
            {
                context.VirtualTagDefinitions.Remove(tag);
                context.SaveChanges();
            }
        }

        _suppressVirtualTagApply = true;
        SelectedVirtualTagId = null;
        VirtualTagName = string.Empty;
        VirtualTagCaptionFormat = string.Empty;
        VirtualTagIsEnabled = true;
        _suppressVirtualTagApply = false;

        RefreshVirtualTagPreview();
        RefreshVirtualTags();
    }

    partial void OnVirtualTagNameChanged(string value) => PersistVirtualTag(v => v.Name = value, refreshList: true);

    partial void OnVirtualTagCaptionFormatChanged(string value)
    {
        PersistVirtualTag(v => v.CaptionFormat = value, refreshList: false);
        RefreshVirtualTagPreview();
    }

    partial void OnVirtualTagIsEnabledChanged(bool value) => PersistVirtualTag(v => v.IsEnabled = value, refreshList: true);

    private void PersistVirtualTag(Action<VirtualTagDefinition> apply, bool refreshList)
    {
        if (_suppressVirtualTagApply || SelectedVirtualTagId is not int id)
        {
            return;
        }

        using var context = _contextFactory();
        var tag = context.VirtualTagDefinitions.FirstOrDefault(v => v.Id == id);
        if (tag is null)
        {
            return;
        }

        apply(tag);
        context.SaveChanges();

        if (refreshList)
        {
            RefreshVirtualTags();
        }
    }

    // ===================== Comic Library Folders (docs/superpowers/specs/2026-08-07-preferences-libraries-tab-design.md §2)
    // - the comic library's watched source folders (LibraryFolderScanner). Historically mislabeled
    // "Book Folders"; the real novel folders now live in their own region further down. =====================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyComicFolderOperationRunning))]
    private bool _isScanning;

    /// <summary>Whether any Comic Folders operation (Scan Now or a Maintenance-tab action) is
    /// currently running - drives the shared <c>BusyIndicator</c>/button-disabling behavior
    /// (docs/superpowers/specs/2026-09-07-library-folder-management-redesign-design.md §2), since
    /// these 5 operations don't otherwise guard against each other.</summary>
    public bool IsAnyComicFolderOperationRunning =>
        IsScanning || IsGeneratingCovers || IsSyncingMetadata || IsRepairingCovers || IsVerifyingCovers;

    /// <summary>Folder management redesign (docs/superpowers/specs/2026-09-07-library-folder-
    /// management-redesign-design.md) - the currently-running Comic Folders operation (Scan Now or
    /// any Maintenance-tab action), for the shared <c>Controls.BusyIndicator</c> to bind to. Every
    /// one of these operations already auto-toasts on completion via the existing
    /// <see cref="IActivityService"/> pipeline, so this replaces inline status text rather than
    /// supplementing it.</summary>
    [ObservableProperty]
    private ActivityJob? _currentComicFolderJob;

    [ObservableProperty]
    private bool _isComicFoldersMaintenanceTabActive;

    public bool HasWatchedFolders => WatchedFolders.Count > 0;

    [RelayCommand]
    private void ShowComicFoldersFoldersTab() => IsComicFoldersMaintenanceTabActive = false;

    [RelayCommand]
    private void ShowComicFoldersMaintenanceTab() => IsComicFoldersMaintenanceTabActive = true;

    // ===================== Scanning: missing-file handling (docs/superpowers/specs/2026-09-06-
    // scan-missing-file-handling-design.md) - CE parity for the Scanning-section checkboxes,
    // extending Library Health further down with an auto-triggered variant of its own "Remove All
    // Confirmed Missing" plus a permanent removed-files blacklist. =====================

    /// <summary>CE <c>Settings.RemoveMissingFilesOnFullScan</c> - see <see cref="AppSettings.AutoRemoveMissingOnScan"/>'s doc comment for how this deliberately diverges from CE.</summary>
    [ObservableProperty]
    private bool _autoRemoveMissingOnScan;

    /// <summary>CE <c>Settings.DontAddRemoveFiles</c> - see <see cref="AppSettings.DontReimportRemovedFiles"/>'s doc comment.</summary>
    [ObservableProperty]
    private bool _dontReimportRemovedFiles;

    public TwoStepConfirm ClearRemovedFilesListConfirm { get; }

    partial void OnAutoRemoveMissingOnScanChanged(bool value) => PersistBehaviorSetting(s => s.AutoRemoveMissingOnScan = value);

    partial void OnDontReimportRemovedFilesChanged(bool value) => PersistBehaviorSetting(s => s.DontReimportRemovedFiles = value);

    partial void OnLibraryHealthConfirmedMissingThresholdChanged(int value)
    {
        _libraryHealth.ConfirmedMissingThreshold = value;
        PersistBehaviorSetting(s => s.LibraryHealthConfirmedMissingThreshold = value);

        if (_suppressBehaviorApply)
        {
            return;
        }

        using var context = _contextFactory();
        RefreshLibraryHealth(context);
    }

    private void ClearRemovedFilesList()
    {
        using var context = _contextFactory();
        context.RemovedFilePaths.RemoveRange(context.RemovedFilePaths);
        context.SaveChanges();
    }

    private void RefreshWatchedFolders()
    {
        using var context = _contextFactory();
        WatchedFolders.Clear();
        foreach (var folder in context.WatchedFolders.OrderBy(w => w.Path))
        {
            WatchedFolders.Add(new WatchedFolderSummary { Id = folder.Id, Path = folder.Path, Watch = folder.Watch });
        }

        OnPropertyChanged(nameof(HasWatchedFolders));
    }

    [RelayCommand]
    private async Task AddFolder()
    {
        string? path = await _filePicker.PickFolderAsync("Add Comic Library Folder");
        if (path is null)
        {
            return;
        }

        using (var context = _contextFactory())
        {
            if (!context.WatchedFolders.Any(w => w.Path == path))
            {
                context.WatchedFolders.Add(new WatchedFolder { Path = path });
                context.SaveChanges();
            }
        }

        RefreshWatchedFolders();
        _reloadFolderWatch();
    }

    [RelayCommand]
    private async Task RemoveFolder(WatchedFolderSummary folder)
    {
        // Library Health folder-removal hook (docs/superpowers/specs/2026-09-06-missing-files-
        // library-health-design.md): a removed folder's issues previously went silently stale
        // forever - nothing ever re-checked them again. Verify just this folder's issues first, so
        // if the files (and folder) are genuinely gone, they're flagged and counted immediately
        // instead of waiting for someone to notice and run a full Verify later. If the folder is
        // still present on disk (user just stopped tracking it), this is a cheap no-op confirmation
        // pass.
        List<int> scopedIssueIds;
        using (var context = _contextFactory())
        {
            scopedIssueIds = context.Issues
                .Where(i => i.FilePath != null && i.FilePath.StartsWith(folder.Path))
                .Select(i => i.Id)
                .ToList();
        }

        if (scopedIssueIds.Count > 0)
        {
            await _libraryHealth.VerifyAsync(scopedIssueIds, new Progress<(int Done, int Total)>());
        }

        using (var context = _contextFactory())
        {
            var entity = context.WatchedFolders.FirstOrDefault(w => w.Id == folder.Id);
            if (entity is not null)
            {
                context.WatchedFolders.Remove(entity);
                context.SaveChanges();
            }
        }

        RefreshWatchedFolders();
        _reloadFolderWatch();

        using var refreshContext = _contextFactory();
        RefreshLibraryHealth(refreshContext);
    }

    /// <summary>
    /// Bound to the row checkbox's <c>Command</c> (docs/superpowers/specs/
    /// 2026-08-23-live-folder-watch-scanning-design.md §4) - by the time this runs,
    /// <see cref="WatchedFolderSummary.Watch"/> already reflects the post-click state (standard
    /// <c>ToggleButton</c> behavior), so this just persists it and rebuilds the live watchers.
    /// </summary>
    [RelayCommand]
    private void ToggleWatch(WatchedFolderSummary folder)
    {
        using (var context = _contextFactory())
        {
            var entity = context.WatchedFolders.FirstOrDefault(w => w.Id == folder.Id);
            if (entity is not null)
            {
                entity.Watch = folder.Watch;
                context.SaveChanges();
            }
        }

        _reloadFolderWatch();
    }

    [RelayCommand]
    private void OpenFolder(WatchedFolderSummary folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = folder.Path, UseShellExecute = true });
        }
        catch
        {
            // No shell/file-manager available - nothing more we can do.
        }
    }

    // ===================== Book Folders (novels: EPUB/PDF) - moved here from the Books screen so
    // every "populate my library" folder action lives on this tab. Simpler than the comic
    // WatchedFolders above: BookFolder has no live-watch, so no Watch column. =====================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyBookFolderOperationRunning))]
    private bool _isScanningBooks;

    /// <summary>Book Folders' own combined running-flag, same rationale as
    /// <see cref="IsAnyComicFolderOperationRunning"/>.</summary>
    public bool IsAnyBookFolderOperationRunning => IsScanningBooks || IsClearingBookCoverCache;

    /// <summary>Book Folders' own job reference, same rationale as
    /// <see cref="CurrentComicFolderJob"/>.</summary>
    [ObservableProperty]
    private ActivityJob? _currentBookFolderJob;

    [ObservableProperty]
    private bool _isBookFoldersMaintenanceTabActive;

    public bool HasBookFolders => BookFolders.Count > 0;

    [RelayCommand]
    private void ShowBookFoldersFoldersTab() => IsBookFoldersMaintenanceTabActive = false;

    [RelayCommand]
    private void ShowBookFoldersMaintenanceTab() => IsBookFoldersMaintenanceTabActive = true;

    private void RefreshBookFolders()
    {
        using var context = _contextFactory();
        BookFolders.Clear();
        foreach (var folder in context.BookFolders.OrderBy(f => f.Path))
        {
            BookFolders.Add(new BookFolderSummary { Id = folder.Id, Path = folder.Path });
        }

        OnPropertyChanged(nameof(HasBookFolders));
    }

    [RelayCommand]
    private async Task AddBookFolder()
    {
        string? path = await _filePicker.PickFolderAsync("Add Book Folder");
        if (path is null)
        {
            return;
        }

        using (var context = _contextFactory())
        {
            if (!context.BookFolders.Any(f => f.Path == path))
            {
                context.BookFolders.Add(new BookFolder { Path = path });
                context.SaveChanges();
            }
        }

        RefreshBookFolders();
    }

    [RelayCommand]
    private void RemoveBookFolder(BookFolderSummary folder)
    {
        using (var context = _contextFactory())
        {
            var entity = context.BookFolders.FirstOrDefault(f => f.Id == folder.Id);
            if (entity is not null)
            {
                context.BookFolders.Remove(entity);
                context.SaveChanges();
            }
        }

        RefreshBookFolders();
    }

    [RelayCommand]
    private void OpenBookFolder(BookFolderSummary folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = folder.Path, UseShellExecute = true });
        }
        catch
        {
            // No shell/file-manager available - nothing more we can do.
        }
    }

    [RelayCommand]
    private async Task ScanBooksNow()
    {
        if (IsScanningBooks)
        {
            return;
        }

        IsScanningBooks = true;
        using var job = _activity.StartJob(ActivityJobKind.BookScan, "Scanning book folders");
        CurrentBookFolderJob = job.Job;
        try
        {
            var scanProgress = new Progress<(int Done, int Total)>(p => job.Report(p.Done, p.Total, $"{p.Done} / {p.Total} files"));
            var result = await new BookFolderScanService().ScanAllAsync(scanProgress, job.CancellationToken);

            if (result.BooksAdded > 0)
            {
                var coverProgress = new Progress<(int Done, int Total)>(p => job.Report(p.Done, p.Total, $"Covers {p.Done} / {p.Total}"));
                await new BookCoverThumbnailService(_contextFactory).GenerateAllAsync(coverProgress, job.CancellationToken);
            }

            string summary = result.BooksAdded == 0
                ? "No new books found."
                : $"Added {result.BooksAdded} book{(result.BooksAdded == 1 ? "" : "s")} across {result.SeriesTouched} series.";
            job.Succeed(summary, itemsProcessed: result.BooksAdded);
            _scheduler?.NotifyRan(Services.Scheduling.ScheduledTaskCatalog.BookScan, ScheduledRunStatus.Succeeded);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            job.Fail("Book scan failed", ex: ex);
            _scheduler?.NotifyRan(Services.Scheduling.ScheduledTaskCatalog.BookScan, ScheduledRunStatus.Failed);
        }
        finally
        {
            IsScanningBooks = false;
            CurrentBookFolderJob = null;
            RefreshBookFolders();
        }
    }

    [RelayCommand]
    private async Task ScanNow()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        using var job = _activity.StartJob(ActivityJobKind.LibraryScan, "Scanning library folders");
        CurrentComicFolderJob = job.Job;
        try
        {
            var progress = new Progress<(int Done, int Total)>(p => job.Report(p.Done, p.Total, $"{p.Done} / {p.Total} files"));
            var result = await _libraryScanner.ScanAllAsync(progress, job.CancellationToken);
            string summary = result.IssuesAdded == 0
                ? "No new issues found."
                : $"Added {result.IssuesAdded} issue{(result.IssuesAdded == 1 ? "" : "s")} across {result.SeriesTouched} series.";

            if (result.IssuesAdded > 0)
            {
                // Newly-added issues have no cached cover yet - generate them now instead of
                // leaving Library showing blank placeholders until someone finds the separate
                // "Generate Covers" button on the Library screen (same pipeline it uses).
                var coverProgress = new Progress<(int Done, int Total)>(p => job.Report(p.Done, p.Total, $"Covers {p.Done} / {p.Total}"));
                await new CoverThumbnailService(_contextFactory).GenerateAllAsync(coverProgress, job.CancellationToken);
                DuplicateAlertHelper.RaiseIfAny(_activity, result.AddedIssueIds);
            }

            // Auto-remove-missing-on-scan (docs/superpowers/specs/2026-09-06-scan-missing-file-
            // handling-design.md) - Scan Now only, never the live-watch path (LiveFolderWatchService
            // never calls ScanNow). Runs the same full-library VerifyAsync Verify Now uses (Scan Now
            // already covers every library folder, so no new scoped variant needed), then the same
            // two-strikes eligibility ConfirmBulkRemove already uses, plus a drive-reachability
            // guard the unattended path needs that the human-reviewed manual button doesn't.
            bool autoRemoveOnScan;
            using (var settingsContext = _contextFactory())
            {
                autoRemoveOnScan = settingsContext.GetOrCreateAppSettings().AutoRemoveMissingOnScan;
            }

            if (autoRemoveOnScan)
            {
                var healthProgress = new Progress<(int Done, int Total)>(p => job.Report(p.Done, p.Total, $"Verifying {p.Done} / {p.Total}"));
                var healthResult = await _libraryHealth.VerifyAsync(healthProgress, job.CancellationToken);

                using (var healthContext = _contextFactory())
                {
                    healthContext.GetOrCreateAppSettings().LastLibraryHealthVerifyUtc = DateTime.UtcNow;
                    healthContext.SaveChanges();
                }

                if (healthResult.ConfirmedMissingCount > 0)
                {
                    int removedCount;
                    using (var removeContext = _contextFactory())
                    {
                        removedCount = AutoRemoveConfirmedMissing(removeContext);
                    }

                    if (removedCount > 0)
                    {
                        summary += $" Automatically removed {removedCount} confirmed-missing file{(removedCount == 1 ? "" : "s")}.";
                    }
                }

                using var refreshContext = _contextFactory();
                RefreshLibraryHealth(refreshContext);
            }

            job.Succeed(summary, itemsProcessed: result.IssuesAdded);
            _scheduler?.NotifyRan(Services.Scheduling.ScheduledTaskCatalog.LibraryScan, ScheduledRunStatus.Succeeded);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            job.Fail("Library scan failed", ex: ex);
            _scheduler?.NotifyRan(Services.Scheduling.ScheduledTaskCatalog.LibraryScan, ScheduledRunStatus.Failed);
        }
        finally
        {
            IsScanning = false;
            CurrentComicFolderJob = null;
        }
    }

    /// <summary>
    /// The auto-remove-on-scan body (docs/superpowers/specs/2026-09-06-scan-missing-file-handling-
    /// design.md) - same confirmed-missing eligibility <see cref="ConfirmBulkRemove"/> uses, but
    /// unattended: no confirmation dialog, and an additional <see cref="LibraryHealthService.IsPathRootReachable"/>
    /// check <see cref="ConfirmBulkRemove"/> doesn't need (a human already reviews that list before
    /// confirming - nothing reviews this one). An item whose drive isn't currently reachable is left
    /// in place for manual review rather than removed. Returns how many were actually removed.
    /// </summary>
    private int AutoRemoveConfirmedMissing(PaperbunkrDbContext context)
    {
        var eligible = context.Issues
            .Where(i => i.FileIsMissing && i.MissingVerificationCount >= _libraryHealth.ConfirmedMissingThreshold && !i.MissingAcknowledged)
            .Include(i => i.Series)
            .ToList();

        int removed = 0;
        foreach (var issue in eligible)
        {
            if (!LibraryHealthService.IsPathRootReachable(issue.FilePath))
            {
                continue;
            }

            RecordRemoval(context, issue);
            LibraryDeletionHelper.RemoveIssue(context, issue);
            removed++;
        }

        context.SaveChanges();
        return removed;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyComicFolderOperationRunning))]
    private bool _isGeneratingCovers;

    /// <summary>
    /// Generates real cover art for every issue that doesn't have one cached yet
    /// (docs/superpowers/specs/2026-08-06-cover-thumbnails-design.md §2). Moved here from the
    /// Library screen (docs/superpowers/specs/2026-08-09-embedded-metadata-and-migration-relocation-design.md
    /// follow-up), alongside Book Folders and Sync Metadata - all three are "populate my library"
    /// actions. Progress now shows as a live toast rather than an inline bar (Library reloads its
    /// own data on every visit already, so no explicit reload call is needed here).
    /// </summary>
    [RelayCommand]
    private async Task GenerateCovers()
    {
        if (IsGeneratingCovers)
        {
            return;
        }

        IsGeneratingCovers = true;
        using var job = _activity.StartJob(ActivityJobKind.GenerateCovers, "Generating covers");
        CurrentComicFolderJob = job.Job;
        int total = 0;

        try
        {
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                total = p.Total;
                job.Report(p.Done, p.Total, $"{p.Done} / {p.Total} issues");
            });
            await new CoverThumbnailService(_contextFactory).GenerateAllAsync(progress, job.CancellationToken);
            job.Succeed($"Checked {total} issue{(total == 1 ? "" : "s")}", itemsProcessed: total);
            _scheduler?.NotifyRan(Services.Scheduling.ScheduledTaskCatalog.GenerateCovers, ScheduledRunStatus.Succeeded);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            job.Fail("Cover generation failed", ex: ex);
            _scheduler?.NotifyRan(Services.Scheduling.ScheduledTaskCatalog.GenerateCovers, ScheduledRunStatus.Failed);
        }
        finally
        {
            IsGeneratingCovers = false;
            CurrentComicFolderJob = null;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyComicFolderOperationRunning))]
    private bool _isVerifyingCovers;

    /// <summary>
    /// Unconditionally re-derives every comic and book cover from its source file and overwrites
    /// the cache, catching a cover that was wrong from the moment it was scanned - not just the
    /// identity-fingerprint mismatches <see cref="GenerateCovers"/> already self-heals in the
    /// background (docs/superpowers/specs/2026-08-30-cover-thumbnail-content-verification-design.md).
    /// Deliberately a separate command/button from <see cref="GenerateCovers"/> rather than a
    /// behavior change to it, so today's "fill gaps only" semantics (and its tests) stay intact.
    /// Covers both comics and books in one run/toast, unlike <see cref="GenerateCovers"/> which is
    /// comics-only.
    /// </summary>
    [RelayCommand]
    private async Task VerifyCovers()
    {
        if (IsVerifyingCovers)
        {
            return;
        }

        IsVerifyingCovers = true;
        using var job = _activity.StartJob(ActivityJobKind.GenerateCovers, "Verifying covers");
        CurrentComicFolderJob = job.Job;

        // Two sequential passes sharing one job - accumulate rather than assign directly, or the
        // second pass's smaller Total would make the bar jump backward and undercount the summary.
        int comicTotal = 0;
        int bookTotal = 0;
        try
        {
            var comicProgress = new Progress<(int Done, int Total)>(p =>
            {
                comicTotal = p.Total;
                job.Report(p.Done, comicTotal, $"{p.Done} / {comicTotal} comics");
            });
            await new CoverThumbnailService(_contextFactory).VerifyAllAsync(comicProgress, job.CancellationToken);

            var bookProgress = new Progress<(int Done, int Total)>(p =>
            {
                bookTotal = p.Total;
                job.Report(comicTotal + p.Done, comicTotal + bookTotal, $"{p.Done} / {bookTotal} books");
            });
            await new BookCoverThumbnailService(_contextFactory).VerifyAllAsync(bookProgress, job.CancellationToken);

            int total = comicTotal + bookTotal;
            job.Succeed($"Re-checked {total} cover{(total == 1 ? "" : "s")}", itemsProcessed: total);
            _scheduler?.NotifyRan(Services.Scheduling.ScheduledTaskCatalog.VerifyCovers, ScheduledRunStatus.Succeeded);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            job.Fail("Cover verification failed", ex: ex);
            _scheduler?.NotifyRan(Services.Scheduling.ScheduledTaskCatalog.VerifyCovers, ScheduledRunStatus.Failed);
        }
        finally
        {
            IsVerifyingCovers = false;
            CurrentComicFolderJob = null;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyComicFolderOperationRunning))]
    private bool _isRepairingCovers;

    /// <summary>
    /// Regenerates covers only for comics and books that currently have <b>no</b> cover (generated
    /// or user-picked) and whose source file is readable right now - the light "my covers went
    /// blank, put them back" action (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-
    /// durability-design.md, Part 2). Presence-based, so it is safely cancellable and resumable.
    /// Unlike <see cref="VerifyCovers"/> it never re-decodes a cover that is already there, and
    /// unlike the Clear/Rebuild actions it never deletes anything.
    /// </summary>
    [RelayCommand]
    private async Task RepairMissingCovers()
    {
        if (IsRepairingCovers)
        {
            return;
        }

        IsRepairingCovers = true;
        using var job = _activity.StartJob(ActivityJobKind.GenerateCovers, "Repairing missing covers");
        CurrentComicFolderJob = job.Job;

        int comicTotal = 0;
        int bookTotal = 0;
        try
        {
            var comicProgress = new Progress<(int Done, int Total)>(p =>
            {
                comicTotal = p.Total;
                job.Report(p.Done, comicTotal, $"{p.Done} / {comicTotal} comics");
            });
            await new CoverThumbnailService(_contextFactory).RepairMissingAsync(comicProgress, job.CancellationToken);

            var bookProgress = new Progress<(int Done, int Total)>(p =>
            {
                bookTotal = p.Total;
                job.Report(comicTotal + p.Done, comicTotal + bookTotal, $"{p.Done} / {bookTotal} books");
            });
            await new BookCoverThumbnailService(_contextFactory).RepairMissingAsync(bookProgress, job.CancellationToken);

            int total = comicTotal + bookTotal;
            job.Succeed(
                total == 0 ? "No missing covers to repair" : $"Repaired {total} cover{(total == 1 ? "" : "s")}",
                itemsProcessed: total);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            job.Fail("Cover repair failed", ex: ex);
        }
        finally
        {
            IsRepairingCovers = false;
            CurrentComicFolderJob = null;
        }
    }

    /// <summary>
    /// Manual, unconditional escape hatch (docs/superpowers/specs/2026-08-30-cover-thumbnail-
    /// content-verification-design.md) - wipes the entire comic cover cache and rebuilds it, no
    /// detection logic involved at all. Separate from <see cref="ClearBookCoverCacheConfirm"/> so
    /// one side can be nuked without touching the other. Guarded by <see cref="IsGeneratingCovers"/>
    /// (not a new flag) since the rebuild step reuses <see cref="GenerateCovers"/>'s own pipeline.
    /// </summary>
    public TwoStepConfirm ClearComicCoverCacheConfirm { get; }

    private async Task ClearComicCoverCacheAsync()
    {
        if (IsGeneratingCovers)
        {
            return;
        }

        foreach (string path in CoverThumbnailPaths.EnumerateAll())
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }

        // Directory is now empty, so the cheap presence-based path regenerates everything - no
        // need for VerifyAllAsync/force here.
        IsGeneratingCovers = true;
        using var job = _activity.StartJob(ActivityJobKind.GenerateCovers, "Rebuilding comic covers");
        CurrentComicFolderJob = job.Job;
        int total = 0;
        try
        {
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                total = p.Total;
                job.Report(p.Done, p.Total, $"{p.Done} / {p.Total} issues");
            });
            await new CoverThumbnailService(_contextFactory).GenerateAllAsync(progress, job.CancellationToken);
            job.Succeed($"Regenerated {total} cover{(total == 1 ? "" : "s")}", itemsProcessed: total);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            job.Fail("Comic cover rebuild failed", ex: ex);
        }
        finally
        {
            IsGeneratingCovers = false;
            CurrentComicFolderJob = null;
        }
    }

    /// <summary>Book counterpart of <see cref="ClearComicCoverCacheConfirm"/> - see its doc comment.
    /// Guarded by <see cref="IsClearingBookCoverCache"/>, since <see cref="IsGeneratingCovers"/> is
    /// comic-only and there's no pre-existing "generating book covers" flag to reuse.</summary>
    public TwoStepConfirm ClearBookCoverCacheConfirm { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyBookFolderOperationRunning))]
    private bool _isClearingBookCoverCache;

    private async Task ClearBookCoverCacheAsync()
    {
        if (IsClearingBookCoverCache)
        {
            return;
        }

        foreach (string path in BookCoverThumbnailPaths.EnumerateAll())
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }

        IsClearingBookCoverCache = true;
        using var job = _activity.StartJob(ActivityJobKind.GenerateCovers, "Rebuilding book covers");
        CurrentBookFolderJob = job.Job;
        int total = 0;
        try
        {
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                total = p.Total;
                job.Report(p.Done, p.Total, $"{p.Done} / {p.Total} books");
            });
            await new BookCoverThumbnailService(_contextFactory).GenerateAllAsync(progress, job.CancellationToken);
            job.Succeed($"Regenerated {total} cover{(total == 1 ? "" : "s")}", itemsProcessed: total);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            job.Fail("Book cover rebuild failed", ex: ex);
        }
        finally
        {
            IsClearingBookCoverCache = false;
            CurrentBookFolderJob = null;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyComicFolderOperationRunning))]
    private bool _isSyncingMetadata;

    /// <summary>
    /// "Sync Metadata" - re-reads embedded ComicInfo.xml for every already-linked issue and fills
    /// in currently-blank fields only. Moved here alongside Generate Covers, same rationale.
    /// </summary>
    [RelayCommand]
    private async Task SyncMetadata()
    {
        if (IsSyncingMetadata)
        {
            return;
        }

        IsSyncingMetadata = true;
        using var job = _activity.StartJob(ActivityJobKind.SyncMetadata, "Syncing metadata");
        CurrentComicFolderJob = job.Job;

        try
        {
            var progress = new Progress<(int Done, int Total)>(p => job.Report(p.Done, p.Total, $"{p.Done} / {p.Total} issues"));
            var result = await _libraryScanner.SyncMetadataAsync(progress, job.CancellationToken);
            job.Succeed(
                result.IssuesUpdated == 0
                    ? "No new metadata found"
                    : $"Updated {result.IssuesUpdated} issue{(result.IssuesUpdated == 1 ? "" : "s")}",
                itemsProcessed: result.IssuesUpdated);
            _scheduler?.NotifyRan(Services.Scheduling.ScheduledTaskCatalog.SyncMetadata, ScheduledRunStatus.Succeeded);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            job.Fail("Metadata sync failed", ex: ex);
            _scheduler?.NotifyRan(Services.Scheduling.ScheduledTaskCatalog.SyncMetadata, ScheduledRunStatus.Failed);
        }
        finally
        {
            IsSyncingMetadata = false;
            CurrentComicFolderJob = null;
        }
    }

    // ===================== File Association (docs/superpowers/specs/2026-08-07-preferences-advanced-tab-design.md §2) =====================

    public ObservableCollection<FileAssociationSummary> FileAssociations { get; }

    private void RefreshFileAssociations()
    {
        FileAssociations.Clear();
        foreach (var format in _fileAssociationService.GetAvailableFormats())
        {
            FileAssociations.Add(format);
        }
    }

    /// <summary>
    /// Defense-in-depth on top of the real fix (<c>ShellRegister</c> now writes to
    /// <c>HKEY_CURRENT_USER\Software\Classes</c> instead of the elevation-requiring
    /// <c>HKEY_CLASSES_ROOT</c>, which is what was actually crashing this - see that file's
    /// <c>ClassesRootWritable</c> doc comment). Kept as a real try/catch with user-visible feedback
    /// rather than CE's own bare <c>catch</c> swallow (<see cref="Paperbunkr.Engine.IO.Provider.FileFormat.RegisterShell"/>)
    /// - a locked-down machine (group policy, antivirus) could still deny this even from HKCU, and
    /// silently doing nothing would be as confusing as crashing.
    /// </summary>
    [RelayCommand]
    private void ToggleFileAssociation(FileAssociationSummary format)
    {
        try
        {
            _fileAssociationService.SetAssociated(format.Name, !format.IsAssociated);
        }
        catch (Exception ex)
        {
            _showToast("Couldn't update file association", ex.Message);
        }

        RefreshFileAssociations();
    }

    // ===================== Backup Manager (docs/superpowers/specs/2026-08-07-preferences-advanced-tab-design.md §3) =====================

    public ObservableCollection<BackupRowViewModel> Backups { get; }

    /// <summary>docs/superpowers/specs/2026-09-08-advanced-backup-file-association-redesign-design.md §3 - feeds the "requires a restart" note's visibility.</summary>
    public bool HasBackups => Backups.Count > 0;

    [ObservableProperty]
    private string _backupLocation = string.Empty;

    [ObservableProperty]
    private int _backupsToKeep;

    /// <summary>docs/superpowers/specs/2026-08-29-db-corruption-safeguards-design.md §2.</summary>
    [ObservableProperty]
    private bool _autoBackupEnabled;

    /// <summary>See <see cref="AutoBackupEnabled"/>.</summary>
    [ObservableProperty]
    private int _autoBackupMinIntervalHours;

    [ObservableProperty]
    private string? _backupStatus;

    private void RefreshBackups()
    {
        Backups.Clear();
        foreach (string path in _backupService.GetAvailableBackups())
        {
            Backups.Add(new BackupRowViewModel(path, OnRestoreBackupConfirmed));
        }

        OnPropertyChanged(nameof(HasBackups));
    }

    private void OnRestoreBackupConfirmed(BackupRowViewModel row)
    {
        _backupService.RestoreBackup(row.FilePath);
        BackupStatus = "Restored — restart Paperbunkr for the change to take effect.";
    }

    partial void OnBackupLocationChanged(string value)
    {
        if (_suppressBackupSettingsApply)
        {
            return;
        }

        _backupService.SetBackupLocation(value);
    }

    partial void OnBackupsToKeepChanged(int value)
    {
        if (_suppressBackupSettingsApply)
        {
            return;
        }

        _backupService.SetBackupsToKeep(value);
    }

    partial void OnAutoBackupEnabledChanged(bool value)
    {
        if (_suppressBackupSettingsApply)
        {
            return;
        }

        _backupService.SetAutoBackupEnabled(value);
    }

    partial void OnAutoBackupMinIntervalHoursChanged(int value)
    {
        if (_suppressBackupSettingsApply)
        {
            return;
        }

        _backupService.SetAutoBackupMinIntervalHours(value);
    }

    [RelayCommand]
    private async Task BrowseBackupLocation()
    {
        string? path = await _filePicker.PickFolderAsync("Backup Location");
        if (path is null)
        {
            return;
        }

        BackupLocation = path;
    }

    [RelayCommand]
    private void BackupNow()
    {
        try
        {
            _backupService.BackupNow();
            BackupStatus = "Backup created.";
        }
        catch (Exception ex)
        {
            BackupStatus = $"Backup failed: {ex.Message}";
        }

        RefreshBackups();
    }

    // ===================== Reading List Sources (docs/superpowers/specs/2026-08-22-cbl-manager-
    // arc-lookup-design.md §5) - ComicVine/Metron credentials, backed by CredentialStore. Credential
    // *values* live on the row itself (docs/superpowers/specs/2026-09-07-connections-redesign-
    // design.md) - ComicVineRow.SecretValue / MetronRow.Username+Password. =====================

    [ObservableProperty]
    private bool _isComicVineConnected;

    [ObservableProperty]
    private bool _isMetronConnected;

    /// <summary>Row list backing the Connections screen's "Reading List Sources" section (docs/superpowers/specs/2026-09-06-connections-tracker-dialog-redesign-design.md).</summary>
    public ObservableCollection<ConnectionProviderRow> SourceProviderRows { get; }

    private ConnectionProviderRow ComicVineRow => SourceProviderRows.Single(r => r.Id == "ComicVine");
    private ConnectionProviderRow MetronRow => SourceProviderRows.Single(r => r.Id == "Metron");

    private void RefreshSourceCredentials(PaperbunkrDbContext context)
    {
        ComicVineRow.SecretValue = CredentialStore.Get(context, "ComicVine", CredentialKind.ApiKey) ?? string.Empty;
        MetronRow.Username = CredentialStore.Get(context, "Metron", CredentialKind.Username) ?? string.Empty;
        MetronRow.Password = CredentialStore.Get(context, "Metron", CredentialKind.Password) ?? string.Empty;

        // New (docs/superpowers/specs/2026-09-06-connections-tracker-dialog-redesign-design.md) -
        // ComicVine/Metron previously had no "connected" concept at all, just a one-line status
        // message after Save. Same CredentialStore.HasCredentials check the Trackers already use.
        IsComicVineConnected = CredentialStore.HasCredentials(context, "ComicVine", CredentialKind.ApiKey);
        IsMetronConnected = CredentialStore.HasCredentials(context, "Metron", CredentialKind.Username, CredentialKind.Password);

        SyncProviderRowConnectedState(SourceProviderRows, "ComicVine", IsComicVineConnected);
        SyncProviderRowConnectedState(SourceProviderRows, "Metron", IsMetronConnected);
    }

    [RelayCommand]
    private void SaveComicVineCredentials()
    {
        using var context = _contextFactory();
        CredentialStore.Set(context, "ComicVine", CredentialKind.ApiKey, ComicVineRow.SecretValue);
        ConnectionDialogStatus = "ComicVine API key saved.";
        RefreshSourceCredentials(context);
    }

    [RelayCommand]
    private void SaveMetronCredentials()
    {
        using var context = _contextFactory();
        CredentialStore.Set(context, "Metron", CredentialKind.Username, MetronRow.Username);
        CredentialStore.Set(context, "Metron", CredentialKind.Password, MetronRow.Password);
        ConnectionDialogStatus = "Metron credentials saved.";
        RefreshSourceCredentials(context);
    }

    [RelayCommand]
    private void DisconnectComicVine()
    {
        using var context = _contextFactory();
        CredentialStore.Delete(context, "ComicVine", CredentialKind.ApiKey);
        ComicVineRow.SecretValue = string.Empty;
        ConnectionDialogStatus = "ComicVine disconnected.";
        RefreshSourceCredentials(context);
    }

    [RelayCommand]
    private void DisconnectMetron()
    {
        using var context = _contextFactory();
        CredentialStore.Delete(context, "Metron", CredentialKind.Username);
        CredentialStore.Delete(context, "Metron", CredentialKind.Password);
        MetronRow.Username = string.Empty;
        MetronRow.Password = string.Empty;
        ConnectionDialogStatus = "Metron disconnected.";
        RefreshSourceCredentials(context);
    }

    // ===================== Trackers (docs/superpowers/specs/2026-08-23-tracker-write-back-sync-
    // design.md) - AniList/MyAnimeList/Shikimori use browser OAuth (each needs the user's own
    // registered Client ID, same "user registers their own app" model as every OAuth flow here, not
    // a Paperbunkr-wide embedded client), Bangumi uses a pasted Personal Access Token instead
    // (deliberate asymmetry - see the design spec's own "why the four aren't uniform" section).
    // =====================

    // Credential *values* live on the row itself (docs/superpowers/specs/2026-09-07-connections-
    // redesign-design.md) - see ConnectionProviderRow.ClientId/.ClientSecret/.PastedValue/
    // .SecretValue/.Username/.Password. Only the "connected" flags and the code-verifier stay here.
    [ObservableProperty] private bool _isAniListConnected;
    [ObservableProperty] private bool _isMyAnimeListConnected;
    private string? _myAnimeListCodeVerifier;
    [ObservableProperty] private bool _isShikimoriConnected;
    [ObservableProperty] private bool _isBangumiConnected;
    [ObservableProperty] private bool _isMangaBakaConnected;
    [ObservableProperty] private bool _isMangaUpdatesConnected;
    [ObservableProperty] private bool _isKitsuConnected;

    /// <summary>Row list backing the Connections screen's "Trackers" section (docs/superpowers/specs/2026-09-06-connections-tracker-dialog-redesign-design.md).</summary>
    public ObservableCollection<ConnectionProviderRow> TrackerProviderRows { get; }

    private ConnectionProviderRow AniListRow => TrackerProviderRows.Single(r => r.Id == nameof(TrackingService.AniList));
    private ConnectionProviderRow MyAnimeListRow => TrackerProviderRows.Single(r => r.Id == nameof(TrackingService.MyAnimeList));
    private ConnectionProviderRow ShikimoriRow => TrackerProviderRows.Single(r => r.Id == nameof(TrackingService.Shikimori));
    private ConnectionProviderRow BangumiRow => TrackerProviderRows.Single(r => r.Id == nameof(TrackingService.Bangumi));
    private ConnectionProviderRow MangaBakaRow => TrackerProviderRows.Single(r => r.Id == nameof(TrackingService.MangaBaka));
    private ConnectionProviderRow MangaUpdatesRow => TrackerProviderRows.Single(r => r.Id == nameof(TrackingService.MangaUpdates));
    private ConnectionProviderRow KitsuRow => TrackerProviderRows.Single(r => r.Id == nameof(TrackingService.Kitsu));

    // ===================== Connections list+dialog (docs/superpowers/specs/2026-09-06-connections-
    // tracker-dialog-redesign-design.md) - the row-list/dialog state shared by both the Reading List
    // Sources and Trackers sections. =====================

    [ObservableProperty]
    private ConnectionProviderRow? _selectedConnectionProvider;

    [ObservableProperty]
    private bool _isConnectionDialogOpen;

    /// <summary>
    /// One shared status line for whichever dialog is currently open (docs/superpowers/specs/
    /// 2026-09-07-connections-redesign-design.md) - replaces the old TrackersStatus/SourcesStatus
    /// pair, which were both always-rendered in the dialog regardless of which provider was open,
    /// so a stale message from the last provider touched could bleed into an unrelated one.
    /// </summary>
    [ObservableProperty]
    private string? _connectionDialogStatus;

    [RelayCommand]
    private void OpenConnectionDialog(ConnectionProviderRow row)
    {
        // Clear ephemeral paste-back fields (never the persisted Client ID/Secret/Username fields -
        // those are meant to survive switching providers and reopening the same one) so a stale
        // code/token from a previous dialog session can't bleed into a different provider's dialog.
        // Also clear the shared status line for the same reason (see ConnectionDialogStatus's own
        // doc comment).
        AniListRow.PastedValue = string.Empty;
        MyAnimeListRow.PastedValue = string.Empty;
        ShikimoriRow.PastedValue = string.Empty;
        ConnectionDialogStatus = string.Empty;

        SelectedConnectionProvider = row;
        IsConnectionDialogOpen = true;
    }

    [RelayCommand]
    private void CloseConnectionDialog()
    {
        IsConnectionDialogOpen = false;
        SelectedConnectionProvider = null;
    }

    private static void SyncProviderRowConnectedState(ObservableCollection<ConnectionProviderRow> rows, string id, bool isConnected)
    {
        var row = rows.FirstOrDefault(r => r.Id == id);
        if (row is not null)
        {
            row.IsConnected = isConnected;
        }
    }

    private void RefreshTrackerConnectionState(PaperbunkrDbContext context)
    {
        IsAniListConnected = CredentialStore.HasCredentials(context, nameof(TrackingService.AniList), CredentialKind.OAuthAccessToken);
        IsMyAnimeListConnected = CredentialStore.HasCredentials(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthAccessToken);
        IsShikimoriConnected = CredentialStore.HasCredentials(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthAccessToken);
        IsBangumiConnected = CredentialStore.HasCredentials(context, nameof(TrackingService.Bangumi), CredentialKind.ApiKey);
        IsMangaBakaConnected = CredentialStore.HasCredentials(context, nameof(TrackingService.MangaBaka), CredentialKind.ApiKey);
        IsMangaUpdatesConnected = CredentialStore.HasCredentials(context, nameof(TrackingService.MangaUpdates), CredentialKind.OAuthAccessToken);
        IsKitsuConnected = CredentialStore.HasCredentials(context, nameof(TrackingService.Kitsu), CredentialKind.OAuthAccessToken);

        AniListRow.ClientId = CredentialStore.Get(context, nameof(TrackingService.AniList), CredentialKind.OAuthClientId) ?? string.Empty;
        MyAnimeListRow.ClientId = CredentialStore.Get(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthClientId) ?? string.Empty;
        ShikimoriRow.ClientId = CredentialStore.Get(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthClientId) ?? string.Empty;
        ShikimoriRow.ClientSecret = CredentialStore.Get(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthClientSecret) ?? string.Empty;

        SyncProviderRowConnectedState(TrackerProviderRows, nameof(TrackingService.AniList), IsAniListConnected);
        SyncProviderRowConnectedState(TrackerProviderRows, nameof(TrackingService.MyAnimeList), IsMyAnimeListConnected);
        SyncProviderRowConnectedState(TrackerProviderRows, nameof(TrackingService.Shikimori), IsShikimoriConnected);
        SyncProviderRowConnectedState(TrackerProviderRows, nameof(TrackingService.Bangumi), IsBangumiConnected);
        SyncProviderRowConnectedState(TrackerProviderRows, nameof(TrackingService.MangaBaka), IsMangaBakaConnected);
        SyncProviderRowConnectedState(TrackerProviderRows, nameof(TrackingService.MangaUpdates), IsMangaUpdatesConnected);
        SyncProviderRowConnectedState(TrackerProviderRows, nameof(TrackingService.Kitsu), IsKitsuConnected);
    }

    [RelayCommand]
    private void ConnectAniList()
    {
        using var context = _contextFactory();
        CredentialStore.Set(context, nameof(TrackingService.AniList), CredentialKind.OAuthClientId, AniListRow.ClientId);
        Process.Start(new ProcessStartInfo { FileName = AniListTrackerAdapter.BuildAuthorizationUrl(AniListRow.ClientId), UseShellExecute = true });
        ConnectionDialogStatus = "Complete sign-in in your browser, then paste the token back here.";
    }

    [RelayCommand]
    private void CompleteAniListConnect()
    {
        using var context = _contextFactory();
        AniListTrackerAdapter.CompleteConnect(context, AniListRow.PastedValue);
        AniListRow.PastedValue = string.Empty;
        RefreshTrackerConnectionState(context);
        ConnectionDialogStatus = "AniList connected.";
    }

    [RelayCommand]
    private void ConnectMyAnimeList()
    {
        using var context = _contextFactory();
        CredentialStore.Set(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthClientId, MyAnimeListRow.ClientId);
        _myAnimeListCodeVerifier = MyAnimeListTrackerAdapter.GenerateCodeVerifier();
        Process.Start(new ProcessStartInfo
        {
            FileName = MyAnimeListTrackerAdapter.BuildAuthorizationUrl(MyAnimeListRow.ClientId, _myAnimeListCodeVerifier),
            UseShellExecute = true,
        });
        ConnectionDialogStatus = "The page after sign-in will fail to load - that's expected. Copy the \"code\" value from its address bar and paste it back here.";
    }

    [RelayCommand]
    private async Task CompleteMyAnimeListConnectAsync()
    {
        if (_myAnimeListCodeVerifier is null)
        {
            ConnectionDialogStatus = "Click Connect first.";
            return;
        }

        using var context = _contextFactory();
        var adapter = new MyAnimeListTrackerAdapter(TrackerHttpClients.MyAnimeList, MyAnimeListRow.ClientId);
        bool connected = await adapter.CompleteConnectAsync(context, MyAnimeListRow.ClientId, _myAnimeListCodeVerifier, MyAnimeListRow.PastedValue, default);

        MyAnimeListRow.PastedValue = string.Empty;
        _myAnimeListCodeVerifier = null;
        RefreshTrackerConnectionState(context);
        ConnectionDialogStatus = connected ? "MyAnimeList connected." : "Couldn't connect to MyAnimeList. Check your Client ID and try again.";
    }

    [RelayCommand]
    private void ConnectShikimori()
    {
        using var context = _contextFactory();
        CredentialStore.Set(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthClientId, ShikimoriRow.ClientId);
        CredentialStore.Set(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthClientSecret, ShikimoriRow.ClientSecret);
        Process.Start(new ProcessStartInfo { FileName = ShikimoriTrackerAdapter.BuildAuthorizationUrl(ShikimoriRow.ClientId), UseShellExecute = true });
        ConnectionDialogStatus = "Shikimori will show you a code to copy - paste it back here.";
    }

    [RelayCommand]
    private async Task CompleteShikimoriConnectAsync()
    {
        using var context = _contextFactory();
        var adapter = new ShikimoriTrackerAdapter(TrackerHttpClients.Shikimori);
        bool connected = await adapter.CompleteConnectAsync(context, ShikimoriRow.ClientId, ShikimoriRow.ClientSecret, ShikimoriRow.PastedValue, default);

        ShikimoriRow.PastedValue = string.Empty;
        RefreshTrackerConnectionState(context);
        ConnectionDialogStatus = connected ? "Shikimori connected." : "Couldn't connect to Shikimori. Check your Client ID/Secret and try again.";
    }

    [RelayCommand]
    private void SaveBangumiToken()
    {
        using var context = _contextFactory();
        BangumiTrackerAdapter.CompleteConnect(context, BangumiRow.SecretValue);
        BangumiRow.SecretValue = string.Empty;
        RefreshTrackerConnectionState(context);
        ConnectionDialogStatus = "Bangumi token saved.";
    }

    [RelayCommand]
    private void SaveMangaBakaToken()
    {
        using var context = _contextFactory();
        MangaBakaTrackerAdapter.CompleteConnect(context, MangaBakaRow.SecretValue);
        MangaBakaRow.SecretValue = string.Empty;
        RefreshTrackerConnectionState(context);
        ConnectionDialogStatus = "MangaBaka token saved.";
    }

    [RelayCommand]
    private async Task ConnectMangaUpdatesAsync()
    {
        using var context = _contextFactory();
        var adapter = new MangaUpdatesTrackerAdapter(TrackerHttpClients.MangaUpdates);
        bool connected = await adapter.CompleteConnectAsync(context, MangaUpdatesRow.Username, MangaUpdatesRow.Password, default);

        MangaUpdatesRow.Password = string.Empty;
        RefreshTrackerConnectionState(context);
        ConnectionDialogStatus = connected ? "MangaUpdates connected." : "Couldn't connect to MangaUpdates. Check your username/password and try again.";
    }

    [RelayCommand]
    private async Task ConnectKitsuAsync()
    {
        using var context = _contextFactory();
        var adapter = new KitsuTrackerAdapter(TrackerHttpClients.Kitsu, accessToken: null);
        bool connected = await adapter.CompleteConnectAsync(context, KitsuRow.Username, KitsuRow.Password, default);

        KitsuRow.Password = string.Empty;
        RefreshTrackerConnectionState(context);
        ConnectionDialogStatus = connected ? "Kitsu connected." : "Couldn't connect to Kitsu. Check your username/password and try again.";
    }

    // ===================== Disconnect commands (docs/superpowers/specs/2026-09-06-connections-
    // tracker-dialog-redesign-design.md) - new, didn't exist before this spec (there was previously
    // no UI way to clear a saved credential). OAuth providers clear only the access/refresh token(s),
    // deliberately keeping the registered Client ID/Secret so reconnecting doesn't need re-pasting
    // it from the provider's own developer settings page. =====================

    [RelayCommand]
    private void DisconnectAniList()
    {
        using var context = _contextFactory();
        CredentialStore.Delete(context, nameof(TrackingService.AniList), CredentialKind.OAuthAccessToken);
        RefreshTrackerConnectionState(context);
        ConnectionDialogStatus = "AniList disconnected.";
    }

    [RelayCommand]
    private void DisconnectMyAnimeList()
    {
        using var context = _contextFactory();
        CredentialStore.Delete(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthAccessToken);
        CredentialStore.Delete(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthRefreshToken);
        RefreshTrackerConnectionState(context);
        ConnectionDialogStatus = "MyAnimeList disconnected.";
    }

    [RelayCommand]
    private void DisconnectShikimori()
    {
        using var context = _contextFactory();
        CredentialStore.Delete(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthAccessToken);
        CredentialStore.Delete(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthRefreshToken);
        RefreshTrackerConnectionState(context);
        ConnectionDialogStatus = "Shikimori disconnected.";
    }

    [RelayCommand]
    private void DisconnectBangumi()
    {
        using var context = _contextFactory();
        CredentialStore.Delete(context, nameof(TrackingService.Bangumi), CredentialKind.ApiKey);
        BangumiRow.SecretValue = string.Empty;
        RefreshTrackerConnectionState(context);
        ConnectionDialogStatus = "Bangumi disconnected.";
    }

    [RelayCommand]
    private void DisconnectMangaBaka()
    {
        using var context = _contextFactory();
        CredentialStore.Delete(context, nameof(TrackingService.MangaBaka), CredentialKind.ApiKey);
        MangaBakaRow.SecretValue = string.Empty;
        RefreshTrackerConnectionState(context);
        ConnectionDialogStatus = "MangaBaka disconnected.";
    }

    [RelayCommand]
    private void DisconnectMangaUpdates()
    {
        using var context = _contextFactory();
        CredentialStore.Delete(context, nameof(TrackingService.MangaUpdates), CredentialKind.OAuthAccessToken);
        MangaUpdatesRow.Username = string.Empty;
        MangaUpdatesRow.Password = string.Empty;
        RefreshTrackerConnectionState(context);
        ConnectionDialogStatus = "MangaUpdates disconnected.";
    }

    [RelayCommand]
    private void DisconnectKitsu()
    {
        using var context = _contextFactory();
        CredentialStore.Delete(context, nameof(TrackingService.Kitsu), CredentialKind.OAuthAccessToken);
        CredentialStore.Delete(context, nameof(TrackingService.Kitsu), CredentialKind.OAuthRefreshToken);
        KitsuRow.Username = string.Empty;
        KitsuRow.Password = string.Empty;
        RefreshTrackerConnectionState(context);
        ConnectionDialogStatus = "Kitsu disconnected.";
    }

    // ===================== Automation tab (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-
    //                       cover-durability-design.md, Part 1) =====================

    private Services.Scheduling.ISchedulerService? _scheduler;

    /// <summary>The scheduler's task rows, rebuilt whenever it raises <c>Changed</c>.</summary>
    public ObservableCollection<ScheduledTaskRow> ScheduledTasks { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduledTaskNotificationLevelText))]
    private ScheduledTaskNotificationLevel _scheduledTaskNotificationLevel = ScheduledTaskNotificationLevel.OnlyFailures;

    /// <summary>SuggestBox string view - see the reader-tab projections above.</summary>
    public string ScheduledTaskNotificationLevelText
    {
        get => ScheduledTaskNotificationLevel.ToString();
        set { if (Enum.TryParse<ScheduledTaskNotificationLevel>(value, out var parsed)) ScheduledTaskNotificationLevel = parsed; }
    }

    public string[] NotificationLevelNames { get; } = Enum.GetNames<ScheduledTaskNotificationLevel>();

    partial void OnScheduledTaskNotificationLevelChanged(ScheduledTaskNotificationLevel value)
    {
        if (!_isLoaded)
        {
            return;
        }

        try
        {
            using var context = _contextFactory();
            context.GetOrCreateAppSettings().ScheduledTaskNotificationLevel = value;
            context.SaveChanges();
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Wired once from <c>MainViewModel</c> after both this VM and the scheduler exist.</summary>
    public void AttachScheduler(Services.Scheduling.ISchedulerService scheduler)
    {
        _scheduler = scheduler;
        scheduler.Changed += (_, _) => RebuildScheduledTasks();
        using (var context = _contextFactory())
        {
            ScheduledTaskNotificationLevel = context.GetOrCreateAppSettings().ScheduledTaskNotificationLevel;
        }

        RebuildScheduledTasks();
    }

    private bool _rebuildingScheduledTasks;

    private void RebuildScheduledTasks()
    {
        if (_scheduler is null || _rebuildingScheduledTasks)
        {
            return;
        }

        void Apply()
        {
            _rebuildingScheduledTasks = true;
            try
            {
                ApplyRows();
            }
            finally
            {
                _rebuildingScheduledTasks = false;
            }
        }

        void ApplyRows()
        {
            var byId = ScheduledTasks.ToDictionary(r => r.TaskId);
            ScheduledTasks.Clear();
            foreach (var source in _scheduler.Tasks)
            {
                // Reuse the row instance where possible so an in-progress edit isn't stomped.
                var row = byId.TryGetValue(source.TaskId, out var existing) ? existing : source;
                row.Enabled = source.Enabled;
                row.Mode = source.Mode;
                row.IntervalHours = source.IntervalHours;
                row.DailyAtTime = source.DailyAtTime;
                row.LastRunUtc = source.LastRunUtc;
                row.LastRunStatus = source.LastRunStatus;
                row.IsRunning = source.IsRunning;
                row.IsQueued = source.IsQueued;
                row.NextRunLabel = source.NextRunLabel;
                row.RunNowCommand ??= new AsyncRelayCommand(() => _scheduler!.RunNowAsync(row.TaskId));
                WireRowPersistence(row);
                ScheduledTasks.Add(row);
            }
        }

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(Apply);
        }
    }

    private readonly HashSet<string> _rowPersistenceWired = new();

    private void WireRowPersistence(ScheduledTaskRow row)
    {
        if (!_rowPersistenceWired.Add(row.TaskId))
        {
            return;
        }

        row.PropertyChanged += (_, e) =>
        {
            if (_scheduler is null || _rebuildingScheduledTasks)
            {
                return;
            }

            switch (e.PropertyName)
            {
                case nameof(ScheduledTaskRow.Enabled):
                    _scheduler.SetEnabled(row.TaskId, row.Enabled);
                    break;
                case nameof(ScheduledTaskRow.Mode):
                case nameof(ScheduledTaskRow.IntervalHours):
                case nameof(ScheduledTaskRow.DailyAtTime):
                    _scheduler.SetSchedule(row.TaskId, row.Mode, row.IntervalHours, (int)row.DailyAtTime.TotalMinutes);
                    break;
            }
        };
    }

    /// <summary>Session-only pre-pause snapshot of each task's own <see cref="ScheduledTaskRow.Enabled"/>
    /// (docs/superpowers/specs/2026-09-08-automation-tasks-redesign-design.md) - non-null while
    /// paused. Deliberately not persisted: resets to "not paused" on app restart, and Resume
    /// restores exactly this snapshot rather than blindly re-enabling every task (several tasks
    /// default to disabled; a blind resume would incorrectly turn those back on).</summary>
    private Dictionary<string, bool>? _prePauseEnabledSnapshot;

    public bool IsSchedulerPaused => _prePauseEnabledSnapshot is not null;

    [RelayCommand]
    private void RunAllScheduledTasksNow()
    {
        if (_scheduler is null)
        {
            return;
        }

        // Iterates the scheduler's own live Tasks, not this VM's ScheduledTasks cache - the cache
        // is only refreshed via a dispatcher round-trip off the scheduler's Changed event, so
        // reading it here could act on a stale/empty snapshot immediately after AttachScheduler.
        foreach (var row in _scheduler.Tasks)
        {
            _ = _scheduler.RunNowAsync(row.TaskId);
        }
    }

    [RelayCommand]
    private void TogglePauseAllScheduledTasks()
    {
        if (_scheduler is null)
        {
            return;
        }

        if (_prePauseEnabledSnapshot is null)
        {
            _prePauseEnabledSnapshot = _scheduler.Tasks.ToDictionary(r => r.TaskId, r => r.Enabled);
            foreach (var row in _scheduler.Tasks)
            {
                _scheduler.SetEnabled(row.TaskId, false);
            }
        }
        else
        {
            foreach (var (taskId, wasEnabled) in _prePauseEnabledSnapshot)
            {
                _scheduler.SetEnabled(taskId, wasEnabled);
            }

            _prePauseEnabledSnapshot = null;
        }

        OnPropertyChanged(nameof(IsSchedulerPaused));
    }

    // ===================== Library Health tab (docs/superpowers/specs/2026-09-06-missing-files-
    //                       library-health-design.md) =====================
    //
    // The sole home for missing-file review, replacing (not duplicating) Needs Review's old
    // "Missing Files" section. MissingFileRowViewModel is reused as-is - same component, new host.

    private const int RecentlyRemovedRetentionDays = 30;

    public ObservableCollection<MissingFileRowViewModel> MissingFileItems { get; }

    public ObservableCollection<RemovedLibraryEntryRowViewModel> RecentlyRemovedItems { get; }

    public bool HasMissingFileItems => MissingFileItems.Count > 0;

    public bool HasRecentlyRemovedItems => RecentlyRemovedItems.Count > 0;

    /// <summary>Recently Removed is collapsed by default (docs/superpowers/specs/2026-09-07-
    /// library-health-redesign-design.md §6) - it's an audit trail, not an actionable list like
    /// Missing Files, so it doesn't need to stay expanded to earn its space.</summary>
    [ObservableProperty]
    private bool _isRecentlyRemovedExpanded;

    [RelayCommand]
    private void ToggleRecentlyRemovedExpanded() => IsRecentlyRemovedExpanded = !IsRecentlyRemovedExpanded;

    [ObservableProperty]
    private int _libraryHealthChecked;

    [ObservableProperty]
    private int _libraryHealthMissingNow;

    [ObservableProperty]
    private int _libraryHealthConfirmedMissing;

    public bool HasConfirmedMissingItems => LibraryHealthConfirmedMissing > 0;

    /// <summary>Consecutive missing Verify passes before an issue counts as confirmed-missing
    /// (docs/superpowers/specs/2026-09-07-library-health-redesign-design.md §7) - was a hardcoded
    /// <see cref="LibraryHealthService.ConfirmedMissingThreshold"/> constant, now a real setting.</summary>
    [ObservableProperty]
    private int _libraryHealthConfirmedMissingThreshold = 2;

    [ObservableProperty]
    private string _libraryHealthLastVerifiedLabel = "Never";

    [ObservableProperty]
    private bool _isVerifyingLibraryHealth;

    /// <summary>The in-flight Verify job, for <c>Controls.BusyIndicator</c> to bind to while
    /// <see cref="IsVerifyingLibraryHealth"/> is true. Null the rest of the time. Completion/failure
    /// already surfaces as a toast via the existing <see cref="IActivityService"/> pipeline (default
    /// <c>ActivityToastPolicy.Always</c>), so no separate status text is needed here.</summary>
    [ObservableProperty]
    private ActivityJob? _currentLibraryHealthJob;

    /// <summary>
    /// Reloads every Library Health surface from the current DB state - the missing-files list, the
    /// summary counts, the last-verified label, and Recently Removed. Called from <see cref="Reload"/>
    /// (Preferences screen open) and after every Verify/Relink/Remove/Dismiss/Restore, same
    /// "always re-query, never patch in place" approach <see cref="NeedsReviewViewModel.Refresh"/>
    /// uses. Also does Recently Removed's 30-day retention trim, lazily on load rather than via a
    /// separate scheduled sweep - simplest correct place given how often this tab is likely to be
    /// opened, matching this feature's own "manual, attended" tone (no automatic Verify by default).
    /// </summary>
    private void RefreshLibraryHealth(PaperbunkrDbContext context)
    {
        LibraryHealthLastVerifiedLabel = context.GetOrCreateAppSettings().LastLibraryHealthVerifyUtc is { } lastVerified
            ? lastVerified.ToLocalTime().ToString("MMM d, yyyy h:mm tt")
            : "Never";

        var trackedIssues = context.Issues.Where(i => !i.IsPlaceholder && i.FilePath != null);
        LibraryHealthChecked = trackedIssues.Count();
        LibraryHealthMissingNow = trackedIssues.Count(i => i.FileIsMissing);
        LibraryHealthConfirmedMissing = trackedIssues.Count(i =>
            i.FileIsMissing && i.MissingVerificationCount >= _libraryHealth.ConfirmedMissingThreshold && !i.MissingAcknowledged);

        MissingFileItems.Clear();
        foreach (var issue in trackedIssues.Where(i => i.FileIsMissing && !i.MissingAcknowledged).Include(i => i.Series).OrderBy(i => i.Series!.Name))
        {
            int issueId = issue.Id;
            bool confirmedMissing = issue.MissingVerificationCount >= _libraryHealth.ConfirmedMissingThreshold;
            MissingFileItems.Add(new MissingFileRowViewModel(
                issueId,
                $"{issue.Series?.Name ?? "Unknown"} #{issue.EffectiveNumber()}{(confirmedMissing ? " · confirmed missing" : "")}",
                onRelink: RelinkMissingFile,
                onRemove: _ => RemoveMissingFile(issueId),
                onDismiss: _ => DismissMissingFile(issueId)));
        }

        var cutoff = DateTime.UtcNow.AddDays(-RecentlyRemovedRetentionDays);
        var stale = context.RemovedLibraryEntries.Where(e => e.RemovedAtUtc < cutoff);
        context.RemovedLibraryEntries.RemoveRange(stale);
        context.SaveChanges();

        RecentlyRemovedItems.Clear();
        foreach (var entry in context.RemovedLibraryEntries.OrderByDescending(e => e.RemovedAtUtc).ToList())
        {
            int entryId = entry.Id;
            string label = $"{entry.SeriesName} #{entry.Number ?? "?"}";
            RecentlyRemovedItems.Add(new RemovedLibraryEntryRowViewModel(entryId, label, entry.FilePath, entry.RemovedAtUtc, RestoreRemovedEntry));
        }

        NotifyLibraryHealthCountsChanged();
    }

    private void NotifyLibraryHealthCountsChanged()
    {
        OnPropertyChanged(nameof(HasMissingFileItems));
        OnPropertyChanged(nameof(HasRecentlyRemovedItems));
        OnPropertyChanged(nameof(HasConfirmedMissingItems));
    }

    [RelayCommand]
    private async Task VerifyLibraryHealthNow()
    {
        if (IsVerifyingLibraryHealth)
        {
            return;
        }

        IsVerifyingLibraryHealth = true;
        using var job = _activity.StartJob(ActivityJobKind.LibraryVerify, "Verifying library files");
        CurrentLibraryHealthJob = job.Job;
        try
        {
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                job.Report(p.Done, p.Total, $"{p.Done} / {p.Total} files");
            });
            var result = await _libraryHealth.VerifyAsync(progress, job.CancellationToken);

            using (var context = _contextFactory())
            {
                context.GetOrCreateAppSettings().LastLibraryHealthVerifyUtc = DateTime.UtcNow;
                context.SaveChanges();
            }

            string summary = result.MissingNow == 0
                ? $"Checked {result.Checked} issues - all files present."
                : $"Checked {result.Checked} issues - {result.MissingNow} missing ({result.ConfirmedMissingCount} confirmed).";
            job.Succeed(summary, itemsProcessed: result.Checked);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            job.Fail("Library Health verify failed", ex: ex);
        }
        finally
        {
            IsVerifyingLibraryHealth = false;
            CurrentLibraryHealthJob = null;
            using var context = _contextFactory();
            RefreshLibraryHealth(context);
        }
    }

    private async Task RelinkMissingFile(MissingFileRowViewModel row)
    {
        string? path = await _filePicker.PickOpenFileAsync("Locate the file", "cbz", "Comic Archive");
        if (path is null)
        {
            return;
        }

        using (var context = _contextFactory())
        {
            var issue = context.Issues.Find(row.IssueId);
            if (issue is not null)
            {
                issue.FilePath = path;
                issue.FileIsMissing = false;
                issue.IsPlaceholder = false;
                issue.MissingVerificationCount = 0;
                context.SaveChanges();
            }
        }

        using var refreshContext = _contextFactory();
        RefreshLibraryHealth(refreshContext);
    }

    private void RemoveMissingFile(int issueId)
    {
        using (var context = _contextFactory())
        {
            var issue = context.Issues.Include(i => i.Series).FirstOrDefault(i => i.Id == issueId);
            if (issue is not null)
            {
                RecordRemoval(context, issue);
                LibraryDeletionHelper.RemoveIssue(context, issue);
                context.SaveChanges();
            }
        }

        // Deferred: this command runs from the row's own TwoStepConfirm "Confirm" Button.Click still
        // routing through the MissingFileItems row's own ItemsControl. RefreshLibraryHealth clears
        // that collection, which would detach that same row mid-route and crash Avalonia's detach
        // walk with an ArgumentOutOfRangeException (see Paperbunkr.App.Controls.SuggestBox.Commit
        // for the fully diagnosed case).
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            using var refreshContext = _contextFactory();
            RefreshLibraryHealth(refreshContext);
        });
    }

    private void DismissMissingFile(int issueId)
    {
        using (var context = _contextFactory())
        {
            var issue = context.Issues.Find(issueId);
            if (issue is not null)
            {
                issue.MissingAcknowledged = true;
                context.SaveChanges();
            }
        }

        // Deferred: same reason as RemoveMissingFile above - this command runs from a single click
        // on the row's own "Dismiss" Button still routing through the MissingFileItems row's own
        // ItemsControl.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            using var refreshContext = _contextFactory();
            RefreshLibraryHealth(refreshContext);
        });
    }

    /// <summary>
    /// Dismisses every currently-listed missing file at once ("I know these are gone, stop asking")
    /// - unlike the bulk Remove action below, this isn't gated by the two-strikes threshold, since
    /// Dismiss never touches the file or the Issue row, just the review-queue flag. Same semantics
    /// as dismissing each row individually.
    /// </summary>
    [RelayCommand]
    private void BulkDismissMissingFiles()
    {
        using (var context = _contextFactory())
        {
            var ids = MissingFileItems.Select(r => r.IssueId).ToList();
            foreach (var issue in context.Issues.Where(i => ids.Contains(i.Id)))
            {
                issue.MissingAcknowledged = true;
            }

            context.SaveChanges();
        }

        using var refreshContext = _contextFactory();
        RefreshLibraryHealth(refreshContext);
    }

    /// <summary>
    /// "The library got reorganized onto a new drive/folder - go find everything that moved."
    /// Picks one folder, then matches every currently-missing issue to a file under that folder
    /// (recursively) by filename alone - deliberately not by full relative path, since a
    /// reorganization is exactly the case where the path changed but the filename usually didn't.
    /// A false-positive match (same filename, different comic) is possible but rare enough not to
    /// warrant per-match confirmation here; Relink's per-row picker remains available for anything
    /// this misses or gets wrong.
    /// </summary>
    [RelayCommand]
    private async Task BulkRelinkMissingFiles()
    {
        if (MissingFileItems.Count == 0)
        {
            return;
        }

        string? folder = await _filePicker.PickFolderAsync("Locate the reorganized folder");
        if (folder is null || !Directory.Exists(folder))
        {
            return;
        }

        var byFileName = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key!, g => g.First(), StringComparer.OrdinalIgnoreCase);

        int total = MissingFileItems.Count;
        int relinked = 0;
        using (var context = _contextFactory())
        {
            foreach (int issueId in MissingFileItems.Select(r => r.IssueId).ToList())
            {
                var issue = context.Issues.Find(issueId);
                if (issue?.FilePath is null)
                {
                    continue;
                }

                if (byFileName.TryGetValue(Path.GetFileName(issue.FilePath), out string? matchPath))
                {
                    issue.FilePath = matchPath;
                    issue.FileIsMissing = false;
                    issue.IsPlaceholder = false;
                    issue.MissingVerificationCount = 0;
                    relinked++;
                }
            }

            context.SaveChanges();
        }

        string summary = relinked == 0
            ? $"No matching files found in that folder for {total} missing issue{(total == 1 ? "" : "s")}."
            : $"Relinked {relinked} of {total} missing issue{(total == 1 ? "" : "s")}.";
        _showToast("Library Health", summary);

        using var refreshContext = _contextFactory();
        RefreshLibraryHealth(refreshContext);
    }

    /// <summary>Snapshots an about-to-be-deleted issue into <see cref="RemovedLibraryEntry"/>, inside the caller's transaction, immediately before <see cref="LibraryDeletionHelper.RemoveIssue"/> - every Library Health removal writes one of these, single-item or bulk.</summary>
    private static void RecordRemoval(PaperbunkrDbContext context, Issue issue)
    {
        context.RemovedLibraryEntries.Add(new RemovedLibraryEntry
        {
            SeriesName = issue.Series?.Name ?? "Unknown",
            SeriesId = issue.SeriesId,
            Number = issue.Number,
            Volume = issue.Volume,
            Title = issue.Title,
            FilePath = issue.FilePath,
            RemovedAtUtc = DateTime.UtcNow,
            Reason = RemovedLibraryEntryReason.MissingFileCleanup,
        });
    }

    /// <summary>Confirms via the shared <see cref="IDialogService"/> (docs/superpowers/specs/
    /// 2026-09-07-library-health-redesign-design.md §4) instead of the old bespoke inline panel -
    /// <see cref="ConfirmDialogRequest.Items"/> was built for exactly this call site.</summary>
    [RelayCommand]
    private async Task OpenBulkRemoveConfirm()
    {
        List<Issue> eligible;
        using (var context = _contextFactory())
        {
            eligible = context.Issues.Include(i => i.Series)
                .Where(i => i.FileIsMissing && i.MissingVerificationCount >= _libraryHealth.ConfirmedMissingThreshold && !i.MissingAcknowledged)
                .OrderBy(i => i.Series!.Name)
                .ToList();
        }

        if (eligible.Count == 0)
        {
            return;
        }

        var preview = eligible
            .Select(issue => $"{issue.Series?.Name ?? "Unknown"} #{issue.EffectiveNumber()} — {issue.FilePath}")
            .ToList();

        int answer = await _dialogService.ShowAsync(new ConfirmDialogRequest(
            Message: "Remove every confirmed-missing issue below? This cannot be undone directly, but each one is logged in Recently Removed for 30 days with a Restore option.",
            Items: preview,
            PrimaryLabel: "Confirm Remove All",
            IsDestructive: true));

        if (answer != 0)
        {
            return;
        }

        using (var context = _contextFactory())
        {
            var toRemove = context.Issues
                .Where(i => i.FileIsMissing && i.MissingVerificationCount >= _libraryHealth.ConfirmedMissingThreshold && !i.MissingAcknowledged)
                .Include(i => i.Series)
                .ToList();

            foreach (var issue in toRemove)
            {
                RecordRemoval(context, issue);
                LibraryDeletionHelper.RemoveIssue(context, issue);
            }

            context.SaveChanges();
        }

        using var refreshContext = _contextFactory();
        RefreshLibraryHealth(refreshContext);
    }

    /// <summary>
    /// Recreates the Issue row with <c>FileIsMissing = true</c> (the file is still actually absent -
    /// this is "undo the removal," not "the file reappeared") - it lands right back in Library
    /// Health's missing-files list, not silently marked fixed. Links to the still-existing Series by
    /// id when possible, otherwise finds-or-creates one by name. Does not attempt to recover reading
    /// progress / collection membership / reading-list entries - those cross-references were removed
    /// by LibraryDeletionHelper.RemoveIssue and were never snapshotted (v1-minimal, per the design's
    /// non-goals).
    /// </summary>
    private void RestoreRemovedEntry(RemovedLibraryEntryRowViewModel row)
    {
        using (var context = _contextFactory())
        {
            var entry = context.RemovedLibraryEntries.Find(row.EntryId);
            if (entry is null)
            {
                return;
            }

            var series = (entry.SeriesId is int seriesId ? context.Series.Find(seriesId) : null)
                ?? context.Series.FirstOrDefault(s => s.Name == entry.SeriesName);
            if (series is null)
            {
                series = new Series { Name = entry.SeriesName };
                context.Series.Add(series);
            }

            context.Issues.Add(new Issue
            {
                Series = series,
                Number = entry.Number,
                Volume = entry.Volume,
                Title = entry.Title,
                FilePath = entry.FilePath,
                FileIsMissing = true,
                MissingVerificationCount = 0,
            });

            context.RemovedLibraryEntries.Remove(entry);
            context.SaveChanges();
        }

        // Deferred: this command runs from the row's own "Restore" Button.Click still routing
        // through the RecentlyRemovedItems row's own ItemsControl. RefreshLibraryHealth clears that
        // collection, which would detach that same row mid-route and crash Avalonia's detach walk
        // with an ArgumentOutOfRangeException (see Paperbunkr.App.Controls.SuggestBox.Commit for the
        // fully diagnosed case).
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            using var refreshContext = _contextFactory();
            RefreshLibraryHealth(refreshContext);
        });
    }
}
