using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins;

/// <summary>
/// Ported from ComicRackCE's abstract <c>Command</c> (docs/superpowers/specs/
/// 2026-08-24-plugin-api-v2-design.md §3), stripped of WinForms-only members (<c>Keys</c>
/// shortcuts, GDI+ <c>Image</c> theming). <see cref="CSharpCommand"/> is the only concrete
/// subclass - CE's XML/Python split collapses into one script-backed command type per §2's
/// "XML manifest + .csx script" decision.
/// </summary>
public abstract class Command
{
    /// <summary>Key of the plugin this command belongs to (<see cref="PluginManifest.Key"/>) - identifies the plugin for grouping on the Plugin screen and for <see cref="Data.Entities.PluginCommandState"/> persistence.</summary>
    public required string PluginKey { get; init; }

    public required string Hook { get; init; }

    public required string Key { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Relative path (from the plugin's folder) to an icon image, or null.</summary>
    public string? Image { get; init; }

    public bool Enabled { get; set; } = true;

    /// <summary>Per-command clone of the environment, with <see cref="IPluginEnvironment.CommandPath"/> set to this command's plugin folder. Set by <see cref="Initialize"/>.</summary>
    public IPluginEnvironment? Environment { get; private set; }

    /// <summary>A paired config command sharing this command's <see cref="Key"/>, present when the manifest also declares a <see cref="Hooks.PluginHooks.ConfigScript"/> command for it.</summary>
    public Command? Configure { get; set; }

    /// <summary>Compile/parse error captured at discovery time, surfaced on the Plugin screen. Null when the command loaded cleanly.</summary>
    public string? CompileError { get; protected set; }

    public bool IsBroken => CompileError is not null;

    public bool IsHook(params string[] hooks) => hooks.Contains(Hook);

    /// <summary>Clones <paramref name="env"/>, points the clone's <see cref="IPluginEnvironment.CommandPath"/> at <paramref name="pluginPath"/>, and runs any subclass-specific precompilation. Returns false (never throws) if the command couldn't be prepared.</summary>
    public bool Initialize(IPluginEnvironment env, string pluginPath)
    {
        try
        {
            Environment = (IPluginEnvironment)env.Clone();
            Environment.CommandPath = pluginPath;
            OnInitialize(pluginPath);
            return true;
        }
        catch (Exception ex)
        {
            CompileError = ex.Message;
            return false;
        }
    }

    public async Task<object?> InvokeAsync(PluginGlobals globals)
    {
        return await OnInvokeAsync(globals).ConfigureAwait(false);
    }

    protected virtual void OnInitialize(string pluginPath)
    {
    }

    /// <summary>Eager compile step run once at discovery (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §2) so a broken script surfaces its error on the Plugin screen immediately rather than on first invoke. No-op for command types with nothing to precompile.</summary>
    public virtual void PreCompile()
    {
    }

    protected abstract Task<object?> OnInvokeAsync(PluginGlobals globals);
}
