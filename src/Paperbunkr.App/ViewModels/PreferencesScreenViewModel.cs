using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
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
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private bool _isLoaded;
    private bool _suppressFontApply;
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
        Action<string, string> showToast)
        : this(skinService, filePicker, libraryScanner, fileAssociationService, backupService, keyBindingService, showToast, PaperbunkrDb.CreateContext)
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
        Func<PaperbunkrDbContext> contextFactory)
    {
        _skinService = skinService;
        _filePicker = filePicker;
        _libraryScanner = libraryScanner;
        _fileAssociationService = fileAssociationService;
        _backupService = backupService;
        _keyBindingService = keyBindingService;
        _showToast = showToast;
        _contextFactory = contextFactory;
        Skins = new ObservableCollection<SkinSummary>();
        FontFamilies = new ObservableCollection<string>();
        VirtualTags = new ObservableCollection<VirtualTagSummary>();
        WatchedFolders = new ObservableCollection<WatchedFolderSummary>();
        FileAssociations = new ObservableCollection<FileAssociationSummary>();
        Backups = new ObservableCollection<BackupRowViewModel>();
        KeyBindings = new ObservableCollection<KeyBindingRowViewModel>();
    }

    private static Issue SampleIssue() => new()
    {
        Number = "1",
        Volume = 1,
        Year = 2024,
        Title = "Sample Issue",
        Publisher = "Sample Publisher",
        Writer = "Sample Writer",
        Penciller = "Sample Penciller",
    };

    public ObservableCollection<SkinSummary> Skins { get; }

    public ObservableCollection<string> FontFamilies { get; }

    public ObservableCollection<VirtualTagSummary> VirtualTags { get; }

    public ObservableCollection<WatchedFolderSummary> WatchedFolders { get; }

    [ObservableProperty]
    private string _activeTab = "appearance";

    public bool IsAppearanceTab => ActiveTab == "appearance";
    public bool IsBehaviorTab => ActiveTab == "behavior";
    public bool IsLibrariesTab => ActiveTab == "libraries";
    public bool IsReaderTab => ActiveTab == "reader";
    public bool IsAdvancedTab => ActiveTab == "advanced";

    [ObservableProperty]
    private string? _selectedFontFamily;

    [ObservableProperty]
    private bool _openLastPage;

    [ObservableProperty]
    private bool _autoNavigateComics;

    [ObservableProperty]
    private bool _reverseRtlNavigation;

    [ObservableProperty]
    private bool _highQualityPageDisplay;

    /// <summary>Every registered <see cref="KeyboardCommandRegistry"/> command, data-driven so a future command needs no Preferences-side change - see <see cref="KeyboardCommandRegistry"/>'s remarks.</summary>
    public ObservableCollection<KeyBindingRowViewModel> KeyBindings { get; }

    [ObservableProperty]
    private string? _keyBindingConflictError;

    public bool HasKeyBindingConflictError => !string.IsNullOrEmpty(KeyBindingConflictError);

    partial void OnKeyBindingConflictErrorChanged(string? value) => OnPropertyChanged(nameof(HasKeyBindingConflictError));

    [ObservableProperty]
    private string? _installSkinError;

    public bool HasInstallSkinError => !string.IsNullOrEmpty(InstallSkinError);

    partial void OnInstallSkinErrorChanged(string? value) => OnPropertyChanged(nameof(HasInstallSkinError));

    partial void OnActiveTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsAppearanceTab));
        OnPropertyChanged(nameof(IsBehaviorTab));
        OnPropertyChanged(nameof(IsLibrariesTab));
        OnPropertyChanged(nameof(IsReaderTab));
        OnPropertyChanged(nameof(IsAdvancedTab));
    }

    [RelayCommand]
    private void GoAppearance() => ActiveTab = "appearance";

    [RelayCommand]
    private void GoBehavior() => ActiveTab = "behavior";

    [RelayCommand]
    private void GoLibraries() => ActiveTab = "libraries";

    [RelayCommand]
    private void GoReader() => ActiveTab = "reader";

    [RelayCommand]
    private void GoAdvanced() => ActiveTab = "advanced";

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

        using var context = _contextFactory();
        var settings = context.GetOrCreateAppSettings();
        _suppressBehaviorApply = true;
        OpenLastPage = settings.OpenLastPage;
        AutoNavigateComics = settings.AutoNavigateComics;
        ReverseRtlNavigation = settings.ReverseRtlNavigation;
        HighQualityPageDisplay = settings.HighQualityPageDisplay;
        _suppressBehaviorApply = false;

        var firstIssue = context.Issues.Include(i => i.Series).OrderBy(i => i.Id).FirstOrDefault();
        if (firstIssue is not null)
        {
            _previewIssue = firstIssue;
            _previewSeries = firstIssue.Series;
        }

        RefreshVirtualTags();
        RefreshWatchedFolders();
        RefreshFileAssociations();

        _suppressBackupSettingsApply = true;
        BackupLocation = _backupService.GetBackupLocation();
        BackupsToKeep = _backupService.GetBackupsToKeep();
        _suppressBackupSettingsApply = false;
        RefreshBackups();

        RefreshKeyBindings();
    }

    // ===================== Keyboard Shortcuts (docs/alpha-roadmap.md P5 follow-up) =====================

    private void RefreshKeyBindings()
    {
        KeyBindings.Clear();
        foreach (var (command, currentKey) in _keyBindingService.GetAllBindings())
        {
            KeyBindings.Add(new KeyBindingRowViewModel(command, currentKey, _keyBindingService, RecomputeKeyBindingConflict));
        }

        RecomputeKeyBindingConflict();
    }

    /// <summary>
    /// Soft validation, not a hard block - the row already persisted its new key by the time this
    /// runs (matches every other Preferences toggle's immediate-persist behavior). Only same-group
    /// collisions matter (e.g. Reader.PageTurnLeft/Right sharing a key would break navigation);
    /// commands in different groups are never active at the same time, so a cross-group repeat
    /// isn't actually a conflict.
    /// </summary>
    private void RecomputeKeyBindingConflict()
    {
        var conflict = KeyBindings
            .GroupBy(r => r.Group)
            .SelectMany(g => g.GroupBy(r => r.SelectedKey.Key))
            .FirstOrDefault(g => g.Count() > 1);

        KeyBindingConflictError = conflict is null
            ? null
            : $"\"{conflict.First().SelectedKey.Label}\" is assigned to more than one {conflict.First().Group} shortcut.";
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

    partial void OnOpenLastPageChanged(bool value) => PersistBehaviorSetting(s => s.OpenLastPage = value);

    partial void OnAutoNavigateComicsChanged(bool value) => PersistBehaviorSetting(s => s.AutoNavigateComics = value);

    partial void OnReverseRtlNavigationChanged(bool value) => PersistBehaviorSetting(s => s.ReverseRtlNavigation = value);

    partial void OnHighQualityPageDisplayChanged(bool value) => PersistBehaviorSetting(s => s.HighQualityPageDisplay = value);

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

    // ===================== Book Folders (docs/superpowers/specs/2026-08-07-preferences-libraries-tab-design.md §2) =====================

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
            WatchedFolders.Add(new WatchedFolderSummary { Id = folder.Id, Path = folder.Path });
        }
    }

    [RelayCommand]
    private async Task AddFolder()
    {
        string? path = await _filePicker.PickFolderAsync("Add Book Folder");
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
            ScanStatus = result.IssuesAdded == 0
                ? "No new issues found."
                : $"Added {result.IssuesAdded} issue{(result.IssuesAdded == 1 ? "" : "s")} across {result.SeriesTouched} series.";

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

    [RelayCommand]
    private void ToggleFileAssociation(FileAssociationSummary format)
    {
        _fileAssociationService.SetAssociated(format.Name, !format.IsAssociated);
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
}
