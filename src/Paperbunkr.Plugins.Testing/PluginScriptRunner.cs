using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins.Testing;

/// <summary>
/// Runs a real plugin folder the way Paperbunkr does, for a plugin author's own tests (docs/superpowers/specs/2026-10-28-plugin-api-
/// 4-5-pitch-completion-design.md §A4): discovers <paramref name="pluginFolder"/> through the real <see cref="PluginEngine"/> (the
/// same manifest checks, the same script sandbox) against a <see cref="FakePluginEnvironment"/>, then invokes one command by key.
/// <code>
/// var runner = new PluginScriptRunner(@"..\..\..\my-plugin");
/// object? result = await runner.RunAsync("my-plugin.run", env => new BooksHookGlobals { Environment = env, Books = books });
/// Assert.Contains(runner.Environment.RecordedLog.Entries, e => e.Message.Contains("Ran on"));
/// </code>
/// </summary>
public sealed class PluginScriptRunner
{
    public PluginScriptRunner(string pluginFolder, FakePluginEnvironment? environment = null)
    {
        Environment = environment ?? new FakePluginEnvironment();
        string folder = Path.GetFullPath(pluginFolder);

        // Discovery walks a root; a copy of just this one plugin under a private root keeps a sibling folder out of the run.
        Root = Path.Combine(Path.GetTempPath(), "paperbunkr-plugin-runner-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(folder, Path.Combine(Root, Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar))));
        Engine.Discover(Root, Environment);
    }

    public PluginEngine Engine { get; } = new();

    /// <summary>What the plugin's commands logged, reported and stored, through every per-command clone.</summary>
    public FakePluginEnvironment Environment { get; }

    /// <summary>The private discovery root (a copy). Deleted by nothing - it lives under the temp folder.</summary>
    public string Root { get; }

    /// <summary>Why the plugin didn't load, or null when it did (a manifest block, or the first broken command's compile error).</summary>
    public string? Problem =>
        Engine.PackageApiInfo.Values.Select(i => i.BlockedReason).FirstOrDefault(r => r is not null)
        ?? Engine.AllCommands.FirstOrDefault(c => c.IsBroken)?.CompileError;

    /// <summary>Invokes the command with key <paramref name="commandKey"/> and returns what it returned. Throws what the command threw.</summary>
    public async Task<object?> RunAsync<TGlobals>(string commandKey, Func<IPluginEnvironment, TGlobals> globals)
        where TGlobals : PluginGlobals
    {
        Command command = Engine.AllCommands.FirstOrDefault(c => c.Key == commandKey)
            ?? throw new InvalidOperationException($"No command '{commandKey}' was discovered. {Problem}");
        if (command.IsBroken)
        {
            throw new InvalidOperationException($"Command '{commandKey}' didn't compile: {command.CompileError}");
        }

        return await command.InvokeAsync(globals(command.Environment ?? Environment)).ConfigureAwait(false);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }

        foreach (string dir in Directory.GetDirectories(from))
        {
            string name = Path.GetFileName(dir);
            if (name is "bin" or "obj")
            {
                continue;
            }

            CopyDirectory(dir, Path.Combine(to, name));
        }
    }
}
