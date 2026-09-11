using Paperbunkr.Plugins.Abstractions.Native;

namespace MinimalNative;

/// <summary>Test fixture only - the minimal possible native plugin, used to prove
/// <see cref="Paperbunkr.Plugins.Native.PluginLoadContext"/>/<see cref="Paperbunkr.Plugins.PluginEngine"/>'s
/// native discovery path end to end (implementation plan Phase 1 Step 1.3/1.4).</summary>
public sealed class MinimalNativePlugin : INativePluginModule
{
    // Instance field, not static - a plugin loaded into its own AssemblyLoadContext gets its own
    // independent copy of this type, with its own independent static state, separate from any copy
    // already loaded elsewhere (e.g. by a test directly referencing this project for typeof(...)
    // convenience). Observing plugin state from outside must go through a registered command's
    // return value, the same way any real caller would, not by reading a static field across the ALC
    // boundary - found the hard way when a static-field-based test failed for exactly this reason.
    private bool _initialized;

    public void Initialize(INativePluginEnvironment environment)
    {
        _initialized = true;
    }

    public void RegisterCommands(INativeCommandRegistrar registrar)
    {
        registrar.OnStartup("minimal-native.startup", "Minimal Native Startup",
            _ => Task.FromResult<object?>("native-startup-ran"));

        registrar.OnStartup("minimal-native.was-initialized", "Was Initialized",
            _ => Task.FromResult<object?>(_initialized));

        registrar.OnLibrary("minimal-native.count-books", "Count Selected Books",
            (_, books) => Task.FromResult<object?>(books.Count));
    }
}
