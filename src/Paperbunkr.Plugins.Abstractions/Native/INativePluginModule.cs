namespace Paperbunkr.Plugins.Abstractions.Native;

/// <summary>
/// Entry point a native-tier plugin assembly implements exactly once (docs/superpowers/specs/
/// 2026-09-11-plugin-api-v4-native-tier-design.md §3). <see cref="PluginLoadContext"/> reflection-scans
/// the loaded assembly for the single type implementing this interface.
/// </summary>
public interface INativePluginModule
{
    /// <summary>Called once, immediately after the plugin's assembly loads. A plugin wires up its own
    /// services here (e.g. constructing its own <c>HttpClient</c>-backed services, opening its own
    /// local database) and may start its own background work (e.g. a self-contained automation
    /// timer, per CLM §9's fallback) if its persisted settings say to.</summary>
    void Initialize(INativePluginEnvironment environment);

    /// <summary>Called once, immediately after <see cref="Initialize"/>. Registers the plugin's
    /// commands against <paramref name="registrar"/> so they appear on the Plugin screen and are
    /// invocable through the same hook-dispatch path <c>.csx</c> commands use.</summary>
    void RegisterCommands(INativeCommandRegistrar registrar);
}
