using MinimalNative;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Hooks;
using Paperbunkr.Plugins.Native;

namespace Paperbunkr.Plugins.Tests;

/// <summary>
/// End-to-end fixture test for the native plugin tier (docs/superpowers/specs/2026-09-11-plugin-api-
/// v4-native-tier-design.md, implementation plan Phase 1 Step 1.3/1.4) - proves a real compiled
/// assembly loads through <see cref="PluginLoadContext"/>, its registered commands are
/// indistinguishable from scripted ones once inside <see cref="PluginEngine"/>, and native + scripted
/// commands coexist in the same discovery pass.
/// </summary>
public sealed class NativePluginTests
{
    private static string MinimalNativeAssemblyPath => typeof(MinimalNativePlugin).Assembly.Location;

    /// <summary>Writes a fresh `plugin.xml` pointing `assembly` at the fixture's real, already-built
    /// DLL path (absolute paths work fine here - <c>Path.Combine</c> with a rooted second argument
    /// just returns it, matching how <see cref="PluginEngine"/>'s production `Path.Combine(pluginDir,
    /// manifest.Assembly)` call already behaves) - avoids depending on fragile build-output-copying
    /// conventions to line up a compiled fixture DLL with a manifest file.</summary>
    private static string WriteNativeManifest(string? scriptPluginDir = null)
    {
        string dir = Directory.CreateTempSubdirectory("clm-native-test-").FullName;
        string xml =
            $"""
             <Plugin key="minimal-native" name="Minimal Native" tier="Native" assembly="{MinimalNativeAssemblyPath}">
             </Plugin>
             """;
        File.WriteAllText(Path.Combine(dir, "plugin.xml"), xml);

        if (scriptPluginDir is not null)
        {
            // Also copy a real scripted plugin.xml + .csx alongside, to prove native and scripted
            // commands both surface from the same Discover() pass.
            string scriptDir = Path.Combine(dir, "script-sibling");
            Directory.CreateDirectory(scriptDir);
            foreach (string file in Directory.GetFiles(scriptPluginDir))
            {
                File.Copy(file, Path.Combine(scriptDir, Path.GetFileName(file)));
            }
        }

        return dir;
    }

    [Fact]
    public void Native_plugin_loads_and_registers_its_commands()
    {
        string pluginsRoot = WriteNativeManifest();
        var engine = new PluginEngine();

        engine.Discover(pluginsRoot, new FakeNativePluginEnvironment());

        Assert.Equal(3, engine.AllCommands.Count);
        Assert.All(engine.AllCommands, c => Assert.False(c.IsBroken));
        Assert.Contains(engine.AllCommands, c => c.Key == "minimal-native.startup" && c.Hook == PluginHooks.Startup);
        Assert.Contains(engine.AllCommands, c => c.Key == "minimal-native.count-books" && c.Hook == PluginHooks.Library);
    }

    [Fact]
    public async Task Native_startup_command_invokes_through_the_normal_engine_path()
    {
        string pluginsRoot = WriteNativeManifest();
        var engine = new PluginEngine();
        engine.Discover(pluginsRoot, new FakeNativePluginEnvironment());

        var results = await engine.InvokeAsync(PluginHooks.Startup, env => new StartupHookGlobals { Environment = env });

        var result = Assert.Single(results.Where(r => r.Command.Key == "minimal-native.startup"));
        Assert.True(result.Success);
        Assert.Equal("native-startup-ran", result.ReturnValue);
    }

    [Fact]
    public async Task Native_library_command_receives_the_right_clicked_books()
    {
        string pluginsRoot = WriteNativeManifest();
        var engine = new PluginEngine();
        engine.Discover(pluginsRoot, new FakeNativePluginEnvironment());

        var books = new List<Issue> { new() { Id = 1, SeriesId = 1 }, new() { Id = 2, SeriesId = 1 } };
        var results = await engine.InvokeAsync(PluginHooks.Library, env => new BooksHookGlobals { Environment = env, Books = books });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Equal(2, result.ReturnValue);
    }

    [Fact]
    public async Task Initialize_runs_once_per_plugin_before_any_command_is_invokable()
    {
        // Observed through a registered command's return value, not a static field - a plugin loaded
        // into its own AssemblyLoadContext gets its own independent copy of its types/statics,
        // separate from any copy already loaded elsewhere (e.g. this test directly referencing
        // MinimalNative for typeof(...) convenience), so a static field can't be read across that
        // boundary. This is real isolation, not a bug - found the hard way via a failing test.
        string pluginsRoot = WriteNativeManifest();
        var engine = new PluginEngine();
        engine.Discover(pluginsRoot, new FakeNativePluginEnvironment());

        var results = await engine.InvokeAsync(PluginHooks.Startup, env => new StartupHookGlobals { Environment = env });

        var result = Assert.Single(results.Where(r => r.Command.Key == "minimal-native.was-initialized"));
        Assert.True(result.Success);
        Assert.Equal(true, result.ReturnValue);
    }

    [Fact]
    public void A_manifest_environment_that_is_not_native_capable_contributes_no_commands()
    {
        // A base IPluginEnvironment (what .csx scripts get) doesn't implement
        // INativePluginEnvironment - discovery must skip the native plugin silently rather than throw,
        // matching every other "can't load this one" case in Discover.
        string pluginsRoot = WriteNativeManifest();
        var engine = new PluginEngine();

        engine.Discover(pluginsRoot, new FakePluginEnvironment());

        Assert.Empty(engine.AllCommands);
    }

    [Fact]
    public void Native_and_scripted_commands_coexist_in_the_same_discovery_pass()
    {
        string franchiseToolsDir = Path.Combine(AppContext.BaseDirectory, "SamplePlugins", "FranchiseTools");
        string pluginsRoot = WriteNativeManifest(franchiseToolsDir);
        var engine = new PluginEngine();

        engine.Discover(pluginsRoot, new FakeNativePluginEnvironment());

        // 3 native (startup, was-initialized, library) + 3 scripted (startup, tag-series-family, rated-not-checked).
        Assert.Equal(6, engine.AllCommands.Count);
        Assert.Contains(engine.AllCommands, c => c.Key == "minimal-native.startup");
        Assert.Contains(engine.AllCommands, c => c.Key == "franchise-tools.startup");
    }
}
