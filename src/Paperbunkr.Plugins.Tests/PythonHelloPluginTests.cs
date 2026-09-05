using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins.Tests;

/// <summary>
/// End-to-end fixture test against the real "Python Hello" sample plugin (docs/superpowers/specs/
/// 2026-08-30-python-plugin-scripting-design.md) - the Python counterpart of
/// <see cref="DuplicateFinderPluginTests"/>. Discovers and precompiles it for real via
/// <see cref="PluginEngine"/>, proving the whole manifest → dispatch → precompile → invoke path
/// works for a <see cref="PythonCommand"/>, not just the class in isolation.
///
/// <see cref="PluginsRoot"/> is scoped to this plugin's own subfolder (not the shared
/// <c>SamplePlugins</c> root <see cref="DuplicateFinderPluginTests"/> uses) so this fixture's
/// discovery can't change that other test's command count.
/// </summary>
public sealed class PythonHelloPluginTests
{
    private static string PluginsRoot => Path.Combine(AppContext.BaseDirectory, "SamplePlugins", "PythonHello");

    [Fact]
    public async Task Startup_hook_runs_through_the_real_engine_and_returns_its_message()
    {
        var engine = new PluginEngine();
        engine.Discover(PluginsRoot, new FakePluginEnvironment());

        Command cmd = Assert.Single(engine.AllCommands);
        Assert.IsType<PythonCommand>(cmd);
        Assert.False(cmd.IsBroken);

        var results = await engine.InvokeAsync(PluginHooks.Startup, env => new StartupHookGlobals { Environment = env });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Contains("active", (string)result.ReturnValue!);
    }
}
