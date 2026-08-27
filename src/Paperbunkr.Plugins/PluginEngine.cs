using Paperbunkr.Plugins.Hooks;

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
