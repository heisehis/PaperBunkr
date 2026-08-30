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
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
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
    private readonly FileAssociationService _fileAssociationService;
    private readonly BackupService _backupService;
    private readonly KeyBindingService _keyBindingService;
    private readonly Action<string, string> _showToast;
    private readonly Action _openMigration;
    private readonly Action _openDesignShowcase;
    private readonly Action<ToastProgressViewModel> _showProgressToast;
    private readonly Action<ToastProgressViewModel> _closeProgressToast;
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
        Action<ToastProgressViewModel> showProgressToast,
        Action<ToastProgressViewModel> closeProgressToast,
        Action reloadFolderWatch,
        Action openDesignShowcase)
        : this(skinService, filePicker, libraryScanner, fileAssociationService, backupService, keyBindingService, showToast, migration, plugin, openMigration, showProgressToast, closeProgressToast, reloadFolderWatch, openDesignShowcase, PaperbunkrDb.CreateContext)
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
        Action<ToastProgressViewModel> showProgressToast,
        Action<ToastProgressViewModel> closeProgressToast,
        Action reloadFolderWatch,
        Action openDesignShowcase,
        Func<PaperbunkrDbContext> contextFactory)
    {
        _openDesignShowcase = openDesignShowcase;
        _skinService = skinService;
        _filePicker = filePicker;
        _libraryScanner = libraryScanner;
        _fileAssociationService = fileAssociationService;
        _backupService = backupService;
        _keyBindingService = keyBindingService;
        _showToast = showToast;
        Migration = migration;
        Plugin = plugin;
        _openMigration = openMigration;
        _showProgressToast = showProgressToast;
        _closeProgressToast = closeProgressToast;
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

    public static readonly ImageFitMode[] FitModeOptions = Enum.GetValues<ImageFitMode>();

    /// <summary>docs/superpowers/specs/2026-08-13-reader-page-transition-animations-design.md §6.</summary>
    public static readonly PageTransitionStyle[] PageTransitionStyleOptions = Enum.GetValues<PageTransitionStyle>();

    /// <summary>docs/superpowers/specs/2026-08-15-reader-double-page-spread-design.md §2.</summary>
    public static readonly PageLayoutMode[] PageLayoutModeOptions = Enum.GetValues<PageLayoutMode>();

    public static readonly ImageBackgroundMode[] BackgroundModeOptions = Enum.GetValues<ImageBackgroundMode>();

    /// <summary>docs/superpowers/specs/2026-08-27-hardware-accelerated-rendering-design.md §10.</summary>
    public static readonly RenderBackend[] RenderBackendOptions = Enum.GetValues<RenderBackend>();

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

    [ObservableProperty]
    private PreferencesSection _activeSection = PreferencesSection.General;

    public bool IsGeneralSection => ActiveSection == PreferencesSection.General;
    public bool IsAppearanceSection => ActiveSection == PreferencesSection.Appearance;
    public bool IsLibrarySection => ActiveSection == PreferencesSection.Library;
    public bool IsReaderSection => ActiveSection == PreferencesSection.Reader;
    public bool IsKeyboardShortcutsSection => ActiveSection == PreferencesSection.KeyboardShortcuts;
    public bool IsConnectionsSection => ActiveSection == PreferencesSection.Connections;
    public bool IsPluginsSection => ActiveSection == PreferencesSection.Plugins;
    public bool IsAdvancedSection => ActiveSection == PreferencesSection.Advanced;

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
    private ImageFitMode _defaultPageFitMode = ImageFitMode.FitWidth;

    [ObservableProperty]
    private bool _defaultAutoRotate;

    /// <summary>Global default for a book with no <see cref="Series.PageLayoutMode"/>/<see cref="Issue.PageLayoutModeOverride"/> set (docs/superpowers/specs/2026-08-15-reader-double-page-spread-design.md §2).</summary>
    [ObservableProperty]
    private PageLayoutMode _defaultPageLayoutMode = PageLayoutMode.Single;

    /// <summary>docs/superpowers/specs/2026-08-13-reader-page-transition-animations-design.md §2 - default <see cref="PageTransitionStyle.None"/>, matching CE's own <c>BlendWhilePaging</c> default of <c>false</c>.</summary>
    [ObservableProperty]
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
    private ImageBackgroundMode _imageBackgroundMode = ImageBackgroundMode.Color;

    /// <summary>CE default "WhiteSmoke" (<c>DisplayWorkspace.BackgroundColor</c>) - a named or hex color string, parsed by <c>ReaderScreenViewModel.ComputeCanvasBackgroundBrush</c>.</summary>
    [ObservableProperty]
    private string _backgroundColor = "WhiteSmoke";

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
        OnPropertyChanged(nameof(IsReaderSection));
        OnPropertyChanged(nameof(IsKeyboardShortcutsSection));
        OnPropertyChanged(nameof(IsConnectionsSection));
        OnPropertyChanged(nameof(IsPluginsSection));
        OnPropertyChanged(nameof(IsAdvancedSection));
    }

    [RelayCommand]
    private void GoGeneral() => ActiveSection = PreferencesSection.General;

    [RelayCommand]
    private void GoAppearance() => ActiveSection = PreferencesSection.Appearance;

    [RelayCommand]
    private void GoLibrary() => ActiveSection = PreferencesSection.Library;

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
        PageMarginEnabled = settings.PageMarginEnabled;
        PageMarginPercentWidth = settings.PageMarginPercentWidth;
        RenderingBackend = settings.RenderingBackend;
        PreferNativeOpenGl = settings.PreferNativeOpenGl;
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
        _suppressBackupSettingsApply = false;
        RefreshBackups();

        RefreshKeyBindings();
        RefreshSourceCredentials(context);
        RefreshTrackerConnectionState(context);
    }

    // ===================== Keyboard Shortcuts (docs/alpha-roadmap.md P5 follow-up) =====================

    private void RefreshKeyBindings()
    {
        NavigationKeyBindings.Clear();
        ZoomFitKeyBindings.Clear();
        DisplayKeyBindings.Clear();

        foreach (var (command, currentKey) in _keyBindingService.GetAllBindings())
        {
            var row = new KeyBindingRowViewModel(command, currentKey, _keyBindingService, RecomputeKeyBindingConflict);
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
    /// Soft validation, not a hard block - the row already persisted its new key by the time this
    /// runs (matches every other Preferences toggle's immediate-persist behavior). Two commands
    /// conflict iff their gestures are equal AND (their <see cref="ConflictContext"/>s match OR
    /// either is <see cref="ConflictContext.Always"/>) - see that enum's own doc comment for why:
    /// mode-specific contexts (paged/zoomed/continuous) are mutually exclusive at runtime, so
    /// sharing a gesture across two of them is never actually reachable, but an Always command
    /// unconditionally shadows every mode-specific one it collides with.
    /// </summary>
    private void RecomputeKeyBindingConflict()
    {
        var all = NavigationKeyBindings.Concat(ZoomFitKeyBindings).Concat(DisplayKeyBindings).ToList();
        for (int i = 0; i < all.Count; i++)
        {
            for (int j = i + 1; j < all.Count; j++)
            {
                var a = all[i];
                var b = all[j];
                if (a.SelectedKey.Gesture != b.SelectedKey.Gesture)
                {
                    continue;
                }

                if (a.Context != ConflictContext.Always && b.Context != ConflictContext.Always && a.Context != b.Context)
                {
                    continue;
                }

                KeyBindingConflictError = $"\"{a.SelectedKey.Label}\" is assigned to both \"{a.Label}\" and \"{b.Label}\".";
                return;
            }
        }

        KeyBindingConflictError = null;
    }

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

    partial void OnSelectedVirtualTagIdChanged(int? value) => OnPropertyChanged(nameof(HasSelectedVirtualTag));

    private void RefreshVirtualTags()
    {
        using var context = _contextFactory();
        VirtualTags.Clear();
        foreach (var tag in context.VirtualTagDefinitions.OrderBy(v => v.SortOrder))
        {
            VirtualTags.Add(new VirtualTagSummary { Id = tag.Id, Name = tag.Name, IsEnabled = tag.IsEnabled });
        }
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
    private string? _scanStatus;

    [ObservableProperty]
    private bool _isScanning;

    private void RefreshWatchedFolders()
    {
        using var context = _contextFactory();
        WatchedFolders.Clear();
        foreach (var folder in context.WatchedFolders.OrderBy(w => w.Path))
        {
            WatchedFolders.Add(new WatchedFolderSummary { Id = folder.Id, Path = folder.Path, Watch = folder.Watch });
        }
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
    private void RemoveFolder(WatchedFolderSummary folder)
    {
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
    private string? _bookScanStatus;

    [ObservableProperty]
    private bool _isScanningBooks;

    private void RefreshBookFolders()
    {
        using var context = _contextFactory();
        BookFolders.Clear();
        foreach (var folder in context.BookFolders.OrderBy(f => f.Path))
        {
            BookFolders.Add(new BookFolderSummary { Id = folder.Id, Path = folder.Path });
        }
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
        BookScanStatus = "Scanning…";
        try
        {
            var scanProgress = new Progress<(int Done, int Total)>(p => BookScanStatus = $"Scanning… {p.Done}/{p.Total}");
            var result = await new BookFolderScanService().ScanAllAsync(scanProgress);

            if (result.BooksAdded > 0)
            {
                var coverProgress = new Progress<(int Done, int Total)>(p => BookScanStatus = $"Generating covers… {p.Done}/{p.Total}");
                await new BookCoverThumbnailService(_contextFactory).GenerateAllAsync(coverProgress);
            }

            BookScanStatus = result.BooksAdded == 0
                ? "No new books found."
                : $"Added {result.BooksAdded} book{(result.BooksAdded == 1 ? "" : "s")} across {result.SeriesTouched} series.";
            _showToast("Book scan complete", BookScanStatus);
        }
        catch (Exception ex)
        {
            BookScanStatus = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanningBooks = false;
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
        ScanStatus = "Scanning…";
        try
        {
            var progress = new Progress<(int Done, int Total)>(p => ScanStatus = $"Scanning… {p.Done}/{p.Total}");
            var result = await _libraryScanner.ScanAllAsync(progress);
            string summary = result.IssuesAdded == 0
                ? "No new issues found."
                : $"Added {result.IssuesAdded} issue{(result.IssuesAdded == 1 ? "" : "s")} across {result.SeriesTouched} series.";

            if (result.IssuesAdded > 0)
            {
                // Newly-added issues have no cached cover yet - generate them now instead of
                // leaving Library showing blank placeholders until someone finds the separate
                // "Generate Covers" button on the Library screen (same pipeline it uses).
                var coverProgress = new Progress<(int Done, int Total)>(p => ScanStatus = $"Generating covers… {p.Done}/{p.Total}");
                await new CoverThumbnailService(_contextFactory).GenerateAllAsync(coverProgress);
            }

            ScanStatus = summary;

            // Toast alongside the inline ScanStatus text (P6 follow-up) - scanning can take a
            // while on a large folder, and the inline status is easy to miss if the user's
            // navigated to a different screen while it runs.
            _showToast("Scan complete", ScanStatus);
        }
        finally
        {
            IsScanning = false;
        }
    }

    [ObservableProperty]
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
        var toast = new ToastProgressViewModel("Generating covers…");
        _showProgressToast(toast);

        try
        {
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                toast.Done = p.Done;
                toast.Total = p.Total;
            });
            await new CoverThumbnailService(_contextFactory).GenerateAllAsync(progress);
        }
        finally
        {
            IsGeneratingCovers = false;
            _closeProgressToast(toast);
            _showToast("Covers generated", $"Checked {toast.Total} issue{(toast.Total == 1 ? "" : "s")}.");
        }
    }

    [ObservableProperty]
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
        var toast = new ToastProgressViewModel("Verifying covers…");
        _showProgressToast(toast);

        // Two sequential passes sharing one toast - accumulate rather than assign directly, or the
        // second pass's smaller Total would make the bar jump backward and undercount the summary.
        int comicTotal = 0;
        int bookTotal = 0;
        try
        {
            var comicProgress = new Progress<(int Done, int Total)>(p =>
            {
                comicTotal = p.Total;
                toast.Done = p.Done;
                toast.Total = comicTotal;
            });
            await new CoverThumbnailService(_contextFactory).VerifyAllAsync(comicProgress);

            var bookProgress = new Progress<(int Done, int Total)>(p =>
            {
                bookTotal = p.Total;
                toast.Done = comicTotal + p.Done;
                toast.Total = comicTotal + bookTotal;
            });
            await new BookCoverThumbnailService(_contextFactory).VerifyAllAsync(bookProgress);
        }
        finally
        {
            IsVerifyingCovers = false;
            _closeProgressToast(toast);
            int total = comicTotal + bookTotal;
            _showToast("Covers verified", $"Re-checked {total} cover{(total == 1 ? "" : "s")}.");
        }
    }

    [ObservableProperty]
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
        var toast = new ToastProgressViewModel("Syncing metadata…");
        _showProgressToast(toast);

        try
        {
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                toast.Done = p.Done;
                toast.Total = p.Total;
            });
            var result = await _libraryScanner.SyncMetadataAsync(progress);
            _showToast(
                "Metadata sync complete",
                result.IssuesUpdated == 0
                    ? "No new metadata found."
                    : $"Updated {result.IssuesUpdated} issue{(result.IssuesUpdated == 1 ? "" : "s")}.");
        }
        finally
        {
            IsSyncingMetadata = false;
            _closeProgressToast(toast);
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

    [ObservableProperty]
    private string _backupLocation = string.Empty;

    [ObservableProperty]
    private int _backupsToKeep;

    [ObservableProperty]
    private string? _backupStatus;

    private void RefreshBackups()
    {
        Backups.Clear();
        foreach (string path in _backupService.GetAvailableBackups())
        {
            Backups.Add(new BackupRowViewModel(path, OnRestoreBackupConfirmed));
        }
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
    // arc-lookup-design.md §5) - ComicVine/Metron credentials, backed by CredentialStore. =====================

    [ObservableProperty]
    private string _comicVineApiKey = string.Empty;

    [ObservableProperty]
    private string _metronUsername = string.Empty;

    [ObservableProperty]
    private string _metronPassword = string.Empty;

    [ObservableProperty]
    private string? _sourcesStatus;

    private void RefreshSourceCredentials(PaperbunkrDbContext context)
    {
        ComicVineApiKey = CredentialStore.Get(context, "ComicVine", CredentialKind.ApiKey) ?? string.Empty;
        MetronUsername = CredentialStore.Get(context, "Metron", CredentialKind.Username) ?? string.Empty;
        MetronPassword = CredentialStore.Get(context, "Metron", CredentialKind.Password) ?? string.Empty;
    }

    [RelayCommand]
    private void SaveComicVineCredentials()
    {
        using var context = _contextFactory();
        CredentialStore.Set(context, "ComicVine", CredentialKind.ApiKey, ComicVineApiKey);
        SourcesStatus = "ComicVine API key saved.";
    }

    [RelayCommand]
    private void SaveMetronCredentials()
    {
        using var context = _contextFactory();
        CredentialStore.Set(context, "Metron", CredentialKind.Username, MetronUsername);
        CredentialStore.Set(context, "Metron", CredentialKind.Password, MetronPassword);
        SourcesStatus = "Metron credentials saved.";
    }

    // ===================== Trackers (docs/superpowers/specs/2026-08-23-tracker-write-back-sync-
    // design.md) - AniList/MyAnimeList/Shikimori use browser OAuth (each needs the user's own
    // registered Client ID, same "user registers their own app" model as every OAuth flow here, not
    // a Paperbunkr-wide embedded client), Bangumi uses a pasted Personal Access Token instead
    // (deliberate asymmetry - see the design spec's own "why the four aren't uniform" section).
    // =====================

    [ObservableProperty] private string _aniListClientId = string.Empty;
    [ObservableProperty] private string _aniListPastedToken = string.Empty;
    [ObservableProperty] private bool _isAniListConnected;

    [ObservableProperty] private string _myAnimeListClientId = string.Empty;
    [ObservableProperty] private string _myAnimeListPastedCode = string.Empty;
    [ObservableProperty] private bool _isMyAnimeListConnected;
    private string? _myAnimeListCodeVerifier;

    [ObservableProperty] private string _shikimoriClientId = string.Empty;
    [ObservableProperty] private string _shikimoriClientSecret = string.Empty;
    [ObservableProperty] private string _shikimoriPastedCode = string.Empty;
    [ObservableProperty] private bool _isShikimoriConnected;

    [ObservableProperty] private string _bangumiPersonalAccessToken = string.Empty;
    [ObservableProperty] private bool _isBangumiConnected;

    [ObservableProperty] private string _mangaBakaPersonalAccessToken = string.Empty;
    [ObservableProperty] private bool _isMangaBakaConnected;

    [ObservableProperty] private string? _trackersStatus;

    private void RefreshTrackerConnectionState(PaperbunkrDbContext context)
    {
        IsAniListConnected = CredentialStore.HasCredentials(context, nameof(TrackingService.AniList), CredentialKind.OAuthAccessToken);
        IsMyAnimeListConnected = CredentialStore.HasCredentials(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthAccessToken);
        IsShikimoriConnected = CredentialStore.HasCredentials(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthAccessToken);
        IsBangumiConnected = CredentialStore.HasCredentials(context, nameof(TrackingService.Bangumi), CredentialKind.ApiKey);
        IsMangaBakaConnected = CredentialStore.HasCredentials(context, nameof(TrackingService.MangaBaka), CredentialKind.ApiKey);

        AniListClientId = CredentialStore.Get(context, nameof(TrackingService.AniList), CredentialKind.OAuthClientId) ?? string.Empty;
        MyAnimeListClientId = CredentialStore.Get(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthClientId) ?? string.Empty;
        ShikimoriClientId = CredentialStore.Get(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthClientId) ?? string.Empty;
        ShikimoriClientSecret = CredentialStore.Get(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthClientSecret) ?? string.Empty;
    }

    [RelayCommand]
    private void ConnectAniList()
    {
        using var context = _contextFactory();
        CredentialStore.Set(context, nameof(TrackingService.AniList), CredentialKind.OAuthClientId, AniListClientId);
        Process.Start(new ProcessStartInfo { FileName = AniListTrackerAdapter.BuildAuthorizationUrl(AniListClientId), UseShellExecute = true });
        TrackersStatus = "Complete sign-in in your browser, then paste the token back here.";
    }

    [RelayCommand]
    private void CompleteAniListConnect()
    {
        using var context = _contextFactory();
        AniListTrackerAdapter.CompleteConnect(context, AniListPastedToken);
        AniListPastedToken = string.Empty;
        RefreshTrackerConnectionState(context);
        TrackersStatus = "AniList connected.";
    }

    [RelayCommand]
    private void ConnectMyAnimeList()
    {
        using var context = _contextFactory();
        CredentialStore.Set(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthClientId, MyAnimeListClientId);
        _myAnimeListCodeVerifier = MyAnimeListTrackerAdapter.GenerateCodeVerifier();
        Process.Start(new ProcessStartInfo
        {
            FileName = MyAnimeListTrackerAdapter.BuildAuthorizationUrl(MyAnimeListClientId, _myAnimeListCodeVerifier),
            UseShellExecute = true,
        });
        TrackersStatus = "The page after sign-in will fail to load - that's expected. Copy the \"code\" value from its address bar and paste it back here.";
    }

    [RelayCommand]
    private async Task CompleteMyAnimeListConnectAsync()
    {
        if (_myAnimeListCodeVerifier is null)
        {
            TrackersStatus = "Click Connect first.";
            return;
        }

        using var context = _contextFactory();
        var adapter = new MyAnimeListTrackerAdapter(TrackerHttpClients.MyAnimeList, MyAnimeListClientId);
        bool connected = await adapter.CompleteConnectAsync(context, MyAnimeListClientId, _myAnimeListCodeVerifier, MyAnimeListPastedCode, default);

        MyAnimeListPastedCode = string.Empty;
        _myAnimeListCodeVerifier = null;
        RefreshTrackerConnectionState(context);
        TrackersStatus = connected ? "MyAnimeList connected." : "Couldn't connect to MyAnimeList. Check your Client ID and try again.";
    }

    [RelayCommand]
    private void ConnectShikimori()
    {
        using var context = _contextFactory();
        CredentialStore.Set(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthClientId, ShikimoriClientId);
        CredentialStore.Set(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthClientSecret, ShikimoriClientSecret);
        Process.Start(new ProcessStartInfo { FileName = ShikimoriTrackerAdapter.BuildAuthorizationUrl(ShikimoriClientId), UseShellExecute = true });
        TrackersStatus = "Shikimori will show you a code to copy - paste it back here.";
    }

    [RelayCommand]
    private async Task CompleteShikimoriConnectAsync()
    {
        using var context = _contextFactory();
        var adapter = new ShikimoriTrackerAdapter(TrackerHttpClients.Shikimori);
        bool connected = await adapter.CompleteConnectAsync(context, ShikimoriClientId, ShikimoriClientSecret, ShikimoriPastedCode, default);

        ShikimoriPastedCode = string.Empty;
        RefreshTrackerConnectionState(context);
        TrackersStatus = connected ? "Shikimori connected." : "Couldn't connect to Shikimori. Check your Client ID/Secret and try again.";
    }

    [RelayCommand]
    private void SaveBangumiToken()
    {
        using var context = _contextFactory();
        BangumiTrackerAdapter.CompleteConnect(context, BangumiPersonalAccessToken);
        BangumiPersonalAccessToken = string.Empty;
        RefreshTrackerConnectionState(context);
        TrackersStatus = "Bangumi token saved.";
    }

    [RelayCommand]
    private void SaveMangaBakaToken()
    {
        using var context = _contextFactory();
        MangaBakaTrackerAdapter.CompleteConnect(context, MangaBakaPersonalAccessToken);
        MangaBakaPersonalAccessToken = string.Empty;
        RefreshTrackerConnectionState(context);
        TrackersStatus = "MangaBaka token saved.";
    }
}
