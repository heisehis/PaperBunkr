using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;
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

    /// <summary>The app's host once <see cref="Initialize"/> ran - for the few menu builders that are constructed in many places with no host to pass in.</summary>
    public static PluginHostService? Current { get; private set; }

    public void Initialize(MainViewModel main, Window mainWindow)
    {
        _main = main;
        Current = this;

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
            Writer = new PaperbunkrMetadataWriter
            {
                // Plugin API 4.5 write-batch preview (§D2): the shared confirm dialog, from whatever thread the plugin is on.
                ShowDialog = request => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => main.Dialogs.ShowAsync(request)),
            },
            ThemePlugin = new PaperbunkrThemePlugin(),
            ActivityService = main.Activity,
            ResolvePluginName = key => Engine.PackageNames.GetValueOrDefault(key, key),
            SettingsAccess = Settings,
            FieldSchemaFor = key => Engine.FieldSchemas.GetValueOrDefault(key),
            NetworkHostsFor = key => Engine.NetworkHosts.GetValueOrDefault(key) ?? Array.Empty<string>(),
            ProviderRegistry = Providers,
        };

        // Native-capable (docs/superpowers/specs/2026-09-11-plugin-api-v4-native-tier-design.md §3) -
        // there's only one real environment instance; a headless native plugin just never casts to
        // the wider INativePluginUiEnvironment, and a .csx script only ever sees it through the base
        // IPluginEnvironment interface, so nothing about the existing script sandbox changes.
        _environment = new PaperbunkrNativePluginEnvironment(baseEnvironment, main.Activity, main.NativePluginModalHost);

        PluginReadingListSource.Install(Providers);
        DiscoverAndApplyOverrides();

        ActivityForAlerts = main.Activity;
        AttachDomainEvents(main.ReadingEvents, LibraryEvents.Default);

        InvokeAndReport(PluginHooks.Startup, env => new StartupHookGlobals { Environment = env });
        DeveloperMode = PluginDeveloperSettings.LoadDeveloperMode();
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

    // ---- Developer mode (docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §A1) ----

    private PluginFolderWatcher? _watcher;
    private bool _developerMode;

    /// <summary>Quiet period before a burst of file changes becomes one reload. Tests shorten it.</summary>
    internal TimeSpan ReloadQuietPeriod { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How a watcher callback reaches the UI thread. Tests run it inline.</summary>
    internal Action<Action> PostToUi { get; set; } = action => Avalonia.Threading.Dispatcher.UIThread.Post(action);

    /// <summary>Where plugins live for the watcher. Defaults to the real root; tests point it elsewhere.</summary>
    internal Func<string> PluginsRoot { get; set; } = () => PluginPaths.RootDirectory;

    /// <summary>Raised on the UI thread after a dev-mode reload, with the keys of the plugins whose files changed.</summary>
    public event Action<IReadOnlyList<string>>? PluginsReloaded;

    /// <summary>
    /// Watches script plugins and re-discovers them when their files change. Turning it on starts the watcher; off disposes it.
    /// The caller persists the choice (<see cref="PluginDeveloperSettings"/>).
    /// </summary>
    public bool DeveloperMode
    {
        get => _developerMode;
        set
        {
            if (_developerMode == value)
            {
                return;
            }

            _developerMode = value;
            _watcher?.Dispose();
            _watcher = null;
            if (value)
            {
                try
                {
                    _watcher = new PluginFolderWatcher(PluginsRoot(), ReloadQuietPeriod, paths => PostToUi(() => ReloadChanged(paths)));
                }
                catch (Exception ex)
                {
                    DiagnosticsService.LogMilestone($"Plugin developer mode: couldn't watch the plugins folder: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Shows "New plugin…" (§A3) and re-discovers after a plugin was created. Returns the new plugin's key, or null when the
    /// dialog was cancelled or there's no UI environment.
    /// </summary>
    public async Task<string?> OpenNewPluginDialogAsync()
    {
        if (_environment is not INativePluginUiEnvironment uiEnvironment)
        {
            return null;
        }

        PluginScaffoldResult? result;
        try
        {
            result = await uiEnvironment.ShowModalAsync<PluginScaffoldResult?>(resolve => new NewPluginDialogView
            {
                DataContext = new NewPluginDialogViewModel(PluginsRoot(), AppContext.BaseDirectory, resolve),
            });
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        if (result is not { Succeeded: true, Folder: { } folder })
        {
            return null;
        }

        RediscoverPlugins();
        return KeysForPaths(new[] { System.IO.Path.Combine(folder, "plugin.xml") }).FirstOrDefault();
    }

    /// <summary>The plugin keys whose folder contains one of <paramref name="paths"/>, from the current discovery.</summary>
    internal IReadOnlyList<string> KeysForPaths(IEnumerable<string> paths)
    {
        var keys = new List<string>();
        foreach ((string key, string folder) in Engine.PackageFolders)
        {
            string prefix = folder.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
            if (paths.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    /// <summary>
    /// Re-discovers after a dev-mode change and reports a plugin that came back blocked or broken - once per plugin, through the
    /// Activity Center. A clean reload is silent. Native plugins can't be swapped in place, so their folders are only reported.
    /// </summary>
    internal void ReloadChanged(IReadOnlyList<string> paths)
    {
        RediscoverPlugins();
        IReadOnlyList<string> keys = KeysForPaths(paths);
        foreach (string key in keys)
        {
            // A reloaded plugin registers its sources again from its own Startup commands (Plugin API 4.5 §C), as at app start.
            Providers.Remove(key);
            foreach (Command startup in Engine.GetCommands(PluginHooks.Startup).Where(c => c.PluginKey == key && c.Environment is not null).ToList())
            {
                _ = RunCommandAsync(startup, new StartupHookGlobals { Environment = startup.Environment! });
            }

            string? problem = DescribeProblem(key);
            IActivityService? activity = ActivityForAlerts ?? _main?.Activity;
            string name = Engine.PackageNames.GetValueOrDefault(key, key);
            if (problem is null)
            {
                LogFiles.Write(key, "INF", "Reloaded after a file change.");
                continue;
            }

            LogFiles.Write(key, "ERR", $"Reload failed: {problem}");
            activity?.RaiseAlert(new ActivityAlert
            {
                Severity = ActivityAlertSeverity.Warning,
                Title = $"{name} didn't reload",
                Detail = problem,
                DedupeKey = $"plugin:{key}:reload",
            });
        }

        PluginsReloaded?.Invoke(keys);
    }

    /// <summary>Why <paramref name="key"/> isn't usable after discovery, or null when it is.</summary>
    internal string? DescribeProblem(string key)
    {
        if (Engine.PackageApiInfo.GetValueOrDefault(key)?.BlockedReason is { } blocked)
        {
            return blocked;
        }

        if (Engine.NativeLoadResults.GetValueOrDefault(key)?.LoadError is { } loadError)
        {
            return loadError;
        }

        var commands = Engine.AllCommands.Where(c => c.PluginKey == key).ToList();
        Command? broken = commands.FirstOrDefault(c => c.IsBroken);
        return broken is null ? null : $"\"{broken.Name}\": {broken.CompileError}";
    }

    /// <summary>Raised after the set of enabled commands may have changed: a discovery pass, or a command or package switched on or off. The keyboard-shortcut list follows it.</summary>
    public event Action? CommandsChanged;

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
            Engine.Discover(PluginsRoot(), _environment);
            RejectNativePluginsWithTwoSettingsDefinitions();
            ApplyPersistedOverrides();
            PublishPluginSourcesAndFields();
            CommandsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"Plugin discovery failed: {ex.Message}");
        }
    }

    /// <summary>Every source plugins registered through <c>Environment.Providers</c> (Plugin API 4.5 §C). Tests supply their own.</summary>
    public PluginProviderRegistry Providers { get; internal set; } = PluginProviderRegistry.Shared;

    /// <summary>
    /// True when <paramref name="pluginKey"/> is usable: discovered, not blocked, and either has no commands or at least one enabled.
    /// Its fields and sources only count while this holds.
    /// </summary>
    private bool IsLive(string pluginKey)
    {
        if (Engine.PackageApiInfo.GetValueOrDefault(pluginKey)?.BlockedReason is not null)
        {
            return false;
        }

        var commands = Engine.AllCommands.Where(c => c.PluginKey == pluginKey).ToList();
        return commands.Count == 0 || commands.Any(c => c.Enabled);
    }

    /// <summary>
    /// After a discovery or an enable/disable: the Smart List engine learns the live plugins' smart fields (§D1), and registrations
    /// from plugins that are gone or switched off are dropped (§C).
    /// </summary>
    private void PublishPluginSourcesAndFields()
    {
        Providers.LogFiles = LogFiles;
        var live = Engine.PackageNames.Keys.Where(IsLive).ToList();
        Providers.RetainOnly(live);
        Paperbunkr.Data.SmartLists.PluginFieldTypeRegistry.Replace(
            live.Where(Engine.FieldSchemas.ContainsKey)
                .SelectMany(key => Engine.FieldSchemas[key].Fields.Select(f => new Paperbunkr.Data.SmartLists.PluginSmartField(
                    f.StorageName, $"{f.Label} ({Engine.PackageNames.GetValueOrDefault(key, key)})", f.Type))));
    }

    /// <summary>
    /// A native plugin that declares a <c>&lt;Settings&gt;</c> schema <em>and</em> implements its own
    /// <see cref="INativePluginSettingsUi"/> defines its settings twice (docs/superpowers/specs/2026-09-20-
    /// plugin-api-4-1-design.md §6.3) - rejected, with a reason, so there is exactly one source of truth.
    /// This can only be checked here: <c>INativePluginSettingsUi</c> lives in the Avalonia-dependent project
    /// the engine doesn't reference, and the module has to be loaded to ask. (A script plugin with a schema
    /// and a ConfigScript command is caught earlier, in the engine.)
    /// </summary>
    private void RejectNativePluginsWithTwoSettingsDefinitions()
    {
        foreach ((string key, NativePluginLoadResult result) in Engine.NativeLoadResults.ToList())
        {
            if (result.Module is INativePluginSettingsUi && Engine.SettingsSchemas.ContainsKey(key))
            {
                Engine.RejectPackage(key, "the plugin declares a <Settings> schema and also implements its own settings UI - a plugin can have only one settings definition");
            }
        }
    }

    public void Shutdown()
    {
        _watcher?.Dispose();
        _watcher = null;
        DetachDomainEvents();
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
            LogFiles.Write(command.PluginKey, "ERR", $"\"{command.Name}\" ({command.Hook}) failed: {ex.Message}", ex);
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

        // A plugin that declares a <Settings> schema gets the host-rendered overlay (docs/superpowers/specs/
        // 2026-09-20-plugin-api-4-1-design.md §6.4) through this same modal path. A schema and a custom
        // settings UI together are rejected at discovery, so at most one of the two branches applies.
        if (Engine.SettingsSchemas.TryGetValue(pluginKey, out PluginSettingsSchema? schema))
        {
            string pluginName = Engine.PackageNames.GetValueOrDefault(pluginKey, pluginKey);
            var schemaView = new PluginSettingsSchemaView { DataContext = new PluginSettingsSchemaViewModel(pluginKey, pluginName, schema, Settings, new Paperbunkr.App.Services.FilePickerService()) };
            try
            {
                await uiEnvironment.ShowModalAsync<object?>(_ => schemaView).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Dismissed via the shell's own scrim/close button - the only way this modal ends.
            }

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


    /// <summary>True when a loaded native plugin can add a panel to an issue (§E) - lets the Detail screen show its Plugins tab.</summary>
    public bool HasIssueDetailExtensions => LiveModules<INativeIssueDetailUi>().Any();

    /// <summary>Every live native plugin's panel for <paramref name="issue"/> (§E), in plugin-name order. A view that throws is skipped and logged.</summary>
    public IReadOnlyList<PluginView> GetIssueDetailExtensions(Issue issue) =>
        BuildViews<INativeIssueDetailUi>((ui, env) => ui.CreateIssueDetailView(env, issue));

    /// <summary>Every live native plugin's Insights card content (§E).</summary>
    public IReadOnlyList<PluginView> GetInsightsWidgets() =>
        BuildViews<INativeInsightsWidget>((ui, env) => ui.CreateInsightsView(env));

    private IEnumerable<(string Key, T Module)> LiveModules<T>() where T : class =>
        Engine.NativeLoadResults
            .Where(r => r.Value.Module is T && IsLive(r.Key))
            .Select(r => (r.Key, (T)(object)r.Value.Module!));

    private IReadOnlyList<PluginView> BuildViews<T>(Func<T, INativePluginUiEnvironment, Control?> create) where T : class
    {
        if (_environment is not INativePluginUiEnvironment uiEnvironment)
        {
            return Array.Empty<PluginView>();
        }

        var views = new List<PluginView>();
        foreach ((string key, T module) in LiveModules<T>().OrderBy(m => Engine.PackageNames.GetValueOrDefault(m.Key, m.Key)))
        {
            try
            {
                if (create(module, uiEnvironment) is { } view)
                {
                    views.Add(new PluginView(Engine.PackageNames.GetValueOrDefault(key, key), view));
                }
            }
            catch (Exception ex)
            {
                LogFiles.Write(key, "ERR", $"Creating its {typeof(T).Name} view failed: {ex.Message}", ex);
            }
        }

        return views;
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

        PublishPluginSourcesAndFields();
        CommandsChanged?.Invoke();
    }

    /// <summary>Persists a user toggle and applies it immediately (docs §3's <see cref="PluginCommandState"/> sparse-table convention) - called from the Plugin screen.</summary>
    public void SetCommandEnabled(Command command, bool enabled)
    {
        command.Enabled = enabled;

        using var context = ContextFactory();
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
        CommandsChanged?.Invoke();
    }

    private void ApplyPersistedOverrides()
    {
        using var context = ContextFactory();
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
                LogFiles.Write(failure.Command.PluginKey, "ERR", $"\"{failure.Command.Name}\" ({hook}) failed: {failure.Error?.Message}", failure.Error);
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

    private PluginSettingsAccess? _settings;

    /// <summary>
    /// The schema-aware view of the per-plugin settings store (docs/superpowers/specs/2026-09-20-plugin-api-4-1-
    /// design.md §6): what plugins see through <c>GetSetting</c>/<c>SetSetting</c> and what the settings overlay
    /// edits. Resolves each plugin's schema from the engine's current discovery, so a re-discovery is picked up.
    /// </summary>
    public PluginSettingsAccess Settings => _settings ??= new PluginSettingsAccess(
        () => ContextFactory(),
        key => Engine.SettingsSchemas.GetValueOrDefault(key),
        DpapiSecretProtector.Instance);

    // ---- Plugin API 4.1 domain-event hooks (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §5) ----
    // BookRead, LibraryScanCompleted, MissingFileDetected and ReadingListChanged. The services that produce
    // these events stay plugin-unaware: they raise on the recorder / LibraryEvents, and this host subscribes
    // and dispatches through DomainHookDispatcher (fire-and-forget, one queue per command, timeout, report-once).

    private DomainHookDispatcher? _domainHooks;
    private IReadingEventRecorder? _subscribedRecorder;
    private LibraryEvents? _subscribedEvents;

    /// <summary>Where a plugin's own log lives; defaults to the real per-user folder, tests point it at a temp one.</summary>
    internal PluginLogFiles LogFiles { get; set; } = PluginLogFiles.Default;

    /// <summary>Test seam - where <c>BookRead</c> looks up the finished item. Production uses the real per-user database.</summary>
    internal Func<PaperbunkrDbContext> ContextFactory { get; set; } = PaperbunkrDb.CreateContext;

    /// <summary>Where a domain-hook problem alert is raised. Set from <c>MainViewModel.Activity</c> in <see cref="Initialize"/>; tests supply their own.</summary>
    internal IActivityService? ActivityForAlerts { get; set; }

    /// <summary>The dispatcher every domain hook runs through. Created on first use.</summary>
    public DomainHookDispatcher DomainHooks => _domainHooks ??= new DomainHookDispatcher(Engine, ReportDomainHookProblem);

    /// <summary>
    /// Starts listening for the events that drive the four domain hooks. Called once from
    /// <see cref="Initialize"/>; tests call it with their own recorder/events (and optionally a dispatcher
    /// with a short timeout). Replaces any previous subscription rather than doubling up.
    /// </summary>
    internal void AttachDomainEvents(IReadingEventRecorder? recorder, LibraryEvents events, DomainHookDispatcher? dispatcher = null)
    {
        DetachDomainEvents();
        if (dispatcher is not null)
        {
            _domainHooks = dispatcher;
        }

        _subscribedRecorder = recorder;
        _subscribedEvents = events;

        if (recorder is not null)
        {
            recorder.ReadingFinished += OnReadingFinished;
        }

        events.LibraryScanCompleted += OnLibraryScanCompleted;
        events.MissingFileConfirmed += OnMissingFileConfirmed;
        events.ReadingListChanged += OnReadingListChanged;
        events.CollectionCompleted += OnCollectionCompleted;
        events.ManagerBypassed += OnManagerBypassed;
        events.SeriesStatusChanged += OnSeriesStatusChanged;
        events.ScheduledTaskRan += OnScheduledTaskRan;
    }

    internal void DetachDomainEvents()
    {
        if (_subscribedRecorder is not null)
        {
            _subscribedRecorder.ReadingFinished -= OnReadingFinished;
            _subscribedRecorder = null;
        }

        if (_subscribedEvents is not null)
        {
            _subscribedEvents.LibraryScanCompleted -= OnLibraryScanCompleted;
            _subscribedEvents.MissingFileConfirmed -= OnMissingFileConfirmed;
            _subscribedEvents.ReadingListChanged -= OnReadingListChanged;
            _subscribedEvents.CollectionCompleted -= OnCollectionCompleted;
            _subscribedEvents.ManagerBypassed -= OnManagerBypassed;
            _subscribedEvents.SeriesStatusChanged -= OnSeriesStatusChanged;
            _subscribedEvents.ScheduledTaskRan -= OnScheduledTaskRan;
            _subscribedEvents = null;
        }
    }

    /// <summary>A reading-list write skipped <c>ReadingListManager</c> (the context announced it anyway) - logged so it can be found and moved (docs/superpowers/specs/2026-09-20-plugin-api-4-2-followons-design.md section 3).</summary>
    private static void OnManagerBypassed(string message) => DiagnosticsService.LogMilestone(message);

    /// <summary>Cheap guard so a hook nobody registered never pays for a database lookup or globals allocation.</summary>
    private bool HasCommands(string hook) => Engine.GetCommands(hook).Any();

    private void OnReadingFinished(ReadingEvent finished)
    {
        if (!HasCommands(PluginHooks.BookRead))
        {
            return;
        }

        // The item is looked up here, once, rather than per command. Either may legitimately be null:
        // the item can be deleted between the finish and this running, and exactly one of the two
        // applies to any event (comics/manga are Issues, novels are Books).
        Issue? issue = null;
        Book? book = null;
        try
        {
            using var context = ContextFactory();
            if (finished.ItemType == ReadingItemType.Comic)
            {
                issue = context.Issues.Find(finished.ItemId);
            }
            else
            {
                book = context.Books.Find(finished.ItemId);
            }
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"BookRead: couldn't load the finished item: {ex.Message}");
        }

        DomainHooks.Dispatch(PluginHooks.BookRead, env => new BookReadHookGlobals
        {
            Environment = env,
            ItemType = finished.ItemType,
            ItemId = finished.ItemId,
            SeriesId = finished.SeriesId,
            PagesRead = finished.PagesRead,
            FinishedUtc = finished.TimestampUtc,
            Issue = issue,
            Book = book,
        });
    }

    private void OnLibraryScanCompleted(LibraryScanCompletedEvent scan)
    {
        if (!HasCommands(PluginHooks.LibraryScanCompleted))
        {
            return;
        }

        DomainHooks.Dispatch(PluginHooks.LibraryScanCompleted, env => new LibraryScanCompletedHookGlobals
        {
            Environment = env,
            FolderPaths = scan.FolderPaths,
            AddedCount = scan.AddedCount,
            UpdatedCount = scan.UpdatedCount,
            SeriesTouched = scan.SeriesTouched,
            Duration = scan.Duration,
            AddedItemIds = scan.AddedItemIds,
            UpdatedItemIds = scan.UpdatedItemIds,
        });
    }

    private void OnMissingFileConfirmed(MissingFileConfirmedEvent missing)
    {
        if (!HasCommands(PluginHooks.MissingFileDetected))
        {
            return;
        }

        DomainHooks.Dispatch(PluginHooks.MissingFileDetected, env => new MissingFileDetectedHookGlobals
        {
            Environment = env,
            ItemType = missing.ItemType,
            ItemId = missing.ItemId,
            FilePath = missing.FilePath,
            Title = missing.Title,
        });
    }

    /// <summary>Plugin API 4.4 (docs/superpowers/specs/2026-10-06-smart-features-design.md §7.4): a continuity or story event was read to the end.</summary>
    private void OnCollectionCompleted(CollectionCompletedEvent completed)
    {
        if (!HasCommands(PluginHooks.ContinuityCompleted))
        {
            return;
        }

        DomainHooks.Dispatch(PluginHooks.ContinuityCompleted, env => new ContinuityCompletedHookGlobals
        {
            Environment = env,
            Kind = completed.Kind,
            Id = completed.Id,
            Name = completed.Name,
            IssueCount = completed.IssueCount,
            CompletedAtUtc = completed.CompletedAtUtc,
        });
    }

    /// <summary>Plugin API 4.5 (docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §B): raised by the context after the save, one per series.</summary>
    private void OnSeriesStatusChanged(SeriesStatusChangedEvent change)
    {
        if (!HasCommands(PluginHooks.SeriesStatusChanged))
        {
            return;
        }

        DomainHooks.Dispatch(PluginHooks.SeriesStatusChanged, env => new SeriesStatusChangedHookGlobals
        {
            Environment = env,
            SeriesId = change.SeriesId,
            SeriesName = change.SeriesName,
            OldStatus = change.OldStatus.ToString(),
            NewStatus = change.NewStatus.ToString(),
        });
    }

    /// <summary>Plugin API 4.5 (§B): a scheduled task finished a run.</summary>
    private void OnScheduledTaskRan(ScheduledTaskRanEvent run)
    {
        if (!HasCommands(PluginHooks.ScheduledTaskRan))
        {
            return;
        }

        DomainHooks.Dispatch(PluginHooks.ScheduledTaskRan, env => new ScheduledTaskRanHookGlobals
        {
            Environment = env,
            TaskId = run.TaskId,
            TaskName = run.TaskName,
            StartedUtc = run.StartedUtc,
            Duration = run.Duration,
            Outcome = run.Outcome.ToString(),
            Message = run.Message,
        });
    }

    private void OnReadingListChanged(ReadingListChangedEvent change)
    {
        if (!HasCommands(PluginHooks.ReadingListChanged))
        {
            return;
        }

        DomainHooks.Dispatch(PluginHooks.ReadingListChanged, env => new ReadingListChangedHookGlobals
        {
            Environment = env,
            ListId = change.ListId,
            ListName = change.ListName,
            Kind = change.Kind,
            AddedIssueIds = change.AddedIssueIds,
            RemovedIssueIds = change.RemovedIssueIds,
        });
    }

    /// <summary>
    /// A domain-hook problem worth telling the user about - at most once per command per kind per
    /// session (the dispatcher enforces that). Goes to the Activity Center as an alert attributed to the
    /// plugin, deduped under the same plugin-scoped key namespace <c>IPluginActivity</c> uses, and to the
    /// diagnostics log. Never throws.
    /// </summary>
    private void ReportDomainHookProblem(DomainHookProblem problem)
    {
        DiagnosticsService.LogMilestone($"Plugin domain hook: {problem.Message}");

        // The plugin's own log is the one place its author looks (docs/superpowers/specs/2026-09-20-
        // plugin-api-4-2-followons-design.md §1), so the host writes what went wrong with it there too.
        LogFiles.Write(problem.Command.PluginKey, problem.Kind == DomainHookProblemKind.Failed ? "ERR" : "WRN", problem.Message);

        IActivityService? activity = ActivityForAlerts ?? _main?.Activity;
        if (activity is null)
        {
            return;
        }

        string pluginName = Engine.PackageNames.GetValueOrDefault(problem.Command.PluginKey, problem.Command.PluginKey);
        (string title, ActivityAlertSeverity severity) = problem.Kind switch
        {
            DomainHookProblemKind.Failed => ($"{pluginName} failed", ActivityAlertSeverity.Error),
            DomainHookProblemKind.TimedOut => ($"{pluginName} isn't responding", ActivityAlertSeverity.Warning),
            _ => ($"{pluginName} can't keep up", ActivityAlertSeverity.Warning),
        };

        activity.RaiseAlert(new ActivityAlert
        {
            Severity = severity,
            Title = title,
            Detail = problem.Message,
            DedupeKey = $"plugin:{problem.Command.PluginKey}:hook:{problem.Command.Key}:{problem.Kind}",
        });
    }
}
