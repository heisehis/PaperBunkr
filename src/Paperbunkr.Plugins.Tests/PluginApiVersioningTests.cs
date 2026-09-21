using MinimalNative;
using Paperbunkr.Plugins.Hooks;
using ThrowingNative;

namespace Paperbunkr.Plugins.Tests;

/// <summary>
/// Engine-level tests for <c>requiresApi</c> (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md
/// §3.2): a major mismatch (either direction) or malformed value is blocked before any code is
/// compiled or loaded, a same-major minor difference loads and runs, and the version only ever shows
/// up as a hint on a failure. The host here is the real <see cref="PluginApi.Current"/> (4.x), so
/// "higher minor" fixtures use 4.9 and "other major" fixtures use 5.0 and 3.9.
/// </summary>
public sealed class PluginApiVersioningTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "paperbunkr-plugin-api-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteScriptPlugin(string key, string? requiresApi, string script)
    {
        string dir = Path.Combine(_root, key);
        Directory.CreateDirectory(dir);
        string attribute = requiresApi is null ? string.Empty : $" requiresApi=\"{requiresApi}\"";
        File.WriteAllText(Path.Combine(dir, "plugin.xml"), $"""
            <Plugin key="{key}" name="{key}"{attribute}>
              <Command hook="Startup" key="{key}.startup" name="{key}" script="startup.csx" />
            </Plugin>
            """);
        File.WriteAllText(Path.Combine(dir, "startup.csx"), script);
    }

    private void WriteNativePlugin(string key, string? requiresApi, string assemblyPath)
    {
        string dir = Path.Combine(_root, key);
        Directory.CreateDirectory(dir);
        string attribute = requiresApi is null ? string.Empty : $" requiresApi=\"{requiresApi}\"";
        File.WriteAllText(Path.Combine(dir, "plugin.xml"), $"""
            <Plugin key="{key}" name="{key}" tier="Native" assembly="{assemblyPath}"{attribute}>
            </Plugin>
            """);
    }

    private PluginEngine DiscoverScripts()
    {
        var engine = new PluginEngine();
        engine.Discover(_root, new FakePluginEnvironment());
        return engine;
    }

    private const string InvalidCSharp = "this is not valid C#;;;";

    // ---- Script tier: the gate ----

    [Theory]
    [InlineData("5.0")]
    [InlineData("3.9")]
    [InlineData("banana")]
    [InlineData("4.1.2")]
    public void Script_plugin_with_a_blocking_requiresApi_registers_nothing_and_is_never_compiled(string requiresApi)
    {
        // The script is invalid C# on purpose: if the engine had compiled it, a *broken* command would
        // be sitting in AllCommands. Its total absence is the proof that nothing was compiled.
        WriteScriptPlugin("future", requiresApi, InvalidCSharp);
        WriteScriptPlugin("healthy", null, "return \"ok\";");

        PluginEngine engine = DiscoverScripts();

        Assert.DoesNotContain(engine.AllCommands, c => c.PluginKey == "future");
        Command healthy = Assert.Single(engine.AllCommands);
        Assert.Equal("healthy", healthy.PluginKey);
        Assert.False(healthy.IsBroken);

        PluginApiInfo info = engine.PackageApiInfo["future"];
        Assert.NotNull(info.BlockedReason);
        Assert.Equal(requiresApi, info.RequiresApi);
        Assert.Null(engine.PackageApiInfo["healthy"].BlockedReason);
    }

    [Fact]
    public void Script_plugin_with_a_major_mismatch_says_why()
    {
        WriteScriptPlugin("future", "5.0", "return 1;");

        PluginEngine engine = DiscoverScripts();

        string reason = engine.PackageApiInfo["future"].BlockedReason!;
        Assert.Contains("plugin requires API 5.0", reason);
        Assert.Contains("major version mismatch", reason);
    }

    [Fact]
    public async Task Script_plugin_with_a_higher_minor_is_lenient_and_runs()
    {
        WriteScriptPlugin("newer", "4.9", "return \"ran\";");

        PluginEngine engine = DiscoverScripts();

        Command cmd = Assert.Single(engine.AllCommands);
        Assert.False(cmd.IsBroken);
        Assert.Equal(new Version(4, 9), cmd.DeclaredApi);
        Assert.Null(engine.PackageApiInfo["newer"].BlockedReason);

        var results = await engine.InvokeAsync(PluginHooks.Startup, env => new StartupHookGlobals { Environment = env });
        PluginInvocationResult result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Equal("ran", result.ReturnValue);
    }

    [Fact]
    public void Script_plugin_with_no_requiresApi_is_the_baseline()
    {
        WriteScriptPlugin("legacy", null, "return 1;");

        PluginEngine engine = DiscoverScripts();

        Command cmd = Assert.Single(engine.AllCommands);
        Assert.Null(cmd.DeclaredApi);
        PluginApiInfo info = engine.PackageApiInfo["legacy"];
        Assert.Null(info.RequiresApi);
        Assert.Null(info.Declared);
        Assert.Null(info.BlockedReason);
    }

    [Fact]
    public void Package_api_info_is_reset_by_the_next_discover()
    {
        WriteScriptPlugin("gone", "5.0", "return 1;");
        var engine = new PluginEngine();
        engine.Discover(_root, new FakePluginEnvironment());
        Assert.True(engine.PackageApiInfo.ContainsKey("gone"));

        Directory.Delete(Path.Combine(_root, "gone"), recursive: true);
        engine.Discover(_root, new FakePluginEnvironment());

        Assert.Empty(engine.PackageApiInfo);
    }

    // ---- Script tier: hints on a compile failure ----

    [Fact]
    public void A_compile_error_in_a_higher_minor_plugin_gets_the_version_hint()
    {
        WriteScriptPlugin("newer", "4.9", InvalidCSharp);

        Command cmd = Assert.Single(DiscoverScripts().AllCommands);

        Assert.True(cmd.IsBroken);
        Assert.Contains("plugin declares API 4.9, this app provides", cmd.CompileError);
    }

    [Fact]
    public void A_compile_error_in_a_plugin_that_declares_nothing_gets_no_hint()
    {
        WriteScriptPlugin("legacy", null, InvalidCSharp);

        Command cmd = Assert.Single(DiscoverScripts().AllCommands);

        Assert.True(cmd.IsBroken);
        Assert.DoesNotContain("this app provides", cmd.CompileError);
    }

    [Fact]
    public void A_compile_error_in_a_plugin_declaring_the_baseline_gets_no_hint()
    {
        WriteScriptPlugin("baseline", "4.0", InvalidCSharp);

        Command cmd = Assert.Single(DiscoverScripts().AllCommands);

        Assert.True(cmd.IsBroken);
        Assert.DoesNotContain("this app provides", cmd.CompileError);
    }

    // ---- Script tier: hints on an invoke-time failure ----

    private async Task<(PluginInvocationResult Result, Command Command)> InvokeStartup(string key, string? requiresApi, string script)
    {
        WriteScriptPlugin(key, requiresApi, script);
        PluginEngine engine = DiscoverScripts();
        var results = await engine.InvokeAsync(PluginHooks.Startup, env => new StartupHookGlobals { Environment = env });
        return (Assert.Single(results), Assert.Single(engine.AllCommands));
    }

    private const string ThrowsMissingMethod = "throw new System.MissingMethodException(\"IPluginActivity\", \"StartJob\");";

    [Fact]
    public async Task A_missing_member_in_a_higher_minor_plugin_reports_the_version_hint_without_disabling_it()
    {
        var (result, command) = await InvokeStartup("newer", "4.9", ThrowsMissingMethod);

        Assert.False(result.Success);
        PluginApiMismatchException wrapped = Assert.IsType<PluginApiMismatchException>(result.Error);
        Assert.Contains("plugin declares API 4.9", wrapped.Message);
        Assert.IsType<MissingMethodException>(wrapped.InnerException);

        // One bad call must never silently turn a command off.
        Assert.True(command.Enabled);
        Assert.False(command.IsBroken);
    }

    [Fact]
    public async Task A_missing_member_in_a_plugin_that_declares_nothing_suggests_declaring_a_version()
    {
        var (result, _) = await InvokeStartup("legacy", null, ThrowsMissingMethod);

        PluginApiMismatchException wrapped = Assert.IsType<PluginApiMismatchException>(result.Error);
        Assert.Contains("declares no requiresApi", wrapped.Message);
    }

    [Fact]
    public async Task A_missing_member_in_a_plugin_declaring_the_baseline_is_not_wrapped()
    {
        var (result, _) = await InvokeStartup("baseline", "4.0", ThrowsMissingMethod);

        Assert.False(result.Success);
        Assert.IsType<MissingMethodException>(result.Error);
    }

    [Fact]
    public async Task An_ordinary_exception_is_never_wrapped_even_in_a_higher_minor_plugin()
    {
        var (result, _) = await InvokeStartup("newer", "4.9", "throw new System.InvalidOperationException(\"boom\");");

        Assert.False(result.Success);
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.Contains("boom", result.Error!.Message);
    }

    // ---- Native tier ----

    private static string MinimalNativeAssemblyPath => typeof(MinimalNativePlugin).Assembly.Location;

    private static string ThrowingNativeAssemblyPath => typeof(ThrowingNativePlugin).Assembly.Location;

    private PluginEngine DiscoverNative()
    {
        var engine = new PluginEngine();
        engine.Discover(_root, new FakeNativePluginEnvironment());
        return engine;
    }

    [Theory]
    [InlineData("5.0")]
    [InlineData("3.0")]
    [InlineData("nope")]
    public void Native_plugin_with_a_blocking_requiresApi_is_never_loaded(string requiresApi)
    {
        WriteNativePlugin("native-future", requiresApi, MinimalNativeAssemblyPath);

        PluginEngine engine = DiscoverNative();

        // MinimalNative registers three commands when it loads. None appearing, and no module, is
        // what "LoadPlugin was never called" looks like from the outside.
        Assert.Empty(engine.AllCommands);
        NativePluginLoadResult loadResult = engine.NativeLoadResults["native-future"];
        Assert.Null(loadResult.Module);
        Assert.NotNull(loadResult.LoadError);
        Assert.Equal(loadResult.LoadError, engine.PackageApiInfo["native-future"].BlockedReason);
    }

    [Fact]
    public void Native_plugin_with_a_major_mismatch_says_why()
    {
        WriteNativePlugin("native-future", "5.0", MinimalNativeAssemblyPath);

        string? error = DiscoverNative().NativeLoadResults["native-future"].LoadError;

        Assert.Contains("plugin requires API 5.0", error);
        Assert.Contains("major version mismatch", error);
    }

    [Fact]
    public void Native_plugin_with_a_higher_minor_is_lenient_and_loads()
    {
        WriteNativePlugin("native-newer", "4.9", MinimalNativeAssemblyPath);

        PluginEngine engine = DiscoverNative();

        Assert.Equal(3, engine.AllCommands.Count);
        Assert.All(engine.AllCommands, c =>
        {
            Assert.False(c.IsBroken);
            Assert.Equal(new Version(4, 9), c.DeclaredApi);
        });
        Assert.NotNull(engine.NativeLoadResults["native-newer"].Module);
    }

    [Fact]
    public void A_native_load_failure_in_a_higher_minor_plugin_gets_the_version_hint()
    {
        WriteNativePlugin("native-throws", "4.9", ThrowingNativeAssemblyPath);

        string? error = DiscoverNative().NativeLoadResults["native-throws"].LoadError;

        Assert.NotNull(error);
        Assert.Contains("plugin declares API 4.9, this app provides", error);
    }

    [Fact]
    public void An_ordinary_native_load_failure_in_a_plugin_that_declares_nothing_gets_no_hint()
    {
        WriteNativePlugin("native-throws", null, ThrowingNativeAssemblyPath);

        string? error = DiscoverNative().NativeLoadResults["native-throws"].LoadError;

        Assert.NotNull(error);
        Assert.DoesNotContain("this app provides", error);
    }
}
