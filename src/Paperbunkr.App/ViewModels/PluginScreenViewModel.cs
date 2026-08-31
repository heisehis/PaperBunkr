using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Plugin screen (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §6) - lists every
/// discovered command grouped by hook/extension-point, with an enable/disable toggle, a Package
/// column, and a compile-error indicator per command. Grouping is by
/// <see cref="PluginCommandRowViewModel.HookGroupLabel"/> rather than by installed plugin
/// (matching ComicRackCE's real Preferences → Scripts tab - see <see cref="PluginGroupViewModel"/>)
/// - previously grouped by <c>Command.PluginKey</c> instead. Previously a permanent empty state (no
/// plugin engine existed at all - docs/superpowers/specs/2026-08-09-plugin-screen-cleanup-design.md);
/// the empty state is kept for the genuine zero-plugins case, just no longer the only state.
///
/// Also owns the CE-style Packages panel (install/uninstall) - see <see cref="PluginPackageService"/>
/// for why this commits and re-discovers immediately instead of requiring a restart like CE does.
/// </summary>
public partial class PluginScreenViewModel : ViewModelBase
{
    private readonly IFilePickerService _filePicker;
    private readonly PluginPackageService _packageService;
    private PluginHostService? _host;

    public PluginScreenViewModel(IFilePickerService filePicker) : this(filePicker, new PluginPackageService())
    {
    }

    /// <summary>Test seam - substitute a <see cref="PluginPackageService"/> pointed at an isolated folder pair instead of the real %AppData% one.</summary>
    internal PluginScreenViewModel(IFilePickerService filePicker, PluginPackageService packageService)
    {
        _filePicker = filePicker;
        _packageService = packageService;
    }

    public ObservableCollection<PluginGroupViewModel> Groups { get; } = new();

    public ObservableCollection<PluginPackageRowViewModel> Packages { get; } = new();

    [ObservableProperty]
    private bool _hasPlugins;

    [ObservableProperty]
    private bool _hasPackages;

    /// <summary>Called once from <c>App.axaml.cs</c> after <see cref="PluginHostService.Initialize"/> has discovered/precompiled every plugin - the host doesn't exist yet when this ViewModel is constructed in <c>MainViewModel</c>'s own constructor.</summary>
    public void AttachHost(PluginHostService host)
    {
        _host = host;
        Refresh();
    }

    public void Refresh()
    {
        RefreshPackages();

        Groups.Clear();
        if (_host is null)
        {
            HasPlugins = false;
            return;
        }

        var rows = _host.Engine.AllCommands.Select(c => new PluginCommandRowViewModel(c, _host));
        foreach (var group in rows.GroupBy(r => r.HookGroupLabel).OrderBy(g => g.Key))
        {
            Groups.Add(new PluginGroupViewModel(group.Key, group.ToList()));
        }

        HasPlugins = Groups.Count > 0;
    }

    private void RefreshPackages()
    {
        Packages.Clear();
        foreach (PackageManager.Package package in _packageService.GetPackages())
        {
            Packages.Add(new PluginPackageRowViewModel(package, () => RemovePackage(package)));
        }

        HasPackages = Packages.Count > 0;
    }

    /// <summary>
    /// Opens a file picker for a plugin package zip (CE's own "Script Archive|*.zip" format - see
    /// <see cref="PluginPackageService"/>), confirms an overwrite if a same-named package is already
    /// installed (matching CE's own prompt), installs, and re-discovers immediately - no restart.
    /// </summary>
    [RelayCommand]
    private async Task InstallPackage()
    {
        string? file = await _filePicker.PickOpenFileAsync("Install Plugin Package", "zip", "Plugin Package (.zip)");
        if (file is null)
        {
            return;
        }

        if (_packageService.PackageFileExists(file))
        {
            int answer = PluginQuestionDialog.ShowModal(
                "A plugin package with this name is already installed. Overwrite it?", "Overwrite", "Cancel");
            if (answer != 0)
            {
                return;
            }
        }

        if (!_packageService.Install(file))
        {
            _host?.ShowToast("Plugin package", "That file isn't a readable plugin package (.zip).");
            return;
        }

        _host?.RediscoverPlugins();
        Refresh();
        _host?.ShowToast("Plugin package", "Installed - no restart needed.");
    }

    private void RemovePackage(PackageManager.Package package)
    {
        _packageService.Uninstall(package);
        _host?.RediscoverPlugins();
        Refresh();
    }
}
