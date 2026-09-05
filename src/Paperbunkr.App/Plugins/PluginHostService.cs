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
    private IPluginEnvironment? _environment;

    public void Initialize(MainViewModel main, Window mainWindow)
    {
        _main = main;

        _environment = new PaperbunkrPluginEnvironment
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
    /// specs/2026-09-05-plugin-api-v2-remaining-hooks-plan.md §3). Unlike <see cref="RunLibraryHookAsync"/>'s
    /// single hardcoded menu item (fine when only one plugin exists), this surfaces one entry per
    /// enabled command - <see cref="GetEditorCommands"/> backs that enumeration.
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
