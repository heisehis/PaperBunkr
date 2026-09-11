using Paperbunkr.Plugins.Abstractions.Native;
using Paperbunkr.Plugins.Hooks;
using Paperbunkr.Plugins.Native;

namespace Paperbunkr.Plugins;

/// <summary>One command's outcome from a <see cref="PluginEngine.InvokeAsync{TGlobals}"/> call - lets callers decide how to surface a failure (toast, log) without <c>Paperbunkr.Plugins</c> depending on any App-layer service.</summary>
public sealed record PluginInvocationResult(Command Command, bool Success, object? ReturnValue, Exception? Error);

/// <summary>
/// Ported from ComicRackCE's <c>PluginEngine</c> (docs/superpowers/specs/
/// 2026-08-24-plugin-api-v2-design.md §2/§3): discovers plugin manifests under a root folder,
/// initializes + eagerly precompiles their commands, and dispatches hook invocations to every
/// enabled command registered under that hook.
/// </summary>
public sealed class PluginEngine
{
    private readonly CommandCollection _commands = new();

    public IReadOnlyList<Command> AllCommands => _commands;

    /// <summary>Walks <paramref name="pluginsRoot"/> for <c>plugin.xml</c> manifests, initializing and precompiling every command found. Never throws - a broken plugin is flagged via <see cref="Command.IsBroken"/>, not skipped from discovery, and never aborts loading the rest (docs §2).</summary>
    public void Discover(string pluginsRoot, IPluginEnvironment baseEnvironment)
    {
        _commands.Clear();
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
            if (manifest is not null && string.Equals(manifest.Tier, "Native", StringComparison.OrdinalIgnoreCase))
            {
                DiscoverNative(manifest, pluginDir, baseEnvironment);
                continue;
            }

            foreach (Command cmd in XmlPluginInitializer.GetCommands(manifestFile))
            {
                if (!cmd.Initialize(baseEnvironment, pluginDir))
                {
                    continue;
                }

                cmd.PreCompile();

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
    /// assembly, or that assembly doesn't exist. A load/reflection failure inside
    /// <see cref="PluginLoadContext.LoadPlugin"/> is also swallowed here - there's no natural
    /// <see cref="Command.CompileError"/>-style surface for a native load failure since it never
    /// becomes a <see cref="Command"/> at all, so today this is a genuinely silent failure beyond
    /// contributing zero commands. Surfacing a distinct "plugin failed to load" row on the Plugin
    /// screen is real follow-up work, out of scope for this pass.
    /// </summary>
    private void DiscoverNative(PluginManifest manifest, string pluginDir, IPluginEnvironment baseEnvironment)
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

        string pluginKey = string.IsNullOrWhiteSpace(manifest.Key) ? Path.GetFileName(pluginDir) : manifest.Key;

        try
        {
            (_, IReadOnlyList<NativeCommand> commands) = PluginLoadContext.LoadPlugin(assemblyPath, pluginKey, nativeEnvironment);
            foreach (NativeCommand cmd in commands)
            {
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
        catch (Exception)
        {
            // Intentionally swallowed - see method doc comment.
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
                results.Add(new PluginInvocationResult(cmd, false, null, ex));
            }
        }

        return results;
    }
}
