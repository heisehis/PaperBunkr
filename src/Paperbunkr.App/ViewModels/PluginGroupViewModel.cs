using System.Collections.Generic;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One hook/extension-point's command rows (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md
/// §6), matching ComicRackCE's own Preferences → Scripts tab grouping
/// (<c>_reference/ComicRackCE/ComicRack/Dialogs/PreferencesDialog.cs</c> <c>FillScriptsList</c>,
/// which groups its <c>lvScripts</c> ListView by <c>PluginEngine.GetHookDescription(command.Hook)</c>
/// rather than by which installed plugin package a command came from) - "what can happen at this
/// extension point, across every installed plugin" instead of "what does this one plugin do".
/// </summary>
public sealed class PluginGroupViewModel
{
    public PluginGroupViewModel(string header, IReadOnlyList<PluginCommandRowViewModel> commands)
    {
        Header = header;
        Commands = commands;
    }

    public string Header { get; }

    public IReadOnlyList<PluginCommandRowViewModel> Commands { get; }
}
