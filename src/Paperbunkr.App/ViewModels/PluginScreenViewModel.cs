using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Abstractions.Ui;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Plugin screen (docs/superpowers/specs/2026-09-12-plugin-management-screen-redesign-design.md) -
/// master-detail: a sidebar of installed packages (<see cref="Packages"/>/<see cref="FilteredPackages"/>)
/// and a detail pane for whichever one is selected (<see cref="SelectedPackageDetail"/>). Replaces the
/// previous flat "Packages panel + commands grouped by hook" layout (docs/superpowers/specs/2026-08-
/// 24-plugin-api-v2-design.md §6) - that grouping scattered one plugin's own commands across
/// unrelated sections and gave no way to reach a native plugin's compiled settings UI at all.
/// </summary>
public partial class PluginScreenViewModel : ViewModelBase
{
    private readonly IFilePickerService _filePicker;
    private readonly PluginPackageService _packageService;
    private readonly IDialogService _dialogs;
    private PluginHostService? _host;

    public PluginScreenViewModel(IFilePickerService filePicker, IDialogService dialogs)
        : this(filePicker, dialogs, new PluginPackageService())
    {
    }

    /// <summary>Test seam - substitute a <see cref="PluginPackageService"/> pointed at an isolated folder pair instead of the real %AppData% one.</summary>
    internal PluginScreenViewModel(IFilePickerService filePicker, IDialogService dialogs, PluginPackageService packageService)
    {
        _filePicker = filePicker;
        _dialogs = dialogs;
        _packageService = packageService;
    }

    public ObservableCollection<PluginPackageRowViewModel> Packages { get; } = new();

    public ObservableCollection<PluginPackageRowViewModel> FilteredPackages { get; } = new();

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private PluginPackageDetailViewModel? _selectedPackageDetail;

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    /// <summary>Called once from <c>App.axaml.cs</c> after <see cref="PluginHostService.Initialize"/> has discovered/precompiled every plugin - the host doesn't exist yet when this ViewModel is constructed in <c>MainViewModel</c>'s own constructor.</summary>
    public void AttachHost(PluginHostService host)
    {
        _host = host;
        Refresh();
    }

    public void Refresh()
    {
        string? selectedKey = SelectedPackageDetail?.PackageKey;

        Packages.Clear();
        SelectedPackageDetail = null;

        // An update-in-progress (design note: PackageManager.Install now auto-stages the older,
        // same-key copy for removal instead of requiring a separate manual Remove first) briefly
        // has TWO folders for the one logical plugin - the still-Installed old version and the new
        // PendingInstall one - until the next restart commits both. Group by key so that shows as
        // one row (the pending copy, since that's what the user is about to get), not two.
        foreach (var group in _packageService.GetPackages().GroupBy(p => p.Key))
        {
            var candidates = group.ToList();
            PackageManager.Package package = candidates.FirstOrDefault(p => p.PackageType == PackageManager.PackageType.PendingInstall) ?? candidates[0];
            bool isUpdate = candidates.Count > 1;
            bool isBroken = IsPackageBroken(package);
            var row = new PluginPackageRowViewModel(package, isBroken, SelectPackage, isUpdate);
            Packages.Add(row);
        }

        ApplyFilter();

        // Re-select the same package by key across a refresh (e.g. after Reload/master-toggle),
        // rather than silently losing the user's current selection.
        if (selectedKey is not null)
        {
            var toReselect = Packages.FirstOrDefault(p => p.Package.Key == selectedKey);
            if (toReselect is not null)
            {
                SelectPackage(toReselect);
            }
        }
    }

    private void ApplyFilter()
    {
        FilteredPackages.Clear();
        var matches = string.IsNullOrWhiteSpace(FilterText)
            ? Packages
            : Packages.Where(p => p.Name.Contains(FilterText, System.StringComparison.OrdinalIgnoreCase));

        foreach (var package in matches)
        {
            FilteredPackages.Add(package);
        }
    }

    private void SelectPackage(PluginPackageRowViewModel row)
    {
        if (_host is null)
        {
            return;
        }

        var scopedCommands = _host.Engine.AllCommands.Where(c => c.PluginKey == row.Package.Key).ToList();
        var loadResult = _host.Engine.NativeLoadResults.GetValueOrDefault(row.Package.Key);
        var apiInfo = _host.Engine.PackageApiInfo.GetValueOrDefault(row.Package.Key);
        SelectedPackageDetail = new PluginPackageDetailViewModel(
            row.Package,
            scopedCommands,
            _host,
            _filePicker,
            // A native package carries its own load error (which already includes a requiresApi block);
            // a script package blocked by requiresApi registers no commands, so the engine's blocked
            // reason is the only place that failure is visible - show it in the same banner.
            loadError: loadResult?.LoadError ?? apiInfo?.BlockedReason,
            hasConfigure: loadResult?.Module is INativePluginSettingsUi || _host.Engine.SettingsSchemas.ContainsKey(row.Package.Key),
            onRemoved: () => RemovePackage(row.Package),
            onRefreshRequested: Refresh,
            requiresApi: apiInfo?.RequiresApi);
    }

    /// <summary>Health dot (docs §4.3) - a native package whose own load failed is broken; a package
    /// (either tier) that produced at least one command and every one of them is broken is broken.
    /// A package with zero discovered commands is never automatically flagged broken here - that's
    /// indistinguishable from a legitimate config-only script package or a native package that simply
    /// registers nothing, and guessing based on what *other* packages produced in the same pass isn't
    /// a real signal about *this* one (a design-doc phrasing this implementation corrected once
    /// actually written out - see §4.3's own note).</summary>
    private bool IsPackageBroken(PackageManager.Package package)
    {
        if (_host is null)
        {
            return false;
        }

        if (package.IsNativeTier)
        {
            var loadResult = _host.Engine.NativeLoadResults.GetValueOrDefault(package.Key);
            if (loadResult?.LoadError is not null)
            {
                return true;
            }
        }

        // A script package blocked by requiresApi registers zero commands, and the rule below
        // deliberately never flags a zero-command package broken - so it needs its own check.
        if (_host.Engine.PackageApiInfo.GetValueOrDefault(package.Key)?.BlockedReason is not null)
        {
            return true;
        }

        var ownCommands = _host.Engine.AllCommands.Where(c => c.PluginKey == package.Key).ToList();
        return ownCommands.Count > 0 && ownCommands.All(c => c.IsBroken);
    }

    /// <summary>
    /// Opens a file picker for a plugin package zip (CE's own "Script Archive|*.zip" format - see
    /// <see cref="PluginPackageService"/>), confirms an overwrite if a same-named package is already
    /// installed (matching CE's own prompt), installs, and re-discovers immediately - no restart.
    /// </summary>
    [RelayCommand]
    private async Task InstallPackage()
    {
        string? file = await _filePicker.PickPluginPackageFileAsync("Install Plugin Package");
        if (file is null)
        {
            return;
        }

        // Version-aware confirm (design note: re-picking a newer build of an already-installed
        // plugin is now a real one-step update - PackageManager.Install auto-stages the old, same-
        // key copy for removal - so the prompt should say so instead of the old generic "Overwrite?"
        // that gave no sense of what was actually changing).
        PackageManager.Package? incoming = TryPeekPackage(file);
        PackageManager.Package? installed = incoming is not null ? _packageService.GetInstalledPackageByKey(incoming.Key) : null;

        if (installed is not null)
        {
            string prompt = DescribeVersionChange(installed.Version, incoming!.Version) switch
            {
                VersionChange.Newer => $"Update \"{installed.Name}\" from v{installed.Version} to v{incoming.Version}?",
                VersionChange.Older => $"This build (v{incoming.Version}) is older than the installed v{installed.Version} of \"{installed.Name}\". Install it anyway?",
                VersionChange.Same => $"Reinstall \"{installed.Name}\" v{installed.Version}?",
                _ => $"\"{installed.Name}\" is already installed. Replace it with this build?",
            };
            bool proceed = await _dialogs.ConfirmAsync(prompt, confirmLabel: "Install", cancelLabel: "Cancel");
            if (!proceed)
            {
                return;
            }
        }
        else if (_packageService.PackageFileExists(file))
        {
            // Same display name but a different (or unreadable) key - genuinely ambiguous, keep the
            // original safe/generic wording rather than guessing at an update relationship.
            bool overwrite = await _dialogs.ConfirmAsync(
                "A plugin package with this name is already installed. Overwrite it?",
                confirmLabel: "Overwrite", cancelLabel: "Cancel");
            if (!overwrite)
            {
                return;
            }
        }

        if (!_packageService.Install(file))
        {
            _host?.ShowToast("Plugin package", "That file isn't a readable plugin package (.pbplugin/.zip).");
            return;
        }

        _host?.RediscoverPlugins();
        Refresh();

        // A Native-tier install stays pending until the next launch (docs/superpowers/specs/2026-09-
        // 11-plugin-api-v4-native-tier-design.md §4) - the toast has to say so, not claim "no restart
        // needed" the way every Script-tier install still truthfully can.
        var justInstalled = Packages.FirstOrDefault(p => p.Package.PackageType == PackageManager.PackageType.PendingInstall);
        if (justInstalled is not null)
        {
            string verb = justInstalled.IsUpdatePending ? "updating" : "setting it up";
            _host?.ShowToast("Plugin package", $"Installed - restart Paperbunkr to finish {verb} (full read/write access to your library database).");
            _host?.RaisePendingRestartAlert(
                justInstalled.IsUpdatePending ? "Plugin update pending" : "Plugin install pending",
                justInstalled.IsUpdatePending
                    ? $"\"{justInstalled.Package.Name}\" v{justInstalled.Package.Version} is ready - restart to finish updating."
                    : $"\"{justInstalled.Package.Name}\" needs a restart to finish installing.",
                dedupeKey: "plugin-restart-pending");
        }
        else
        {
            _host?.ShowToast("Plugin package", "Installed - no restart needed.");
        }
    }

    /// <summary>Peeks a package file's manifest (key/name/version) without staging an install -
    /// <see cref="PackageManager.Package.CreateFromFile"/> already does this exact peek internally
    /// for <see cref="PluginPackageService.PackageFileExists"/>; reusing it here directly avoids a
    /// second, parallel peek path.</summary>
    private static PackageManager.Package? TryPeekPackage(string file)
    {
        try
        {
            return PackageManager.Package.CreateFromFile(file);
        }
        catch
        {
            return null;
        }
    }

    private enum VersionChange { Unknown, Newer, Same, Older }

    /// <summary>Best-effort semantic-version compare for the install-confirm prompt's wording only -
    /// never blocks an install either way, a plugin author's own version string not parsing as a
    /// <see cref="Version"/> just means the prompt falls back to generic "replace" wording instead of
    /// a specific newer/older/same claim it can't actually verify.</summary>
    private static VersionChange DescribeVersionChange(string? installedVersion, string? incomingVersion)
    {
        if (!System.Version.TryParse(installedVersion, out var installed) || !System.Version.TryParse(incomingVersion, out var incoming))
        {
            return VersionChange.Unknown;
        }

        return incoming.CompareTo(installed) switch
        {
            > 0 => VersionChange.Newer,
            0 => VersionChange.Same,
            _ => VersionChange.Older,
        };
    }

    private void RemovePackage(PackageManager.Package package)
    {
        _packageService.Uninstall(package);
        _host?.RediscoverPlugins();

        // Deferred: this command runs from the detail pane's own TwoStepConfirm "Confirm" Button.Click
        // still routing through this same view. Refresh clears Packages/SelectedPackageDetail, which
        // would detach that same button mid-route and crash Avalonia's detach walk with an
        // ArgumentOutOfRangeException (see Paperbunkr.App.Controls.SuggestBox.Commit for the fully
        // diagnosed case).
        Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);

        // Same restart-to-apply gap as InstallPackage above - only the Native tier defers the actual
        // removal (Uninstall just drops a ".remove" marker for it), so only that tier needs telling.
        if (package.IsNativeTier)
        {
            _host?.RaisePendingRestartAlert(
                "Plugin change pending",
                $"\"{package.Name}\" needs a restart to finish removing.",
                dedupeKey: "plugin-restart-pending");
        }
    }
}
