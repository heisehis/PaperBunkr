using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins.Tests;

/// <summary>
/// The Activity reporter as a plugin author actually uses it (docs/superpowers/specs/2026-09-20-
/// plugin-api-4-1-design.md §4): a real <c>.csx</c> script driving <c>Environment.Activity</c> through
/// the real <see cref="PluginEngine"/>. The Python path is covered by <see cref="PythonHelloPluginTests"/>.
/// The host-side adapter (job kind, title prefix, alert mapping, dedupe scoping) is tested against a
/// real <c>ActivityService</c> in <c>Paperbunkr.App.Tests</c>.
/// </summary>
public sealed class PluginActivityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "paperbunkr-plugin-activity-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteStartupPlugin(string key, string script)
    {
        string dir = Path.Combine(_root, key);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.xml"), $"""
            <Plugin key="{key}" name="{key}">
              <Command hook="Startup" key="{key}.startup" name="{key}" script="startup.csx" />
            </Plugin>
            """);
        File.WriteAllText(Path.Combine(dir, "startup.csx"), script);
    }

    private async Task<(FakePluginEnvironment Env, PluginInvocationResult Result)> RunStartup(string script)
    {
        WriteStartupPlugin("reporter", script);
        var env = new FakePluginEnvironment();
        var engine = new PluginEngine();
        engine.Discover(_root, env);
        Assert.False(Assert.Single(engine.AllCommands).IsBroken, "the test script must compile");

        var results = await engine.InvokeAsync(PluginHooks.Startup, e => new StartupHookGlobals { Environment = e });
        return (env, Assert.Single(results));
    }

    [Fact]
    public void The_api_version_is_4_2_now_that_the_logger_exists()
    {
        Assert.Equal(new Version(4, 2), PluginApi.Current);
    }

    [Fact]
    public async Task A_script_can_run_a_job_end_to_end()
    {
        var (env, result) = await RunStartup("""
            var job = Environment.Activity.StartJob("Syncing", cancellable: false);
            job.Report(1, 4, "one of four");
            job.Report("nearly there");
            job.Succeed("done");
            job.Dispose();
            return "ok";
            """);

        Assert.True(result.Success, result.Error?.ToString());
        var job = Assert.Single(env.RecordedActivity.Jobs);
        Assert.Equal("Syncing", job.Title);
        Assert.False(job.Cancellable);
        Assert.Equal(new[] { "1/4 one of four", "nearly there" }, job.Reports);
        Assert.Equal("Succeeded: done", job.Outcome);
        Assert.True(job.Disposed);
    }

    [Fact]
    public async Task A_script_can_fail_a_job()
    {
        var (env, result) = await RunStartup("""
            var job = Environment.Activity.StartJob("Fetching");
            job.Fail("server unreachable");
            job.Dispose();
            return null;
            """);

        Assert.True(result.Success, result.Error?.ToString());
        Assert.Equal("Failed: server unreachable", Assert.Single(env.RecordedActivity.Jobs).Outcome);
    }

    [Fact]
    public async Task A_job_is_cancellable_by_default()
    {
        var (env, _) = await RunStartup("""
            Environment.Activity.StartJob("Default").Dispose();
            return null;
            """);

        Assert.True(Assert.Single(env.RecordedActivity.Jobs).Cancellable);
    }

    [Fact]
    public async Task A_script_can_raise_alerts_of_every_severity_with_and_without_a_dedupe_key()
    {
        var (env, result) = await RunStartup("""
            Environment.Activity.RaiseAlert(PluginAlertSeverity.Info, "fyi");
            Environment.Activity.RaiseAlert(PluginAlertSeverity.Warning, "careful", "some detail", "warn-key");
            Environment.Activity.RaiseAlert(PluginAlertSeverity.Error, "broken", dedupeKey: "err-key");
            return null;
            """);

        Assert.True(result.Success, result.Error?.ToString());
        var alerts = env.RecordedActivity.Alerts;
        Assert.Equal(3, alerts.Count);
        Assert.Equal(new FakePluginActivity.FakeAlert(PluginAlertSeverity.Info, "fyi", null, null), alerts[0]);
        Assert.Equal(new FakePluginActivity.FakeAlert(PluginAlertSeverity.Warning, "careful", "some detail", "warn-key"), alerts[1]);
        Assert.Equal(new FakePluginActivity.FakeAlert(PluginAlertSeverity.Error, "broken", null, "err-key"), alerts[2]);
    }

    [Fact]
    public async Task The_native_environment_exposes_the_same_reporter()
    {
        var inner = new FakePluginEnvironment();
        var native = new FakeNativePluginEnvironment(inner);

        var job = native.Activity.StartJob("From native");
        job.Succeed("ok");

        Assert.Equal("From native", Assert.Single(inner.RecordedActivity.Jobs).Title);
        await Task.CompletedTask;
    }
}
