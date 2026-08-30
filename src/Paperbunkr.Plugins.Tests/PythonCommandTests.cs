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

    // --- Sandbox probe (docs/superpowers/specs/2026-08-30-python-plugin-scripting-design.md's own
    //     open caveat, made concrete): does clr.AddReference reach an assembly this process loaded
    //     for its own purposes (EF Core) but never registered with the script engine? Both outcomes
    //     are a *pass* here - whichever happens is the real finding, not a test to force one way. ---

    [Fact]
    public async Task InvokeAsync_ClrAddReferenceToEfCore_ReportsWhetherTheSandboxHolds()
    {
        WritePlugin("py-sandbox-probe", """
            <Plugin key="py-sandbox-probe" name="Sandbox Probe">
              <Command hook="Startup" key="py-sandbox-probe.startup" name="Probe" script="probe.py" method="probe" />
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
        Assert.False(Assert.Single(engine.AllCommands).IsBroken);

        var results = await engine.InvokeAsync(PluginHooks.Startup, env => new StartupHookGlobals { Environment = env });
        PluginInvocationResult result = Assert.Single(results);

        if (result.Success)
        {
            // FINDING: the sandbox caveat is real - clr.AddReference reached an assembly (EF Core)
            // this process loaded for its own purposes but never explicitly registered with the
            // Python script engine. This is the design's own stated follow-up trigger (docs/
            // superpowers/specs/2026-08-30-python-plugin-scripting-design.md, "Sandbox" section) -
            // an AssemblyLoadContext-level block or an import/clr.AddReference hook is needed to
            // actually close this, not assumed closed by omission from LoadAssembly.
            Assert.Equal("reached DbContext", result.ReturnValue);
        }
        else
        {
            // Sandbox holds: clr.AddReference could not resolve an assembly never registered with
            // this engine, even though the process has it loaded elsewhere.
            Assert.NotNull(result.Error);
        }
    }
}
