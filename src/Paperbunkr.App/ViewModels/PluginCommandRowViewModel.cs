using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using cYo.Common.Runtime;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Plugins;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.App.ViewModels;

/// <summary>One <see cref="Command"/> row on the Plugin screen (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §6) - Name/Package/compile-error/Configure display plus the enable toggle, which writes through <see cref="PluginHostService.SetCommandEnabled"/> on change.</summary>
public partial class PluginCommandRowViewModel : ViewModelBase
{
    private readonly Command _command;
    private readonly PluginHostService _host;

    public PluginCommandRowViewModel(Command command, PluginHostService host)
    {
        _command = command;
        _host = host;
        _isEnabled = command.Enabled;
    }

    public string Name => _command.Name;

    public string? Description => _command.Description;

    public bool HasDescription => !string.IsNullOrEmpty(_command.Description);

    public string Hook => _command.Hook;

    public string HookGroupLabel => PluginHooks.ValidHooks.TryGetValue(_command.Hook, out var label) && !string.IsNullOrEmpty(label) ? label : _command.Hook;

    /// <summary>
    /// Live-read from a <c>package.ini</c> file in this command's own plugin folder, matching
    /// ComicRackCE's exact mechanism (<c>_reference/ComicRackCE/ComicRack/Dialogs/PreferencesDialog.cs</c>
    /// <c>FillScriptsList</c>: <c>IniFile.GetValue(Path.Combine(command.Environment.CommandPath,
    /// "package.ini"), "Name", "Other")</c>) rather than anything stored on the manifest at discovery
    /// time - installing/removing a package.ini next to a plugin takes effect on the next screen
    /// refresh with no re-discovery needed. "Other" is CE's own fallback, not a Paperbunkr default.
    /// </summary>
    public string Package => _command.Environment is null
        ? "Other"
        : IniFile.GetValue(Path.Combine(_command.Environment.CommandPath, "package.ini"), "Name", "Other");

    public bool IsBroken => _command.IsBroken;

    public string? CompileError => _command.CompileError;

    public bool HasConfigure => _command.Configure is not null;

    /// <summary>
    /// Manual "Run" trigger (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §5/§7) -
    /// scoped to <see cref="Paperbunkr.Plugins.Hooks.PluginHooks.CreateBookList"/> only, since that's
    /// the one hook whose globals need no payload beyond <see cref="IPluginEnvironment"/>, making a
    /// generic "run it right now" button meaningful without any live selection/context to supply.
    /// Other hooks (Library, BookOpened, etc.) need real context this screen doesn't have, so they
    /// stay reachable only from their real trigger site.
    /// </summary>
    public bool CanRunManually => !IsBroken && _command.Hook == PluginHooks.CreateBookList;

    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value) => _host.SetCommandEnabled(_command, value);

    [RelayCommand]
    private async Task Run()
    {
        if (_command.Environment is null)
        {
            return;
        }

        var globals = new CreateBookListHookGlobals { Environment = _command.Environment };
        PluginInvocationResult result = await _host.RunCommandAsync(_command, globals);

        if (!result.Success)
        {
            _host.ShowToast("Plugin error", $"\"{Name}\" failed: {result.Error?.Message}");
            return;
        }

        var issues = (result.ReturnValue as IEnumerable<Issue>)?.ToList() ?? new List<Issue>();
        string summary = issues.Count == 0
            ? "No matches."
            : string.Join(", ", issues.Take(5).Select(i => $"Series #{i.SeriesId} #{i.Number}")) + (issues.Count > 5 ? $" (+{issues.Count - 5} more)" : string.Empty);
        _host.ShowToast(Name, $"Found {issues.Count} issue(s). {summary}");
    }

    /// <summary>
    /// ConfigScript gear icon (docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-hooks-
    /// plan.md §9) - <see cref="Command.Configure"/> pairing and discovery-time wiring already
    /// existed (<see cref="PluginEngine.Discover"/>); only this click-to-invoke action was missing.
    /// No generic plugin-dialog surface exists yet, so this just invokes the paired command - a
    /// config script drives whatever native primitives it has (<c>IApplication.AskQuestion</c>,
    /// its own <c>IPluginConfig.SetSetting</c> calls) the same way any other command does, rather
    /// than this row opening a dialog on the command's behalf.
    /// </summary>
    [RelayCommand]
    private async Task OpenConfigure()
    {
        if (_command.Configure is not { Environment: not null } configure)
        {
            return;
        }

        var result = await _host.RunCommandAsync(configure, new ConfigScriptHookGlobals { Environment = configure.Environment });
        if (!result.Success)
        {
            _host.ShowToast("Plugin error", $"\"{Name}\" configuration failed: {result.Error?.Message}");
        }
    }
}
