using System.Collections.Generic;

namespace Paperbunkr.App.ViewModels;

/// <summary>One installed plugin's command rows, grouped by <c>Command.PluginKey</c> (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §6).</summary>
public sealed class PluginGroupViewModel
{
    public PluginGroupViewModel(string pluginKey, IReadOnlyList<PluginCommandRowViewModel> commands)
    {
        PluginKey = pluginKey;
        Commands = commands;
    }

    public string PluginKey { get; }

    public IReadOnlyList<PluginCommandRowViewModel> Commands { get; }
}
