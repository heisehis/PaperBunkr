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

        foreach (PackageManager.Package package in _packageService.GetPackages())
        {
            bool isBroken = IsPackageBroken(package);
            var row = new PluginPackageRowViewModel(package, isBroken, SelectPackage);
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
        SelectedPackageDetail = new PluginPackageDetailViewModel(
            row.Package,
            scopedCommands,
            _host,
            _filePicker,
            loadError: loadResult?.LoadError,
            hasConfigure: loadResult?.Module is INativePluginSettingsUi,
            onRemoved: () => RemovePackage(row.Package),
            onRefreshRequested: Refresh);
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

        if (_packageService.PackageFileExists(file))
        {
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
        _host?.ShowToast("Plugin package", justInstalled is not null
            ? "Installed - restart Paperbunkr to finish setting it up (full read/write access to your library database)."
            : "Installed - no restart needed.");
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
    }
}
