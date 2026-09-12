using Paperbunkr.Plugins.Abstractions.Native;

namespace ThrowingNative;

/// <summary>Test fixture only - a native plugin whose constructor throws, used to prove
/// <see cref="Paperbunkr.Plugins.PluginEngine.DiscoverNative"/> records the failure in
/// <see cref="Paperbunkr.Plugins.NativePluginLoadResult"/> instead of swallowing it (docs/superpowers/
/// specs/2026-09-12-plugin-management-screen-redesign-plan.md Step 2).</summary>
public sealed class ThrowingNativePlugin : INativePluginModule
{
    public ThrowingNativePlugin()
    {
        throw new InvalidOperationException("Simulated native plugin load failure.");
    }

    public void Initialize(INativePluginEnvironment environment)
    {
    }

    public void RegisterCommands(INativeCommandRegistrar registrar)
    {
    }
}
