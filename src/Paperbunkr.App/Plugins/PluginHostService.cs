using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Abstractions.Ui;
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
    private IPluginEnvironment? _environment;

    public void Initialize(MainViewModel main, Window mainWindow)
    {
        _main = main;

        // Applies any Native-tier install/uninstall that stayed pending from a previous session
        // (docs/superpowers/specs/2026-09-11-plugin-api-v4-native-tier-design.md §4) - must run
        // before Discover below picks up the plugins directory. A no-op the common case (nothing
        // pending, or only Script-tier packages, which already self-commit at install time).
        new PluginPackageService().ApplyPendingChanges();

        var baseEnvironment = new PaperbunkrPluginEnvironment
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

        // Native-capable (docs/superpowers/specs/2026-09-11-plugin-api-v4-native-tier-design.md §3) -
        // there's only one real environment instance; a headless native plugin just never casts to
        // the wider INativePluginUiEnvironment, and a .csx script only ever sees it through the base
        // IPluginEnvironment interface, so nothing about the existing script sandbox changes.
        _environment = new PaperbunkrNativePluginEnvironment(baseEnvironment, main.Activity, main.NativePluginModalHost);

        DiscoverAndApplyOverrides();

        InvokeAndReport(PluginHooks.Startup, env => new StartupHookGlobals { Environment = env });
    }

    /// <summary>
    /// Re-discovers everything under <see cref="PluginPaths.RootDirectory"/> - called by the Plugin
    /// screen after <see cref="PluginPackageService"/> installs/uninstalls a package. No restart is
    /// needed (see <see cref="PluginPackageService"/>'s doc comment): this reuses the same
    /// long-lived environment built in <see cref="Initialize"/> rather than constructing a new one,
    /// so per-plugin settings/state on that environment (none currently, but the shape allows for
    /// it) survive a live reload. A no-op before <see cref="Initialize"/> has run.
    /// </summary>
    public void RediscoverPlugins() => DiscoverAndApplyOverrides();

    /// <summary>Test seam - sets the environment used by <see cref="RediscoverPlugins"/>/discovery without going through the full <see cref="Initialize"/> path (no real <c>MainViewModel</c>/<c>Window</c> needed) and runs an initial discovery immediately.</summary>
    internal void InitializeForTests(IPluginEnvironment environment)
    {
        _environment = environment;
        DiscoverAndApplyOverrides();
    }

    private void DiscoverAndApplyOverrides()
    {
        if (_environment is null)
        {
            return;
        }

        try
        {
            Engine.Discover(PluginPaths.RootDirectory, _environment);
            ApplyPersistedOverrides();
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"Plugin discovery failed: {ex.Message}");
        }
    }

    public void Shutdown()
    {
        InvokeAndReport(PluginHooks.Shutdown, env => new ShutdownHookGlobals { Environment = env });
    }

    /// <summary>
    /// Real Library-hook anchor (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §5) -
    /// backs Library/Detail's own "Plugins ▸" submenu and the Library toolbar's bulk-action
    /// dropdown, one entry per enabled command (mirrors <see cref="GetEditorCommands"/>'s own
    /// already-established shape). Replaces an earlier single hardcoded "Find Duplicates" menu item
    /// that invoked every enabled Library-hook command at once regardless of which one the label
    /// actually named - fine when Duplicate Finder was the only plugin registering this hook, wrong
    /// once Cluster Library Manager's own "Organize Library"/"Scrape with ComicVine" also did.
    /// </summary>
    public IEnumerable<Command> GetLibraryCommands() => Engine.GetCommands(PluginHooks.Library);

    public Task<PluginInvocationResult> RunLibraryCommandAsync(Command command, IReadOnlyList<Issue> books) =>
        RunCommandAsync(command, new BooksHookGlobals { Environment = _environment!, Books = books });

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

    /// <summary>Real BookOpened-hook anchor (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md
    /// §5) - <see cref="ReaderScreenViewModel.IssueOpened"/> fires this once an issue finishes
    /// loading in the reader. Was a documented anchor with no actual subscriber until
    /// docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-hooks-plan.md's audit caught it.</summary>
    public Task<IReadOnlyList<PluginInvocationResult>> RunBookOpenedHookAsync(Issue book) =>
        InvokeAndReportAsync(PluginHooks.BookOpened, env => new BookOpenedHookGlobals { Environment = env, Book = book });

    /// <summary>Real ReaderResized-hook anchor - the reader screen's size-changed handling.</summary>
    public Task<IReadOnlyList<PluginInvocationResult>> RunReaderResizedHookAsync(int width, int height) =>
        InvokeAndReportAsync(PluginHooks.ReaderResized, env => new ReaderResizedHookGlobals { Environment = env, Width = width, Height = height });

    /// <summary>
    /// Real Editor-hook anchor - the Issue Properties/Bulk Editing overlay toolbar (docs/superpowers/
    /// specs/2026-09-05-plugin-api-v2-remaining-hooks-plan.md §3) - one entry per enabled command,
    /// same shape <see cref="GetLibraryCommands"/> now also uses.
    /// </summary>
    public IEnumerable<Command> GetEditorCommands() => Engine.GetCommands(PluginHooks.Editor);

    public Task<PluginInvocationResult> RunEditorCommandAsync(Command command, IReadOnlyList<Issue> books) =>
        RunCommandAsync(command, new BooksHookGlobals { Environment = _environment!, Books = books });

    /// <summary>Real Books-hook anchor - the Books screen (novels/EPUB/PDF) context menu. See <see cref="NovelBooksHookGlobals"/>'s own doc comment for why this isn't <see cref="BooksHookGlobals"/>.</summary>
    public IEnumerable<Command> GetNovelBooksCommands() => Engine.GetCommands(PluginHooks.Books);

    public Task<PluginInvocationResult> RunNovelBooksCommandAsync(Command command, IReadOnlyList<Book> books) =>
        RunCommandAsync(command, new NovelBooksHookGlobals { Environment = _environment!, Books = books });

    /// <summary>Real NewBooks-hook anchor - Library's "Add issue to library" overlay, one entry per enabled command (mirrors CE's own per-command File-menu items, docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-hooks-plan.md §5) rather than replacing the manual add flow.</summary>
    public IEnumerable<Command> GetNewBooksCommands() => Engine.GetCommands(PluginHooks.NewBooks);

    public async Task<Issue?> RunNewBooksCommandAsync(Command command)
    {
        var result = await RunCommandAsync(command, new NewBooksHookGlobals { Environment = _environment! }).ConfigureAwait(false);
        if (!result.Success)
        {
            DiagnosticsService.LogMilestone($"Plugin command '{command.Name}' (NewBooks) failed: {result.Error?.Message}");
            _main?.ShowToastForPlugin("Plugin error", $"\"{command.Name}\" failed: {result.Error?.Message}");
            return null;
        }

        return result.ReturnValue as Issue;
    }

    /// <summary>
    /// Real ParseComicPath-hook anchor - <see cref="Services.LibraryFolderScanner"/>'s filename
    /// parse step (docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-hooks-plan.md §7).
    /// Runs every enabled command in registration order and returns the first non-null override -
    /// the built-in filename parser's guess is used untouched when none returns one (no commands
    /// implement this hook today, so behavior is unchanged until one does).
    /// </summary>
    public async Task<ParsedComicPath?> RunParseComicPathHookAsync(string path)
    {
        foreach (var command in Engine.GetCommands(PluginHooks.ParseComicPath))
        {
            if (command.Environment is null)
            {
                continue;
            }

            var result = await RunCommandAsync(command, new ParseComicPathHookGlobals { Environment = command.Environment, Path = path }).ConfigureAwait(false);
            if (!result.Success)
            {
                DiagnosticsService.LogMilestone($"Plugin command '{command.Name}' (ParseComicPath) failed: {result.Error?.Message}");
                continue;
            }

            if (result.ReturnValue is ParsedComicPath parsed)
            {
                return parsed;
            }
        }

        return null;
    }

    /// <summary>Real NetSearch-hook anchor - additional providers in Detail's Apply-from-Provider search picker, alongside AniList/MangaBaka (docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-hooks-plan.md §8).</summary>
    public IEnumerable<Command> GetNetSearchCommands() => Engine.GetCommands(PluginHooks.NetSearch);

    public Task<PluginInvocationResult> RunNetSearchCommandAsync(Command command, string query) =>
        RunCommandAsync(command, new NetSearchHookGlobals { Environment = _environment!, Query = query });

    /// <summary>Real ComicInfoHtml/ComicInfoUI-hook anchor - the Detail screen's "Plugins" tab (docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-hooks-plan.md §10).</summary>
    public IEnumerable<Command> GetComicInfoCommands() =>
        Engine.GetCommands(PluginHooks.ComicInfoHtml).Concat(Engine.GetCommands(PluginHooks.ComicInfoUI));

    public Task<PluginInvocationResult> RunComicInfoCommandAsync(Command command, Issue book) =>
        RunCommandAsync(command, new ComicInfoHookGlobals { Environment = _environment!, Book = book });

    public void ShowToast(string title, string message) => _main?.ShowToastForPlugin(title, message);

    /// <summary>
    /// Surfaces a Native-tier install/uninstall's "restart to apply" requirement through the
    /// Activity Center rather than only a toast the user has to remember - an "Restart now" action
    /// link (<see cref="ActivityLinkKind.RestartApp"/>, resolved in <c>MainViewModel.ResolveActivityLink</c>)
    /// relaunches the app immediately when clicked. Deduped by <paramref name="dedupeKey"/> so
    /// installing/removing multiple packages in one session before restarting collapses to one
    /// standing alert instead of stacking a fresh one per action.
    /// </summary>
    public void RaisePendingRestartAlert(string title, string detail, string dedupeKey)
    {
        _main?.Activity.RaiseAlert(new ActivityAlert
        {
            Severity = ActivityAlertSeverity.Info,
            Title = title,
            Detail = detail,
            ActionLabel = "Restart now",
            ActionLink = new ActivityLink(ActivityLinkKind.RestartApp),
            DedupeKey = dedupeKey,
        });
    }

    /// <summary>
    /// Opens a native plugin's compiled settings UI (docs/superpowers/specs/2026-09-12-plugin-
    /// management-screen-redesign-design.md §4.4/§4.5) - the entry point that didn't exist before
    /// this redesign. A no-op if the package never loaded (no <c>NativeLoadResults</c> entry), its
    /// module doesn't implement <see cref="INativePluginSettingsUi"/>, or <c>CreateSettingsView</c>
    /// itself returns null. Hosted through the same shared <c>NativePluginModalHostViewModel</c> +
    /// <c>OverlayShell</c> every other native-plugin dialog already uses - dismissal is scrim/X-only
    /// (no <c>resolve</c> callback is ever invoked from inside the settings view), which throws
    /// <see cref="OperationCanceledException"/> on the awaited task exactly the way any other
    /// scrim-dismissed native dialog already does; that's the expected, only way out, not an error.
    /// </summary>
    public async Task OpenPluginSettingsAsync(string pluginKey)
    {
        if (_environment is not INativePluginUiEnvironment uiEnvironment)
        {
            return;
        }

        if (!Engine.NativeLoadResults.TryGetValue(pluginKey, out var loadResult) || loadResult.Module is not INativePluginSettingsUi settingsUi)
        {
            return;
        }

        Control? view = settingsUi.CreateSettingsView(uiEnvironment);
        if (view is null)
        {
            return;
        }

        try
        {
            await uiEnvironment.ShowModalAsync<object?>(_ => view).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Dismissed via the shell's own scrim/close button - the expected, only way this modal
            // ever ends, not a failure.
        }
    }

    /// <summary>
    /// Comic Detail screen's "Details" tab entry point - asks every loaded native module that
    /// implements <see cref="INativeSeriesDetailUi"/> whether it wants to replace that tab's default
    /// External Metadata/Trackers block for <paramref name="series"/>, returning the first non-null
    /// view (in practice, at most one plugin implements this today). Naturally absent (returns null)
    /// whenever no such plugin is installed, since <see cref="PluginEngine.NativeLoadResults"/> then
    /// has nothing to check in the first place - no separate "is it installed" branch needed.
    /// </summary>
    public Control? GetSeriesDetailExtension(Series series)
    {
        if (_environment is not INativePluginUiEnvironment uiEnvironment)
        {
            return null;
        }

        foreach (var loadResult in Engine.NativeLoadResults.Values)
        {
            if (loadResult.Module is INativeSeriesDetailUi detailUi)
            {
                Control? view = detailUi.CreateSeriesDetailView(uiEnvironment, series);
                if (view is not null)
                {
                    return view;
                }
            }
        }

        return null;
    }

    /// <summary>Master enable/disable for every command a package owns at once (docs §4.4/§4.5) -
    /// a bulk write over the existing per-command <see cref="PluginCommandState"/> mechanism, no new
    /// persisted state of its own.</summary>
    public void SetPackageEnabled(string pluginKey, bool enabled)
    {
        foreach (Command command in Engine.AllCommands.Where(c => c.PluginKey == pluginKey))
        {
            SetCommandEnabled(command, enabled);
        }
    }

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
