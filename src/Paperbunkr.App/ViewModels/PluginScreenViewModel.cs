using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.Plugins;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Plugin screen (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §6) - lists every
/// discovered plugin's commands grouped by plugin, with an enable/disable toggle and a
/// compile-error indicator per command. Previously a permanent empty state (no plugin engine
/// existed at all - docs/superpowers/specs/2026-08-09-plugin-screen-cleanup-design.md); the empty
/// state is kept for the genuine zero-plugins case, just no longer the only state.
/// </summary>
public partial class PluginScreenViewModel : ViewModelBase
{
    private PluginHostService? _host;

    public ObservableCollection<PluginGroupViewModel> Groups { get; } = new();

    [ObservableProperty]
    private bool _hasPlugins;

    /// <summary>Called once from <c>App.axaml.cs</c> after <see cref="PluginHostService.Initialize"/> has discovered/precompiled every plugin - the host doesn't exist yet when this ViewModel is constructed in <c>MainViewModel</c>'s own constructor.</summary>
    public void AttachHost(PluginHostService host)
    {
        _host = host;
        Refresh();
    }

    public void Refresh()
    {
        Groups.Clear();
        if (_host is null)
        {
            HasPlugins = false;
            return;
        }

        foreach (var group in _host.Engine.AllCommands.GroupBy(c => c.PluginKey).OrderBy(g => g.Key))
        {
            var rows = group.Select(c => new PluginCommandRowViewModel(c, _host)).ToList();
            Groups.Add(new PluginGroupViewModel(group.Key, rows));
        }

        HasPlugins = Groups.Count > 0;
    }
}
