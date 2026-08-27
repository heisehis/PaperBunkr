using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Plugins;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.App.ViewModels;

/// <summary>One <see cref="Command"/> row on the Plugin screen (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §6) - Name/Hook/compile-error/Configure display plus the enable toggle, which writes through <see cref="PluginHostService.SetCommandEnabled"/> on change.</summary>
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

    public string HookGroupLabel => PluginHooks.ValidHooks.TryGetValue(_command.Hook, out var label) ? label : _command.Hook;

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
}
