using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins.Tests;

/// <summary>
/// <see cref="PluginScriptRunner"/> (Plugin API 4.5 §A4) - the harness a plugin author points at their own plugin folder: it runs the
/// real discovery and the real sandbox, returns what the command returned, and records what it logged.
/// </summary>
public sealed class PluginScriptRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pb-runner-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string Plugin(string script)
    {
        string folder = Path.Combine(_dir, "counter");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "plugin.xml"), """
            <Plugin key="counter" name="Counter">
              <Command hook="Library" key="counter.run" name="Count" script="run.csx" />
            </Plugin>
            """);
        File.WriteAllText(Path.Combine(folder, "run.csx"), script);
        Directory.CreateDirectory(Path.Combine(_dir, "sibling"));
        File.WriteAllText(Path.Combine(_dir, "sibling", "plugin.xml"), "<Plugin key=\"sibling\" name=\"Sibling\" />");
        return folder;
    }

    [Fact]
    public async Task Runs_a_command_and_records_its_log()
    {
        var runner = new PluginScriptRunner(Plugin("""Environment.Log.Info($"counted {Books.Count}"); return Books.Count;"""));

        object? result = await runner.RunAsync("counter.run", env => new BooksHookGlobals
        {
            Environment = env,
            Books = new[] { new Issue(), new Issue(), new Issue() },
        });

        Assert.Equal(3, result);
        Assert.Contains(runner.Environment.RecordedLog.Entries, e => e.Message == "counted 3");
        Assert.Null(runner.Problem);
        Assert.DoesNotContain("sibling", runner.Engine.PackageNames.Keys);   // only the named folder is discovered
    }

    [Fact]
    public async Task A_script_that_does_not_compile_is_reported_not_run()
    {
        var runner = new PluginScriptRunner(Plugin("this is not C#"));

        Assert.NotNull(runner.Problem);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync("counter.run", env => new BooksHookGlobals { Environment = env, Books = Array.Empty<Issue>() }));
        Assert.Contains("didn't compile", error.Message);
    }

    [Fact]
    public async Task An_unknown_command_key_says_what_went_wrong()
    {
        var runner = new PluginScriptRunner(Plugin("return 1;"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync("counter.nope", env => new BooksHookGlobals { Environment = env, Books = Array.Empty<Issue>() }));
    }
}
