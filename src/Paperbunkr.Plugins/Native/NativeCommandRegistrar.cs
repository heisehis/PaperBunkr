using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Abstractions.Native;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins.Native;

/// <summary>
/// Host-side <c>INativeCommandRegistrar</c> implementation (docs/superpowers/specs/2026-09-11-
/// plugin-api-v4-native-tier-design.md §3, implementation plan Phase 1 Step 1.3/1.4) - collects a
/// native plugin's <c>RegisterCommands</c> registrations as <see cref="NativeCommand"/> instances
/// ready for <see cref="PluginEngine.Discover"/> to add alongside scripted ones. Not exposed to
/// plugin authors directly (they only ever see the <see cref="INativeCommandRegistrar"/> interface).
/// </summary>
internal sealed class NativeCommandRegistrar : INativeCommandRegistrar
{
    private readonly string _pluginKey;
    private readonly List<NativeCommand> _commands = new();

    public NativeCommandRegistrar(string pluginKey)
    {
        _pluginKey = pluginKey;
    }

    public IReadOnlyList<NativeCommand> Commands => _commands;

    public void OnStartup(
        string key,
        string name,
        Func<INativePluginEnvironment, Task<object?>> handler,
        string? description = null)
    {
        _commands.Add(new NativeCommand(globals => handler((INativePluginEnvironment)globals.Environment))
        {
            PluginKey = _pluginKey,
            Hook = PluginHooks.Startup,
            Key = key,
            Name = name,
            Description = description,
        });
    }

    public void OnLibrary(
        string key,
        string name,
        Func<INativePluginEnvironment, IReadOnlyList<Issue>, Task<object?>> handler,
        string? description = null,
        bool confirmWrites = false)
    {
        _commands.Add(new NativeCommand(globals =>
        {
            var books = ((BooksHookGlobals)globals).Books;
            return handler((INativePluginEnvironment)globals.Environment, books);
        })
        {
            PluginKey = _pluginKey,
            Hook = PluginHooks.Library,
            Key = key,
            Name = name,
            Description = description,
            ConfirmWrites = confirmWrites,
        });
    }
}
