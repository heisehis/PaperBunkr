using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins.Tests;

public sealed class PluginEngineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "paperbunkr-plugin-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string WritePlugin(string pluginKey, string manifestXml, IReadOnlyDictionary<string, string> scripts)
    {
        string dir = Path.Combine(_root, pluginKey);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.xml"), manifestXml);
        foreach (var (name, content) in scripts)
        {
            File.WriteAllText(Path.Combine(dir, name), content);
        }

        return dir;
    }

    [Fact]
    public void Discover_compiles_and_registers_a_valid_command()
    {
        WritePlugin("sample", """
            <Plugin key="sample" name="Sample">
              <Command hook="Startup" key="sample.startup" name="Say Hello" script="startup.csx" />
            </Plugin>
            """, new Dictionary<string, string>
        {
            ["startup.csx"] = "return \"hello\";",
        });

        var engine = new PluginEngine();
        engine.Discover(_root, new FakePluginEnvironment());

        Command cmd = Assert.Single(engine.AllCommands);
        Assert.Equal("sample.startup", cmd.Key);
        Assert.False(cmd.IsBroken);
        Assert.Null(cmd.CompileError);
    }

    [Fact]
    public void Discover_flags_a_broken_script_without_aborting_other_plugins()
    {
        WritePlugin("broken", """
            <Plugin key="broken" name="Broken">
              <Command hook="Startup" key="broken.startup" name="Broken" script="startup.csx" />
            </Plugin>
            """, new Dictionary<string, string>
        {
            ["startup.csx"] = "this is not valid C#;;;",
        });
        WritePlugin("good", """
            <Plugin key="good" name="Good">
              <Command hook="Startup" key="good.startup" name="Good" script="startup.csx" />
            </Plugin>
            """, new Dictionary<string, string>
        {
            ["startup.csx"] = "return 1;",
        });

        var engine = new PluginEngine();
        engine.Discover(_root, new FakePluginEnvironment());

        Assert.Equal(2, engine.AllCommands.Count);
        Command broken = engine.AllCommands.Single(c => c.Key == "broken.startup");
        Command good = engine.AllCommands.Single(c => c.Key == "good.startup");
        Assert.True(broken.IsBroken);
        Assert.NotNull(broken.CompileError);
        Assert.False(good.IsBroken);
    }

    [Fact]
    public async Task InvokeAsync_only_calls_enabled_commands_and_passes_the_typed_payload()
    {
        WritePlugin("library-cmd", """
            <Plugin key="library-cmd" name="LibraryCmd">
              <Command hook="Library" key="library-cmd.count" name="Count" script="count.csx" />
              <Command hook="Library" key="library-cmd.disabled" name="Disabled" enabled="false" script="count.csx" />
            </Plugin>
            """, new Dictionary<string, string>
        {
            ["count.csx"] = "return Books.Count;",
        });

        var engine = new PluginEngine();
        engine.Discover(_root, new FakePluginEnvironment());

        var books = new List<Paperbunkr.Data.Entities.Issue> { new() { Id = 1, SeriesId = 1 }, new() { Id = 2, SeriesId = 1 } };
        var results = await engine.InvokeAsync(PluginHooks.Library, env => new BooksHookGlobals { Environment = env, Books = books });

        PluginInvocationResult result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Equal(2, result.ReturnValue);
    }

    [Fact]
    public async Task InvokeAsync_captures_a_command_exception_instead_of_throwing()
    {
        WritePlugin("throws", """
            <Plugin key="throws" name="Throws">
              <Command hook="Startup" key="throws.startup" name="Throws" script="startup.csx" />
            </Plugin>
            """, new Dictionary<string, string>
        {
            ["startup.csx"] = "throw new System.InvalidOperationException(\"boom\");",
        });

        var engine = new PluginEngine();
        engine.Discover(_root, new FakePluginEnvironment());

        var results = await engine.InvokeAsync(PluginHooks.Startup, env => new StartupHookGlobals { Environment = env });

        PluginInvocationResult result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.Contains("boom", result.Error?.Message);
    }

    [Fact]
    public void Discover_pairs_a_ConfigScript_command_onto_its_matching_key()
    {
        WritePlugin("configurable", """
            <Plugin key="configurable" name="Configurable">
              <Command hook="Library" key="configurable.cmd" name="Main" script="main.csx" />
              <Command hook="ConfigScript" key="configurable.cmd" name="Configure" script="config.csx" />
            </Plugin>
            """, new Dictionary<string, string>
        {
            ["main.csx"] = "return 1;",
            ["config.csx"] = "return 1;",
        });

        var engine = new PluginEngine();
        engine.Discover(_root, new FakePluginEnvironment());

        Command main = Assert.Single(engine.AllCommands);
        Assert.NotNull(main.Configure);
        Assert.Equal(PluginHooks.ConfigScript, main.Configure!.Hook);
    }
}
