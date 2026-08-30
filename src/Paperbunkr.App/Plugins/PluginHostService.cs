using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// App-lifetime owner of the <see cref="PluginEngine"/> (docs/superpowers/specs/
/// 2026-08-24-plugin-api-v2-design.md §2/§3/§8): builds the real <see cref="IPluginEnvironment"/>
/// adapters once, discovers/precompiles plugins under <see cref="PluginPaths.RootDirectory"/>,
/// applies persisted enable/disable overrides, and fires the <see cref="PluginHooks.Startup"/>/
/// <see cref="PluginHooks.Shutdown"/> lifecycle hooks.
/// </summary>
public sealed class PluginHostService
{
    public PluginEngine Engine { get; } = new();

    private MainViewModel? _main;

    public void Initialize(MainViewModel main, Window mainWindow)
    {
        _main = main;

        var environment = new PaperbunkrPluginEnvironment
        {
            MainWindow = new PaperbunkrPluginHostWindow(mainWindow),
            App = new PaperbunkrApplication(main),
            OpenBooks = new PaperbunkrOpenBooksManager(main),
            Browser = new PaperbunkrBrowser(main),
            ComicDisplay = new PaperbunkrComicDisplay(main.Reader),
            Metadata = new PaperbunkrMetadataGraph(),
            Rules = new PaperbunkrRulesEngine(),
            Writer = new PaperbunkrMetadataWriter(),
            ThemePlugin = new PaperbunkrThemePlugin(),
        };

        try
        {
            Engine.Discover(PluginPaths.RootDirectory, environment);
            ApplyPersistedOverrides();
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"Plugin discovery failed: {ex.Message}");
        }

        InvokeAndReport(PluginHooks.Startup, env => new StartupHookGlobals { Environment = env });
    }

    public void Shutdown()
    {
        InvokeAndReport(PluginHooks.Shutdown, env => new ShutdownHookGlobals { Environment = env });
    }

    /// <summary>
    /// Real Library-hook anchor (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §5) -
    /// wired to a Library grid right-click. Paperbunkr's Library screen has no multi-selection
    /// model (unlike Detail's Bulk Edit), so this always runs against whichever single issue was
    /// right-clicked; <paramref name="books"/> is still a list (matching CE's own "selected books"
    /// shape and <see cref="Hooks.BooksHookGlobals"/>) so a future multi-select Library UI can pass
    /// more than one without any plugin-facing API change. Invokes every enabled command registered
    /// under the Library hook, not just one plugin's.
    /// </summary>
    public Task<IReadOnlyList<PluginInvocationResult>> RunLibraryHookAsync(IEnumerable<Issue> books)
    {
        var list = books.ToList();
        return InvokeAndReportAsync(PluginHooks.Library, env => new BooksHookGlobals { Environment = env, Books = list });
    }

    /// <summary>Runs one specific command directly (bypassing hook-wide dispatch) - backs the Plugin screen's manual "Run" action for hooks like CreateBookList that need no external payload beyond <see cref="IPluginEnvironment"/>.</summary>
    public async Task<PluginInvocationResult> RunCommandAsync<TGlobals>(Command command, TGlobals globals)
        where TGlobals : PluginGlobals
    {
        try
        {
            object? value = await command.InvokeAsync(globals).ConfigureAwait(false);
            return new PluginInvocationResult(command, true, value, null);
        }
        catch (Exception ex)
        {
            return new PluginInvocationResult(command, false, null, ex);
        }
    }

    public void ShowToast(string title, string message) => _main?.ShowToastForPlugin(title, message);

    /// <summary>Persists a user toggle and applies it immediately (docs §3's <see cref="PluginCommandState"/> sparse-table convention) - called from the Plugin screen.</summary>
    public void SetCommandEnabled(Command command, bool enabled)
    {
        command.Enabled = enabled;

        using var context = PaperbunkrDb.CreateContext();
        var row = context.PluginCommandStates.FirstOrDefault(s => s.PluginKey == command.PluginKey && s.CommandKey == command.Key);
        if (row is null)
        {
            context.PluginCommandStates.Add(new PluginCommandState
            {
                PluginKey = command.PluginKey,
                CommandKey = command.Key,
                Enabled = enabled,
            });
        }
        else
        {
            row.Enabled = enabled;
        }

        context.SaveChanges();
    }

    private void ApplyPersistedOverrides()
    {
        using var context = PaperbunkrDb.CreateContext();
        var overrides = context.PluginCommandStates.ToList();
        foreach (var cmd in Engine.AllCommands)
        {
            var match = overrides.FirstOrDefault(o => o.PluginKey == cmd.PluginKey && o.CommandKey == cmd.Key);
            if (match is not null)
            {
                cmd.Enabled = match.Enabled;
            }
        }
    }

    /// <summary>Blocking wrapper for lifecycle points (App startup/shutdown) that are themselves synchronous - never called from a UI event handler.</summary>
    private void InvokeAndReport<TGlobals>(string hook, Func<IPluginEnvironment, TGlobals> globalsFactory)
        where TGlobals : PluginGlobals
    {
        InvokeAndReportAsync(hook, globalsFactory).GetAwaiter().GetResult();
    }

    private async Task<IReadOnlyList<PluginInvocationResult>> InvokeAndReportAsync<TGlobals>(string hook, Func<IPluginEnvironment, TGlobals> globalsFactory)
        where TGlobals : PluginGlobals
    {
        try
        {
            var results = await Engine.InvokeAsync(hook, globalsFactory).ConfigureAwait(false);
            foreach (var failure in results.Where(r => !r.Success))
            {
                DiagnosticsService.LogMilestone($"Plugin command '{failure.Command.Name}' ({hook}) failed: {failure.Error?.Message}");
                _main?.ShowToastForPlugin("Plugin error", $"\"{failure.Command.Name}\" failed: {failure.Error?.Message}");
            }

            return results;
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"Plugin hook '{hook}' invocation failed: {ex.Message}");
            return Array.Empty<PluginInvocationResult>();
        }
    }
}
