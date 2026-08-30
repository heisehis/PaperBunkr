using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins.Tests;

/// <summary>
/// <see cref="PythonCommand"/> (docs/superpowers/specs/2026-08-30-python-plugin-scripting-design.md),
/// exercised the same way <see cref="PluginEngineTests"/> exercises <see cref="CSharpCommand"/> -
/// real manifest + script files written to a temp dir, discovered through the real
/// <see cref="PluginEngine"/>, not the class in isolation.
/// </summary>
public sealed class PythonCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "paperbunkr-python-plugin-tests-" + Guid.NewGuid().ToString("N"));

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
    public void Discover_ValidPythonScript_CompilesCleanly()
    {
        WritePlugin("py-sample", """
            <Plugin key="py-sample" name="Python Sample">
              <Command hook="Startup" key="py-sample.startup" name="Say Hello" script="startup.py" method="on_startup" />
            </Plugin>
            """, new Dictionary<string, string>
        {
            ["startup.py"] = "def on_startup(globals):\n    return 'hello'\n",
        });

        var engine = new PluginEngine();
        engine.Discover(_root, new FakePluginEnvironment());

        Command cmd = Assert.Single(engine.AllCommands);
        Assert.Equal("py-sample.startup", cmd.Key);
        Assert.IsType<PythonCommand>(cmd);
        Assert.False(cmd.IsBroken);
        Assert.Null(cmd.CompileError);
    }

    [Fact]
    public void Discover_PythonSyntaxError_SetsCompileError_DoesNotAbortOtherPlugins()
    {
        WritePlugin("py-broken", """
            <Plugin key="py-broken" name="Python Broken">
              <Command hook="Startup" key="py-broken.startup" name="Broken" script="startup.py" method="on_startup" />
            </Plugin>
            """, new Dictionary<string, string>
        {
            ["startup.py"] = "def on_startup(globals:\n    return 'unterminated def'\n",
        });
        WritePlugin("py-good", """
            <Plugin key="py-good" name="Python Good">
              <Command hook="Startup" key="py-good.startup" name="Good" script="startup.py" method="on_startup" />
            </Plugin>
            """, new Dictionary<string, string>
        {
            ["startup.py"] = "def on_startup(globals):\n    return 1\n",
        });

        var engine = new PluginEngine();
        engine.Discover(_root, new FakePluginEnvironment());

        Assert.Equal(2, engine.AllCommands.Count);
        Command broken = engine.AllCommands.Single(c => c.Key == "py-broken.startup");
        Command good = engine.AllCommands.Single(c => c.Key == "py-good.startup");
        Assert.True(broken.IsBroken);
        Assert.NotNull(broken.CompileError);
        Assert.False(good.IsBroken);
    }

    [Fact]
    public void Discover_MethodNotFoundInScript_SetsCompileError()
    {
        WritePlugin("py-missing-method", """
            <Plugin key="py-missing-method" name="Missing Method">
              <Command hook="Startup" key="py-missing-method.startup" name="Missing" script="startup.py" method="does_not_exist" />
            </Plugin>
            """, new Dictionary<string, string>
        {
            ["startup.py"] = "def on_startup(globals):\n    return 1\n",
        });

        var engine = new PluginEngine();
        engine.Discover(_root, new FakePluginEnvironment());

        Command cmd = Assert.Single(engine.AllCommands);
        Assert.True(cmd.IsBroken);
        Assert.Contains("does_not_exist", cmd.CompileError);
    }

    [Fact]
    public async Task InvokeAsync_CallsTheNamedFunction_PassesGlobalsAsOneArgument_ReturnsItsResult()
    {
        WritePlugin("py-globals", """
            <Plugin key="py-globals" name="Python Globals">
              <Command hook="Library" key="py-globals.count" name="Count" script="count.py" method="count_books" />
            </Plugin>
            """, new Dictionary<string, string>
        {
            ["count.py"] = "def count_books(globals):\n    return len(globals.Books)\n",
        });

        var engine = new PluginEngine();
        engine.Discover(_root, new FakePluginEnvironment());

        var books = new List<Paperbunkr.Data.Entities.Issue> { new() { Id = 1, SeriesId = 1 }, new() { Id = 2, SeriesId = 1 } };
        var results = await engine.InvokeAsync(PluginHooks.Library, env => new BooksHookGlobals { Environment = env, Books = books });

        PluginInvocationResult result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Equal(2, result.ReturnValue);
    }

    // --- Sandbox: the static clr.AddReference scan (docs/superpowers/specs/2026-08-30-python-
    //     plugin-scripting-design.md's "Sandbox" section) - the fix actually landed after both a
    //     custom PlatformAdaptationLayer and real AssemblyLoadContext isolation were verified,
    //     empirically, to not work (IronPython's own clr.AddReference falls back to
    //     Assembly.LoadWithPartialName on any failure, which resolves against the whole process,
    //     not scoped to any host-provided boundary). ---

    [Fact]
    public void Discover_ScriptCallsClrAddReferenceToEfCore_RejectedAtPreCompile_NeverExecutes()
    {
        WritePlugin("py-sandbox-denied", """
            <Plugin key="py-sandbox-denied" name="Sandbox Denied">
              <Command hook="Startup" key="py-sandbox-denied.startup" name="Probe" script="probe.py" method="probe" />
            </Plugin>
            """, new Dictionary<string, string>
        {
            ["probe.py"] = """
                def probe(globals):
                    import clr
                    clr.AddReference('Microsoft.EntityFrameworkCore')
                    from Microsoft.EntityFrameworkCore import DbContext
                    return 'reached DbContext'
                """,
        });

        var engine = new PluginEngine();
        engine.Discover(_root, new FakePluginEnvironment());

        Command cmd = Assert.Single(engine.AllCommands);
        Assert.True(cmd.IsBroken);
        Assert.Contains("Microsoft.EntityFrameworkCore", cmd.CompileError);
    }

    [Theory]
    [InlineData("Microsoft.Data.Sqlite")]
    [InlineData("SQLitePCLRaw.core")]
    public void Discover_ScriptCallsClrAddReferenceToOtherDeniedAssemblies_RejectedAtPreCompile(string deniedName)
    {
        string pluginKey = "py-sandbox-denied-" + deniedName.Replace('.', '-');
        WritePlugin(pluginKey, $"""
            <Plugin key="{pluginKey}" name="Sandbox">
              <Command hook="Startup" key="{pluginKey}.startup" name="Probe" script="probe.py" method="probe" />
            </Plugin>
            """, new Dictionary<string, string>
        {
            ["probe.py"] = $"""
                def probe(globals):
                    import clr
                    clr.AddReference('{deniedName}')
                    return 'reached it'
                """,
        });

        var engine = new PluginEngine();
        engine.Discover(_root, new FakePluginEnvironment());

        Command cmd = Assert.Single(engine.AllCommands);
        Assert.True(cmd.IsBroken);
        Assert.Contains(deniedName, cmd.CompileError);
    }

    [Fact]
    public void Discover_ScriptWithNoClrAddReference_CompilesCleanly()
    {
        // Sanity check: the scan doesn't false-positive on an ordinary script that never touches
        // clr.AddReference at all - the same category of script every other test in this file uses.
        WritePlugin("py-sandbox-clean", """
            <Plugin key="py-sandbox-clean" name="Sandbox Clean">
              <Command hook="Startup" key="py-sandbox-clean.startup" name="Clean" script="clean.py" method="clean" />
            </Plugin>
            """, new Dictionary<string, string>
        {
            ["clean.py"] = "def clean(globals):\n    return 'no clr here'\n",
        });

        var engine = new PluginEngine();
        engine.Discover(_root, new FakePluginEnvironment());

        Assert.False(Assert.Single(engine.AllCommands).IsBroken);
    }
}
