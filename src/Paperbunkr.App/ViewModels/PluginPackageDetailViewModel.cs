using System;
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

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The selected package's detail pane (docs/superpowers/specs/2026-09-12-plugin-management-screen-
/// redesign-design.md §4.4/§4.5) - identity/health header, native settings entry point, script-tier
/// Reload, the master enable/disable toggle, and the commands this specific package owns. Built
/// fresh by <see cref="PluginScreenViewModel"/> each time a sidebar row is selected or the screen
/// refreshes - there's no long-lived mutable state here beyond what <see cref="TwoStepConfirm"/>
/// itself owns, so a stale instance is simply discarded and replaced rather than updated in place.
/// </summary>
public sealed partial class PluginPackageDetailViewModel : ViewModelBase
{
    private readonly PackageManager.Package _package;
    private readonly PluginHostService _host;
    private readonly IFilePickerService _filePicker;
    private readonly Action _onRefreshRequested;

    /// <summary>
    /// <paramref name="loadError"/>/<paramref name="hasConfigure"/> are passed in already-resolved
    /// (from <see cref="PluginEngine.NativeLoadResults"/>) rather than looked up here - keeps this
    /// ViewModel constructible/testable without a real <see cref="PluginEngine"/> behind it.
    /// </summary>
    public PluginPackageDetailViewModel(
        PackageManager.Package package,
        IReadOnlyList<Command> commands,
        PluginHostService host,
        IFilePickerService filePicker,
        string? loadError,
        bool hasConfigure,
        Action onRemoved,
        Action onRefreshRequested)
    {
        _package = package;
        _host = host;
        _filePicker = filePicker;
        _onRefreshRequested = onRefreshRequested;
        DeleteConfirm = new TwoStepConfirm(onRemoved, idleLabel: "Remove", armedLabel: "Confirm remove?");

        Commands = new ObservableCollection<PluginCommandRowViewModel>(
            commands.Select(c => new PluginCommandRowViewModel(c, host)));

        EnabledState = commands.Count == 0
            ? true
            : commands.All(c => c.Enabled)
                ? true
                : commands.All(c => !c.Enabled)
                    ? false
                    : null;

        LoadError = loadError;
        HasConfigure = hasConfigure;
    }

    /// <summary>The correlation key this detail pane was built for - lets <see cref="PluginScreenViewModel.Refresh"/> re-select the same package across a rebuild (e.g. after Reload/master-toggle) without holding a reference to a stale <see cref="PackageManager.Package"/> instance.</summary>
    public string PackageKey => _package.Key;

    public string Name => _package.Name;

    public string? Version => string.IsNullOrEmpty(_package.Version) ? null : _package.Version;

    public string? Author => string.IsNullOrEmpty(_package.Author) ? null : _package.Author;

    public bool IsNativeTier => _package.IsNativeTier;

    public bool IsPending => _package.PackageType is PackageManager.PackageType.PendingInstall or PackageManager.PackageType.PendingRemove;

    /// <summary>Non-null only for a native package whose load failed (docs §4.2/§4.3) - replaces the
    /// old silent vanish with a real, visible message.</summary>
    public string? LoadError { get; }

    public bool HasLoadError => LoadError is not null;

    /// <summary>True only when this package's loaded native module also implements
    /// <c>INativePluginSettingsUi</c> - a script-tier package, an unloaded/broken native package, and
    /// a native package with no settings UI all show no Configure button here.</summary>
    public bool HasConfigure { get; }

    [RelayCommand]
    private async Task Configure() => await _host.OpenPluginSettingsAsync(_package.Key);

    /// <summary>Script-tier only (docs §4.4) - a native package's load can only be re-attempted by
    /// actually reloading its AssemblyLoadContext, which "restart to apply" already covers; offering
    /// a Reload button here that can't do anything a restart doesn't already promise would be its
    /// own new kind of misleading.</summary>
    public bool ShowReload => !IsNativeTier;

    [RelayCommand]
    private void Reload()
    {
        _host.RediscoverPlugins();
        _onRefreshRequested();
    }

    [RelayCommand]
    private async Task CopyError()
    {
        if (LoadError is not null)
        {
            await _filePicker.SetClipboardTextAsync(LoadError);
        }
    }

    /// <summary>Tri-state (docs §4.4) - true when every command is enabled, false when every command
    /// is disabled, null when mixed. The view binds this one-way; the only writer is
    /// <see cref="MasterEnabledClick"/>.</summary>
    public bool? EnabledState { get; }

    /// <summary>
    /// Must not rely on Avalonia's own three-state <c>ToggleButton.Toggle()</c> cycle - verified
    /// against its real source, that cycle is <c>true → null → false → true</c>, so a plain two-way
    /// <c>IsChecked</c> binding would bulk-*disable* everything on a click starting from a mixed
    /// state. Bound to the view's <c>Command</c> instead (with `IsChecked` staying one-way), so the
    /// only two reachable outcomes are "enable everything" (from false or null) and "disable
    /// everything" (from true) - see the design doc §4.4 for the full citation.
    /// </summary>
    [RelayCommand]
    private void MasterEnabledClick()
    {
        bool next = EnabledState != true;
        _host.SetPackageEnabled(_package.Key, next);
        _onRefreshRequested();
    }

    public TwoStepConfirm DeleteConfirm { get; }

    public ObservableCollection<PluginCommandRowViewModel> Commands { get; }
}
