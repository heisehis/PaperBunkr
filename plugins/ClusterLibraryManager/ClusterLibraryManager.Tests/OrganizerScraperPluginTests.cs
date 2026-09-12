using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Hooks;

namespace ClusterLibraryManager.Tests;

/// <summary>
/// End-to-end fixture test proving the plugin actually loads through the real host engine
/// (implementation plan Phase 3 Step 3.1's verification criteria: "loads through PluginEngine
/// exactly like Phase 1's fixture did") - not a substitute for
/// <c>Paperbunkr.Plugins.Tests.NativePluginTests</c>, which already covers the engine/loader
/// machinery itself; this is specifically about this plugin's own manifest + assembly being correct.
/// </summary>
public sealed class OrganizerScraperPluginTests
{
    /// <summary>Copies plugin.xml (a real shipped file, referencing the assembly by its relative
    /// filename, as a real installed plugin folder would) alongside the just-built DLL into a fresh
    /// temp folder, mirroring what an actual installed plugin folder looks like.</summary>
    private static string BuildPluginFolder()
    {
        string pluginsRoot = Directory.CreateTempSubdirectory("clm-plugin-load-test-").FullName;
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "plugin.xml"),
            Path.Combine(pluginsRoot, "plugin.xml"));
        File.Copy(
            typeof(OrganizerScraperPlugin).Assembly.Location,
            Path.Combine(pluginsRoot, "ClusterLibraryManager.dll"));
        return pluginsRoot;
    }

    [Fact]
    public void Plugin_loads_and_registers_all_three_of_its_commands()
    {
        string pluginsRoot = BuildPluginFolder();

        var engine = new PluginEngine();
        engine.Discover(pluginsRoot, new FakeNativePluginEnvironment());

        Assert.Equal(3, engine.AllCommands.Count);
        Assert.All(engine.AllCommands, c => Assert.False(c.IsBroken));
        Assert.Contains(engine.AllCommands, c => c.Key == "cluster-library-manager.startup" && c.Hook == PluginHooks.Startup);
        Assert.Contains(engine.AllCommands, c => c.Key == "cluster-library-manager.organize" && c.Hook == PluginHooks.Library);
        Assert.Contains(engine.AllCommands, c => c.Key == "cluster-library-manager.scrape" && c.Hook == PluginHooks.Library);
    }

    [Fact]
    public async Task Startup_command_returns_its_activation_message()
    {
        string pluginsRoot = BuildPluginFolder();

        var engine = new PluginEngine();
        engine.Discover(pluginsRoot, new FakeNativePluginEnvironment());

        var results = await engine.InvokeAsync(PluginHooks.Startup, env => new StartupHookGlobals { Environment = env });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Equal("Cluster Library Manager is active for this session.", result.ReturnValue);
    }
}
