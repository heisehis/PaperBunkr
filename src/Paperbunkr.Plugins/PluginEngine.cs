using Paperbunkr.Plugins.Abstractions.Native;
using Paperbunkr.Plugins.Hooks;
using Paperbunkr.Plugins.Native;

namespace Paperbunkr.Plugins;

/// <summary>One command's outcome from a <see cref="PluginEngine.InvokeAsync{TGlobals}"/> call - lets callers decide how to surface a failure (toast, log) without <c>Paperbunkr.Plugins</c> depending on any App-layer service.</summary>
public sealed record PluginInvocationResult(Command Command, bool Success, object? ReturnValue, Exception? Error);

/// <summary>
/// One native-tier package's load outcome (docs/superpowers/specs/2026-09-12-plugin-management-
/// screen-redesign-design.md §4.2) - <see cref="Module"/> is the loaded <see cref="INativePluginModule"/>
/// instance on success (needed so the App layer can later check it for <c>INativePluginSettingsUi</c>),
/// or <see langword="null"/> with <see cref="LoadError"/> set on any failure, including a second
/// installed folder declaring a manifest key that's already in use.
/// </summary>
public sealed record NativePluginLoadResult(INativePluginModule? Module, string? LoadError);

/// <summary>
/// One plugin package's <c>requiresApi</c> outcome (docs/superpowers/specs/2026-09-20-plugin-api-4-1-
/// design.md §3), recorded for both tiers so the plugin manager can show the declared requirement and
/// - for a script package, which registers no commands when blocked - the reason it didn't load.
/// </summary>
/// <param name="RequiresApi">The manifest's raw <c>requiresApi</c> text, or null when absent.</param>
/// <param name="Declared">The parsed declaration, or null when absent or malformed.</param>
/// <param name="BlockedReason">Non-null only when the plugin was blocked at load (major mismatch or a malformed value).</param>
public sealed record PluginApiInfo(string? RequiresApi, Version? Declared, string? BlockedReason);

/// <summary>
/// Wraps a command's invoke-time failure with a version hint (docs/superpowers/specs/2026-09-20-
/// plugin-api-4-1-design.md §3.2). Deliberately a wrapper rather than a new field on
/// <see cref="PluginInvocationResult"/>: every caller already formats <c>result.Error?.Message</c>, so
/// the hint reaches all of them without editing any call site.
/// </summary>
public sealed class PluginApiMismatchException : Exception
{
    public PluginApiMismatchException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>
/// Ported from ComicRackCE's <c>PluginEngine</c> (docs/superpowers/specs/
/// 2026-08-24-plugin-api-v2-design.md §2/§3): discovers plugin manifests under a root folder,
/// initializes + eagerly precompiles their commands, and dispatches hook invocations to every
/// enabled command registered under that hook.
/// </summary>
public sealed class PluginEngine
{
    private readonly CommandCollection _commands = new();
    private readonly Dictionary<string, NativePluginLoadResult> _nativeLoadResults = new();

    public IReadOnlyList<Command> AllCommands => _commands;

    /// <summary>
    /// Per-native-package load outcome, keyed by <c>plugin.xml</c>'s root <c>key</c> attribute -
    /// the same key <see cref="Command.PluginKey"/> uses (docs/superpowers/specs/2026-09-12-plugin-
    /// management-screen-redesign-design.md §4.2). Previously <see cref="DiscoverNative"/> discarded
    /// both the loaded <see cref="INativePluginModule"/> instance and any load exception - there was
    /// nowhere to call <c>CreateSettingsView</c> on even if a UI existed to call it from, and a
    /// native plugin that threw while loading vanished with zero record anywhere. Script-tier
    /// packages have no entry here (native-only concept).
    /// </summary>
    public IReadOnlyDictionary<string, NativePluginLoadResult> NativeLoadResults => _nativeLoadResults;

    private readonly Dictionary<string, PluginApiInfo> _packageApi = new();

    /// <summary>
    /// Per-package <c>requiresApi</c> outcome for every manifest that parsed, either tier, keyed by the
    /// same plugin key <see cref="Command.PluginKey"/> uses. A script package blocked at load has an
    /// entry here with a <see cref="PluginApiInfo.BlockedReason"/> and no commands anywhere; a native
    /// one also gets a <see cref="NativeLoadResults"/> entry carrying the same reason.
    /// </summary>
    public IReadOnlyDictionary<string, PluginApiInfo> PackageApiInfo => _packageApi;

    private readonly Dictionary<string, string> _packageNames = new();
    private readonly Dictionary<string, PluginSettingsSchema> _settingsSchemas = new();

    /// <summary>
    /// Plugin key → its validated <c>&lt;Settings&gt;</c> schema, for every plugin that declared a valid one
    /// and wasn't blocked. The host reads it to schema-check <c>GetSetting</c>/<c>SetSetting</c> and to render
    /// the settings overlay (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §6).
    /// </summary>
    public IReadOnlyDictionary<string, PluginSettingsSchema> SettingsSchemas => _settingsSchemas;

    /// <summary>
    /// Rejects an already-discovered package after the fact, with a reason shown like any other load
    /// failure: its commands are removed, its native module (if any) is dropped from
    /// <see cref="NativeLoadResults"/> in favour of the error, and its schema is discarded. Used by the App
    /// layer for a rule this project can't check itself - a native plugin that both declares a settings
    /// schema and implements its own settings UI (<c>INativePluginSettingsUi</c> lives in the
    /// Avalonia-dependent project this one doesn't reference). A no-op for an unknown key.
    /// </summary>
    public void RejectPackage(string pluginKey, string reason)
    {
        _commands.RemoveAll(c => c.PluginKey == pluginKey);
        _settingsSchemas.Remove(pluginKey);
        if (_nativeLoadResults.TryGetValue(pluginKey, out NativePluginLoadResult? existing))
        {
            (existing.Module as IDisposable)?.Dispose();
            _nativeLoadResults[pluginKey] = new NativePluginLoadResult(null, reason);
        }

        if (_packageApi.TryGetValue(pluginKey, out PluginApiInfo? info))
        {
            _packageApi[pluginKey] = info with { BlockedReason = reason };
        }
    }

    /// <summary>
    /// Plugin key → the manifest's display <c>name</c>, for every manifest that parsed (either tier).
    /// Used to attribute Activity Center jobs and alerts to a plugin by name rather than by key
    /// (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §4.3).
    /// </summary>
    public IReadOnlyDictionary<string, string> PackageNames => _packageNames;

    /// <summary>
    /// Plugins that have been absorbed into the app, and what to tell the user. Cluster Library Manager (ComicVine scraping and library organizing) became built-in
    /// (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md section 9); an installed copy is left on disk for the user to remove but is not run.
    /// </summary>
    public static IReadOnlyDictionary<string, string> RetiredPluginKeys { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["cluster-library-manager"] = "Now built in: ComicVine scraping and library organizing are part of Paperbunkr itself " +
            "(right-click comics → Scrape with ComicVine / Organize; settings under Preferences → Organize & Scrape). This plugin is no longer needed and is not loaded; you can remove it.",
    };

    /// <summary>Walks <paramref name="pluginsRoot"/> for <c>plugin.xml</c> manifests, initializing and precompiling every command found. Never throws - a broken plugin is flagged via <see cref="Command.IsBroken"/>, not skipped from discovery, and never aborts loading the rest (docs §2).</summary>
    public void Discover(string pluginsRoot, IPluginEnvironment baseEnvironment)
    {
        // A native module holding its own file-backed resources (a local database, a timer) never
        // gets a second chance to release them once this method drops its own reference - the
        // non-collectible AssemblyLoadContext (design v4 §4) means the module instance itself is
        // never reclaimed either, so a leaked LiteDB connection from a previous Discover() call (an
        // install/uninstall/Reload cycle - see PluginHostService.RediscoverPlugins) sits open for
        // the rest of the process, and a same-process second LiteDatabase.Open on that same file
        // throws "used by another process" the next time anything (this Discover() re-loading the
        // module, or an uninstall trying to delete the folder) touches it.
        foreach (NativePluginLoadResult previous in _nativeLoadResults.Values)
        {
            try
            {
                (previous.Module as IDisposable)?.Dispose();
            }
            catch (Exception)
            {
                // Never throws (this method's own contract) - a module that fails to dispose
                // cleanly shouldn't block discovering everything else.
            }
        }

        _commands.Clear();
        _nativeLoadResults.Clear();
        _packageApi.Clear();
        _packageNames.Clear();
        _settingsSchemas.Clear();
        if (!Directory.Exists(pluginsRoot))
        {
            return;
        }

        var configCommands = new List<Command>();
        foreach (string manifestFile in Directory.EnumerateFiles(pluginsRoot, "plugin.xml", SearchOption.AllDirectories))
        {
            string pluginDir = Path.GetDirectoryName(manifestFile) ?? pluginsRoot;

            // Native-tier plugins (docs/superpowers/specs/2026-09-11-plugin-api-v4-native-tier-
            // design.md §3/§4) declare no <Command> elements at all - their commands come from
            // RegisterCommands, not manifest-declared scripts - so they take a separate path
            // entirely rather than going through XmlPluginInitializer.GetCommands below.
            PluginManifest? manifest = XmlPluginInitializer.ReadManifest(manifestFile);

            // requiresApi gate (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §3.2). Runs
            // BEFORE anything is compiled or loaded: a major mismatch in either direction (or a
            // malformed value) must mean the plugin's code is never touched. A same-major minor
            // difference is deliberately lenient and only ever explains a failure later.
            PluginApiCompatibility compat = PluginApiCompatibility.Evaluate(manifest?.RequiresApi, PluginApi.Current);

            // The declared settings schema (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md
            // §6) is validated at the same gate, for the same reason: a malformed manifest entry - or a
            // plugin that defines its settings twice (a <Settings> schema AND a ConfigScript command) -
            // means the plugin's code is never touched, and the reason is shown like any other block.
            string? blockedReason = compat.Reason;
            PluginSettingsSchema? schema = null;
            if (manifest is not null && blockedReason is null)
            {
                schema = PluginSettingsSchema.Parse(manifest, out string? schemaError);
                if (schemaError is null && schema is not null
                    && manifest.Commands.Any(c => string.Equals(c.Hook, PluginHooks.ConfigScript, StringComparison.OrdinalIgnoreCase)))
                {
                    schemaError = "the plugin declares both <Settings> and a ConfigScript command - a plugin can have only one settings definition";
                }

                if (schemaError is not null)
                {
                    blockedReason = $"invalid <Settings>: {schemaError}";
                    schema = null;
                }
            }

            if (manifest is not null)
            {
                string apiKey = string.IsNullOrWhiteSpace(manifest.Key) ? Path.GetFileName(pluginDir) : manifest.Key;
                _packageApi[apiKey] = new PluginApiInfo(manifest.RequiresApi, compat.Declared, blockedReason);
                _packageNames[apiKey] = string.IsNullOrWhiteSpace(manifest.Name) ? apiKey : manifest.Name;
                if (schema is not null)
                {
                    _settingsSchemas[apiKey] = schema;
                }
            }

            if (manifest is not null && string.Equals(manifest.Tier, "Native", StringComparison.OrdinalIgnoreCase))
            {
                DiscoverNative(manifest, pluginDir, baseEnvironment, compat, blockedReason);
                continue;
            }

            if (blockedReason is not null)
            {
                continue;
            }

            foreach (Command cmd in XmlPluginInitializer.GetCommands(manifestFile))
            {
                cmd.DeclaredApi = compat.Declared;
                if (!cmd.Initialize(baseEnvironment, pluginDir))
                {
                    continue;
                }

                cmd.PreCompile();

                // Compile failures only get the hint for a declared-higher minor; a plain syntax
                // error in a plugin that declares nothing isn't drift (see FailureHint).
                if (cmd.IsBroken)
                {
                    cmd.AppendVersionHint(PluginApiCompatibility.FailureHint(cmd.DeclaredApi, PluginApi.Current, driftShapedFailure: false));
                }

                if (cmd.Hook == PluginHooks.ConfigScript)
                {
                    configCommands.Add(cmd);
                    continue;
                }

                if (_commands.Any(c => c.Key == cmd.Key))
                {
                    continue;
                }

                _commands.Add(cmd);
            }
        }

        foreach (Command cfg in configCommands)
        {
            Command? owner = _commands.FirstOrDefault(c => c.Key == cfg.Key);
            if (owner is not null)
            {
                owner.Configure = cfg;
            }
        }
    }

    /// <summary>
    /// Loads a Native-tier plugin's assembly and adds its registered commands into the same
    /// <see cref="_commands"/> collection scripted commands populate, so they're indistinguishable to
    /// <see cref="GetCommands"/>/<see cref="InvokeAsync{TGlobals}"/> (docs/superpowers/specs/
    /// 2026-09-11-plugin-api-v4-native-tier-design.md §3). Skips silently (contributes no commands,
    /// same "one bad plugin doesn't abort the rest" contract as scripted discovery) if
    /// <paramref name="baseEnvironment"/> isn't native-capable, the manifest doesn't name an
    /// assembly, or that assembly doesn't exist - those cases predate this plugin having any
    /// meaningful key to record a <see cref="NativePluginLoadResult"/> against. A real load/
    /// reflection failure inside <see cref="PluginLoadContext.LoadPlugin"/>, and a second package
    /// declaring an already-used manifest key, are both recorded in <see cref="_nativeLoadResults"/>
    /// instead of swallowed (docs/superpowers/specs/2026-09-12-plugin-management-screen-redesign-
    /// design.md §4.2 - this is exactly the concrete "silently vanishes" bug that design fixes).
    /// </summary>
    private void DiscoverNative(PluginManifest manifest, string pluginDir, IPluginEnvironment baseEnvironment, PluginApiCompatibility compat, string? blockedReason)
    {
        if (baseEnvironment is not INativePluginEnvironment nativeEnvironment)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(manifest.Assembly))
        {
            return;
        }

        string assemblyPath = Path.Combine(pluginDir, manifest.Assembly);
        if (!File.Exists(assemblyPath))
        {
            return;
        }

        // Marked for removal (PackageManager.Uninstall's ".remove" marker, docs/superpowers/specs/
        // 2026-09-12-plugin-management-screen-redesign-design.md's restart-to-apply model for a
        // Native-tier package) - the folder is still physically present until the next launch
        // actually commits the delete, but reloading it here anyway serves no purpose: it's going
        // away regardless, and doing so would reopen this plugin's own file-backed resources (a
        // LiteDB connection, say) right after they were just disposed above for exactly this
        // Discover() call, racing the OS's release of that same file instead of just leaving it shut.
        if (File.Exists(Path.Combine(pluginDir, ".remove")))
        {
            return;
        }

        string pluginKey = string.IsNullOrWhiteSpace(manifest.Key) ? Path.GetFileName(pluginDir) : manifest.Key;

        // A plugin whose feature is now built into the app is never loaded: running both would scrape or reorganize the same library twice.
        // Recorded as a load result so the Plugins screen shows why, instead of the package silently vanishing.
        if (RetiredPluginKeys.TryGetValue(pluginKey, out string? retiredMessage))
        {
            _nativeLoadResults[pluginKey] = new NativePluginLoadResult(null, retiredMessage);
            return;
        }

        // Both packages share this exact key, so there is only ever one dictionary slot for it -
        // "the first package keeps its own healthy result untouched" isn't achievable once a second
        // package claims the same key. Surfacing the conflict in that one shared slot (rather than
        // silently letting whichever loads first look falsely fine) is what actually serves this
        // design's goal: the fact that a duplicate exists at all is the important, actionable signal.
        if (_nativeLoadResults.ContainsKey(pluginKey))
        {
            _nativeLoadResults[pluginKey] = new NativePluginLoadResult(null,
                $"Duplicate plugin key '{pluginKey}' - more than one installed plugin folder declares this key.");
            return;
        }

        // requiresApi gate: after the duplicate-key check (that conflict takes precedence) and
        // strictly BEFORE LoadPlugin, so a blocked plugin's assembly is never loaded and its module
        // never constructed. Recorded as a normal load failure so the plugin manager shows it the
        // same way it already shows any other native load error.
        if (blockedReason is not null)
        {
            _nativeLoadResults[pluginKey] = new NativePluginLoadResult(null, blockedReason);
            return;
        }

        try
        {
            (INativePluginModule module, IReadOnlyList<NativeCommand> commands) = PluginLoadContext.LoadPlugin(assemblyPath, pluginKey, nativeEnvironment);
            _nativeLoadResults[pluginKey] = new NativePluginLoadResult(module, null);

            foreach (NativeCommand cmd in commands)
            {
                cmd.DeclaredApi = compat.Declared;
                if (!cmd.Initialize(baseEnvironment, pluginDir))
                {
                    continue;
                }

                if (_commands.Any(c => c.Key == cmd.Key))
                {
                    continue;
                }

                _commands.Add(cmd);
            }
        }
        catch (Exception ex)
        {
            // Activator.CreateInstance (inside PluginLoadContext.LoadPlugin) wraps a throwing
            // constructor in a TargetInvocationException whose own .Message is a generic reflection
            // string ("Exception has been thrown by the target of an invocation.") - found via a
            // failing test, not assumed. Unwrap to the plugin's own real exception so the surfaced
            // LoadError is actually useful instead of exactly the kind of misleading text this whole
            // design exists to eliminate.
            Exception real = ex is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : ex;
            string? hint = PluginApiCompatibility.FailureHint(compat.Declared, PluginApi.Current, IsDriftShaped(real));
            _nativeLoadResults[pluginKey] = new NativePluginLoadResult(null, hint is null ? real.Message : $"{real.Message} ({hint})");
        }
    }

    public IEnumerable<Command> GetCommands(string hook) => _commands.Where(c => c.Enabled && !c.IsBroken && c.IsHook(hook));

    /// <summary>
    /// Invokes every enabled command registered under <paramref name="hook"/>. <paramref name="globalsFactory"/>
    /// builds the hook's typed globals instance per command, using that command's own
    /// <see cref="IPluginEnvironment"/> clone (each command gets its own via <see cref="Command.Initialize"/>).
    /// Never throws - a command's exception is captured in its <see cref="PluginInvocationResult"/> instead,
    /// so one broken command can't stop the rest from running or crash the caller.
    /// </summary>
    public async Task<IReadOnlyList<PluginInvocationResult>> InvokeAsync<TGlobals>(string hook, Func<IPluginEnvironment, TGlobals> globalsFactory)
        where TGlobals : PluginGlobals
    {
        var results = new List<PluginInvocationResult>();
        foreach (Command cmd in GetCommands(hook))
        {
            if (cmd.Environment is null)
            {
                continue;
            }

            TGlobals globals = globalsFactory(cmd.Environment);
            try
            {
                object? returnValue = await cmd.InvokeAsync(globals).ConfigureAwait(false);
                results.Add(new PluginInvocationResult(cmd, true, returnValue, null));
            }
            catch (Exception ex)
            {
                results.Add(new PluginInvocationResult(cmd, false, null, WithVersionHint(cmd, ex)));
            }
        }

        return results;
    }

    /// <summary>
    /// True for the failure shapes plugin-API drift produces - a member or type the host doesn't have
    /// (a plugin built against a newer API), or an assembly that won't load against this one. An
    /// ordinary exception thrown by the plugin's own logic is never drift.
    /// </summary>
    private static bool IsDriftShaped(Exception ex) =>
        ex is MissingMemberException or TypeLoadException or System.Reflection.ReflectionTypeLoadException
            or FileLoadException or BadImageFormatException;

    /// <summary>
    /// Invoke-time hint (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §3.2): unwraps
    /// reflection/aggregate wrappers, and if the real failure is drift-shaped and a hint applies,
    /// returns a <see cref="PluginApiMismatchException"/> carrying it. Anything else passes through
    /// untouched. Never disables the command - one bad call must not silently turn a command off.
    /// </summary>
    private static Exception WithVersionHint(Command cmd, Exception ex)
    {
        Exception real = ex;
        while (real.InnerException is not null && real is System.Reflection.TargetInvocationException or AggregateException)
        {
            real = real.InnerException;
        }

        if (!IsDriftShaped(real))
        {
            return ex;
        }

        string? hint = PluginApiCompatibility.FailureHint(cmd.DeclaredApi, PluginApi.Current, driftShapedFailure: true);
        return hint is null ? ex : new PluginApiMismatchException($"{real.Message} ({hint})", ex);
    }
}
