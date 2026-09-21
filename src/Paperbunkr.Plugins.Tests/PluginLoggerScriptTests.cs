using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins.Tests;

/// <summary>
/// <c>Environment.Log</c> as a plugin author uses it (docs/superpowers/specs/2026-09-20-plugin-api-4-2-followons-design.md
/// §1): a real <c>.csx</c> script through the real <see cref="PluginEngine"/>. File output, rolling and the
/// host's own failure entries are tested against the real writer in <c>Paperbunkr.App.Tests</c>.
/// </summary>
public sealed class PluginLoggerScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "paperbunkr-plugin-logger-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<(FakePluginEnvironment Env, PluginInvocationResult Result)> RunStartup(string script)
    {
        string dir = Path.Combine(_root, "logger");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.xml"), """
            <Plugin key="logger" name="logger">
              <Command hook="Startup" key="logger.startup" name="logger" script="startup.csx" />
            </Plugin>
            """);
        File.WriteAllText(Path.Combine(dir, "startup.csx"), script);

        var env = new FakePluginEnvironment();
        var engine = new PluginEngine();
        engine.Discover(_root, env);
        Assert.False(Assert.Single(engine.AllCommands).IsBroken, "the test script must compile");

        var results = await engine.InvokeAsync(PluginHooks.Startup, e => new StartupHookGlobals { Environment = e });
        return (env, Assert.Single(results));
    }

    [Fact]
    public async Task A_script_can_log_at_every_level()
    {
        var (env, result) = await RunStartup("""
            Environment.Log.Debug("d");
            Environment.Log.Info("i");
            Environment.Log.Warn("w");
            Environment.Log.Error("e");
            return null;
            """);

        Assert.True(result.Success, result.Error?.ToString());
        Assert.Equal(
            new[] { ("Debug", "d"), ("Info", "i"), ("Warn", "w"), ("Error", "e") },
            env.RecordedLog.Entries.Select(e => (e.Level, e.Message)));
    }

    [Fact]
    public async Task An_error_can_carry_an_exception()
    {
        var (env, result) = await RunStartup("""
            try { throw new System.InvalidOperationException("boom"); }
            catch (System.Exception ex) { Environment.Log.Error("it broke", ex); }
            return null;
            """);

        Assert.True(result.Success, result.Error?.ToString());
        var entry = Assert.Single(env.RecordedLog.Entries);
        Assert.Equal("it broke", entry.Message);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }

    [Fact]
    public void The_native_environment_exposes_the_same_logger()
    {
        var inner = new FakePluginEnvironment();
        var native = new FakeNativePluginEnvironment(inner);

        native.Log.Info("from native");

        Assert.Equal("from native", Assert.Single(inner.RecordedLog.Entries).Message);
    }

    [Fact]
    public void The_api_version_includes_the_logger()
    {
        Assert.True(PluginApi.Current >= new Version(4, 2));
    }
}
