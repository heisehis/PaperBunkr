using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins.Native;

/// <summary>
/// Wraps one compiled delegate registered through <c>INativeCommandRegistrar</c> as a
/// <see cref="Command"/> (docs/superpowers/specs/2026-09-11-plugin-api-v4-native-tier-design.md §3,
/// implementation plan Phase 1 Step 1.3) - the only other concrete <see cref="Command"/> subclass
/// besides <see cref="CSharpCommand"/>/<c>PythonCommand</c>. There's nothing to compile (the
/// assembly is already loaded by the time a <see cref="NativeCommand"/> exists), so
/// <see cref="Command.PreCompile"/>'s base no-op is left as-is; invoking just calls the stored
/// delegate directly against whatever <see cref="PluginGlobals"/> <see cref="PluginEngine.InvokeAsync{TGlobals}"/>
/// builds for the hook - from this point on, the engine can't tell a native command apart from a
/// scripted one.
/// </summary>
public sealed class NativeCommand : Command
{
    private readonly Func<PluginGlobals, Task<object?>> _handler;

    public NativeCommand(Func<PluginGlobals, Task<object?>> handler)
    {
        _handler = handler;
    }

    protected override Task<object?> OnInvokeAsync(PluginGlobals globals) => _handler(globals);
}
