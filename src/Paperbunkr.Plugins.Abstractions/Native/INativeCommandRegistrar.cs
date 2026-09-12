using Paperbunkr.Data.Entities;

namespace Paperbunkr.Plugins.Abstractions.Native;

/// <summary>
/// What a native plugin's <see cref="INativePluginModule.RegisterCommands"/> registers compiled
/// delegates against, one method per hook type it needs (docs/superpowers/specs/2026-09-11-plugin-
/// api-v4-native-tier-design.md §3) — the same 17 hook types `.csx` commands use, so native and
/// scripted commands appear side by side in the same hook-grouped list on the Plugin screen. Only
/// the two hooks Cluster Library Manager needs are exposed so far (<see cref="OnStartup"/>,
/// <see cref="OnLibrary"/>); add more as a future native plugin actually needs them, mirroring the
/// corresponding `Hooks.*HookGlobals` payload shape.
/// </summary>
public interface INativeCommandRegistrar
{
    /// <summary>Registers a <c>Startup</c> hook command — fires once per app session, no input
    /// payload, mirroring <c>Hooks.StartupHookGlobals</c>.</summary>
    void OnStartup(
        string key,
        string name,
        Func<INativePluginEnvironment, Task<object?>> handler,
        string? description = null);

    /// <summary>Registers a <c>Library</c> hook command — the right-click "operate on selected
    /// comics" family, mirroring <c>Hooks.BooksHookGlobals</c>. <paramref name="confirmWrites"/>
    /// matches the manifest attribute of the same name on the `.csx` side (docs/superpowers/specs/
    /// 2026-08-28-plugin-api-v3-data-manager-design.md §5) — native plugins are full-trust and don't
    /// route writes through <c>IMetadataWriter</c> at all (v4 §2), so this flag is accepted for
    /// symmetry/manifest-shape purposes only and currently has no gating effect for a native command;
    /// it exists so a future tightening of native-plugin write auditing has somewhere to attach to
    /// without a breaking interface change.</summary>
    void OnLibrary(
        string key,
        string name,
        Func<INativePluginEnvironment, IReadOnlyList<Issue>, Task<object?>> handler,
        string? description = null,
        bool confirmWrites = false);
}
