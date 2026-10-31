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

    /// <summary>The manifest's <c>shortcut</c> attribute (Plugin API 4.3): the command's preferred default keyboard shortcut, as text. The host decides whether it can honour it.</summary>
    public string? Shortcut { get; init; }

    /// <summary>Relative path (from the plugin's folder) to an icon image, or null.</summary>
    public string? Image { get; init; }

    /// <summary>
    /// Manifest <c>confirmWrites</c> attribute (docs/superpowers/specs/2026-08-28-plugin-api-v3-data-
    /// manager-design.md §5), default false. When true, <c>IMetadataWriter</c> calls from this
    /// command fail closed until the user has answered an <c>IApplication.AskQuestion</c>
    /// affirmatively in the same invocation.
    /// </summary>
    public bool ConfirmWrites { get; init; }

    /// <summary>The manifest's <c>menus</c> attribute as written (Plugin API 4.5 §E), or null; see <see cref="ShowsInMenu"/>.</summary>
    public string? Menus { get; init; }

    /// <summary>The manifest's <c>toolbar</c> attribute (Plugin API 4.5 §E): listed under the Library toolbar's Plugins button.</summary>
    public bool Toolbar { get; init; }

    /// <summary>Right-click menus a command can be shown in (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §E).</summary>
    public static class MenuNames
    {
        public const string Library = "library";
        public const string Detail = "detail";
        public const string ReadingList = "readinglist";
        public const string Books = "books";
    }

    /// <summary>
    /// True when this command belongs in <paramref name="menu"/>. With no <see cref="Menus"/> declared the old placement holds: a
    /// Library command in the Library and Detail menus, a Books command in the Books menu, and nothing else anywhere.
    /// </summary>
    public bool ShowsInMenu(string menu)
    {
        if (string.IsNullOrWhiteSpace(Menus))
        {
            return Hook switch
            {
                PluginHooks.Library => menu is MenuNames.Library or MenuNames.Detail,
                PluginHooks.Books => menu == MenuNames.Books,
                _ => false,
            };
        }

        return Menus.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(m => string.Equals(m, menu, StringComparison.OrdinalIgnoreCase));
    }

    public bool Enabled { get; set; } = true;

    /// <summary>Per-command clone of the environment, with <see cref="IPluginEnvironment.CommandPath"/> set to this command's plugin folder. Set by <see cref="Initialize"/>.</summary>
    public IPluginEnvironment? Environment { get; private set; }

    /// <summary>A paired config command sharing this command's <see cref="Key"/>, present when the manifest also declares a <see cref="Hooks.PluginHooks.ConfigScript"/> command for it.</summary>
    public Command? Configure { get; set; }

    /// <summary>Compile/parse error captured at discovery time, surfaced on the Plugin screen. Null when the command loaded cleanly.</summary>
    public string? CompileError { get; protected set; }

    public bool IsBroken => CompileError is not null;

    /// <summary>
    /// The API version this command's plugin declared via <c>requiresApi</c> (docs/superpowers/specs/
    /// 2026-09-20-plugin-api-4-1-design.md §3), or null when the attribute was absent (the 4.0
    /// baseline). Set by <see cref="PluginEngine"/> at discovery; used only to explain a failure.
    /// </summary>
    public Version? DeclaredApi { get; set; }

    /// <summary>Appends a version hint to an already-recorded <see cref="CompileError"/> (its setter is protected). No-op when there's no error or no hint.</summary>
    internal void AppendVersionHint(string? hint)
    {
        if (CompileError is not null && hint is not null)
        {
            CompileError = $"{CompileError} ({hint})";
        }
    }

    public bool IsHook(params string[] hooks) => hooks.Contains(Hook);

    /// <summary>Clones <paramref name="env"/>, points the clone's <see cref="IPluginEnvironment.CommandPath"/> at <paramref name="pluginPath"/>, and runs any subclass-specific precompilation. Returns false (never throws) if the command couldn't be prepared.</summary>
    public bool Initialize(IPluginEnvironment env, string pluginPath)
    {
        try
        {
            Environment = (IPluginEnvironment)env.Clone();
            Environment.CommandPath = pluginPath;
            Environment.PluginKey = PluginKey;
            OnInitialize(pluginPath);
            return true;
        }
        catch (Exception ex)
        {
            CompileError = ex.Message;
            return false;
        }
    }

    /// <summary>How this command has behaved since the app started - runs, failures and durations (docs/superpowers/specs/2026-09-20-plugin-api-4-2-followons-design.md §2). In memory only.</summary>
    public CommandStats Stats { get; } = new();

    public async Task<object?> InvokeAsync(PluginGlobals globals)
    {
        // Per-invocation confirmation gate for IMetadataWriter (docs/superpowers/specs/2026-08-28-
        // plugin-api-v3-data-manager-design.md §5) - scoped to exactly this call, flows across the
        // script's awaits.
        using var _ = PluginInvocationContext.Enter(PluginKey, ConfirmWrites);

        // Timed here, in the one method every hook and every caller goes through, so the numbers cover
        // the CE-era hooks and the domain hooks alike. A thrown exception is counted and rethrown untouched.
        var timer = System.Diagnostics.Stopwatch.StartNew();
        bool failed = false;
        try
        {
            return await OnInvokeAsync(globals).ConfigureAwait(false);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            timer.Stop();
            Stats.RecordRun(timer.Elapsed, failed);
        }
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
