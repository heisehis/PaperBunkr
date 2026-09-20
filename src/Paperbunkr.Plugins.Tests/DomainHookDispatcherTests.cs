using Paperbunkr.Data.Events;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins.Tests;

/// <summary>
/// <see cref="DomainHookDispatcher"/> (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §5.1)
/// against real <c>.csx</c> plugins through a real <see cref="PluginEngine"/>. The claims under test are
/// the ones that protect the app from a bad plugin: fire-and-forget, one invocation at a time per
/// command, a bounded queue that drops the oldest, a cancellation token tripped at the timeout, a hung
/// command getting nothing new until it returns, and each kind of problem reported once per command.
/// Timeouts here are tens of milliseconds - production uses 30 s.
/// </summary>
public sealed class DomainHookDispatcherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "paperbunkr-dispatcher-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<DomainHookProblem> _problems = new();
    private readonly object _problemsGate = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WritePlugin(string key, string hook, string script)
    {
        string dir = Path.Combine(_root, key);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.xml"), $"""
            <Plugin key="{key}" name="{key}">
              <Command hook="{hook}" key="{key}.cmd" name="{key}" script="run.csx" />
            </Plugin>
            """);
        File.WriteAllText(Path.Combine(dir, "run.csx"), script);
    }

    private (PluginEngine Engine, FakePluginEnvironment Env) Discover()
    {
        var env = new FakePluginEnvironment();
        var engine = new PluginEngine();
        engine.Discover(_root, env);
        Assert.All(engine.AllCommands, c => Assert.False(c.IsBroken, c.CompileError));
        return (engine, env);
    }

    private DomainHookDispatcher NewDispatcher(PluginEngine engine, TimeSpan? timeout = null, int capacity = DomainHookDispatcher.DefaultQueueCapacity) =>
        new(engine, p => { lock (_problemsGate) { _problems.Add(p); } }, timeout ?? TimeSpan.FromSeconds(10), capacity);

    private List<DomainHookProblem> Problems()
    {
        lock (_problemsGate)
        {
            return _problems.ToList();
        }
    }

    private static ReadingListChangedHookGlobals Change(IPluginEnvironment env, string name) => new()
    {
        Environment = env,
        ListId = 1,
        ListName = name,
        Kind = ReadingListChangeKind.Added,
        AddedIssueIds = new[] { 1, 2 },
        RemovedIssueIds = Array.Empty<int>(),
    };

    private const string RecordsTheListName = "Environment.Activity.RaiseAlert(PluginAlertSeverity.Info, ListName, Kind.ToString()); return null;";

    // ---- The basics ----

    [Fact]
    public async Task Dispatch_returns_immediately_and_the_plugin_runs_in_the_background()
    {
        // A script that takes ~400 ms. Dispatch must not wait for it.
        WritePlugin("slow", PluginHooks.ReadingListChanged, """
            await Task.Delay(400);
            Environment.Activity.RaiseAlert(PluginAlertSeverity.Info, "done");
            return null;
            """);
        var (engine, env) = Discover();
        var dispatcher = NewDispatcher(engine);

        var started = DateTime.UtcNow;
        dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, "x"));
        var dispatchTook = DateTime.UtcNow - started;

        Assert.True(dispatchTook < TimeSpan.FromMilliseconds(300), $"Dispatch blocked for {dispatchTook.TotalMilliseconds:0} ms");
        Assert.Empty(env.RecordedActivity.Alerts);
        Assert.True(await dispatcher.WaitForIdleAsync(TimeSpan.FromSeconds(10)));
        Assert.Single(env.RecordedActivity.Alerts);
    }

    [Fact]
    public async Task The_typed_payload_reaches_the_script()
    {
        WritePlugin("reader", PluginHooks.ReadingListChanged, """
            Environment.Activity.RaiseAlert(PluginAlertSeverity.Info, ListName + "|" + Kind + "|" + string.Join(",", AddedIssueIds), dedupeKey: "k");
            return "ignored";
            """);
        var (engine, env) = Discover();
        var dispatcher = NewDispatcher(engine);

        dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, "Signal War"));
        Assert.True(await dispatcher.WaitForIdleAsync(TimeSpan.FromSeconds(10)));

        var alert = Assert.Single(env.RecordedActivity.Alerts);
        Assert.Equal("Signal War|Added|1,2", alert.Title);
        Assert.Empty(Problems());   // a return value is simply ignored
    }

    [Fact]
    public async Task A_script_can_test_the_change_kind_flags_by_name()
    {
        // The wiki tells authors to write Kind.HasFlag(ReadingListChangeKind.Removed) - so the enum
        // must be nameable from a script without a using of their own.
        WritePlugin("flags", PluginHooks.ReadingListChanged, """
            Environment.Activity.RaiseAlert(PluginAlertSeverity.Info,
                Kind.HasFlag(ReadingListChangeKind.Added) + "/" + Kind.HasFlag(ReadingListChangeKind.Removed));
            return null;
            """);
        var (engine, env) = Discover();
        var dispatcher = NewDispatcher(engine);

        dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, "x"));   // Kind = Added
        Assert.True(await dispatcher.WaitForIdleAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal("True/False", Assert.Single(env.RecordedActivity.Alerts).Title);
    }

    [Fact]
    public async Task A_command_registered_on_a_different_hook_is_not_run()
    {
        WritePlugin("other", PluginHooks.BookRead, "Environment.Activity.RaiseAlert(PluginAlertSeverity.Info, \"book\"); return null;");
        var (engine, env) = Discover();
        var dispatcher = NewDispatcher(engine);

        dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, "x"));
        Assert.True(await dispatcher.WaitForIdleAsync(TimeSpan.FromSeconds(5)));

        Assert.Empty(env.RecordedActivity.Alerts);
    }

    [Fact]
    public async Task A_disabled_command_is_not_run()
    {
        WritePlugin("off", PluginHooks.ReadingListChanged, RecordsTheListName);
        var (engine, env) = Discover();
        engine.AllCommands.Single().Enabled = false;
        var dispatcher = NewDispatcher(engine);

        dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, "x"));
        Assert.True(await dispatcher.WaitForIdleAsync(TimeSpan.FromSeconds(5)));

        Assert.Empty(env.RecordedActivity.Alerts);
    }

    [Fact]
    public async Task Events_for_one_command_run_one_at_a_time_in_order()
    {
        WritePlugin("ordered", PluginHooks.ReadingListChanged, """
            await Task.Delay(30);
            Environment.Activity.RaiseAlert(PluginAlertSeverity.Info, ListName);
            return null;
            """);
        var (engine, env) = Discover();
        var dispatcher = NewDispatcher(engine);

        foreach (string name in new[] { "A", "B", "C", "D" })
        {
            dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, name));
        }

        Assert.True(await dispatcher.WaitForIdleAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(new[] { "A", "B", "C", "D" }, env.RecordedActivity.Alerts.Select(a => a.Title));
    }

    [Fact]
    public async Task Two_commands_on_the_same_hook_both_run_and_one_failing_does_not_stop_the_other()
    {
        WritePlugin("bad", PluginHooks.ReadingListChanged, "throw new System.InvalidOperationException(\"boom\");");
        WritePlugin("good", PluginHooks.ReadingListChanged, RecordsTheListName);
        var (engine, env) = Discover();
        var dispatcher = NewDispatcher(engine);

        dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, "x"));
        Assert.True(await dispatcher.WaitForIdleAsync(TimeSpan.FromSeconds(10)));

        Assert.Single(env.RecordedActivity.Alerts);      // "good" ran
        Assert.Single(Problems());                        // "bad" was reported
    }

    // ---- Failures are reported once ----

    [Fact]
    public async Task A_failing_command_is_reported_once_however_many_events_it_fails_on()
    {
        WritePlugin("bad", PluginHooks.ReadingListChanged, "throw new System.InvalidOperationException(\"boom\");");
        var (engine, _) = Discover();
        var dispatcher = NewDispatcher(engine);

        for (int i = 0; i < 5; i++)
        {
            dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, "x"));
        }

        Assert.True(await dispatcher.WaitForIdleAsync(TimeSpan.FromSeconds(10)));
        var problem = Assert.Single(Problems());
        Assert.Equal(DomainHookProblemKind.Failed, problem.Kind);
        Assert.Contains("boom", problem.Message);
        Assert.Equal("bad.cmd", problem.Command.Key);
        Assert.Equal(PluginHooks.ReadingListChanged, problem.Hook);
    }

    [Fact]
    public async Task A_throwing_problem_callback_never_reaches_the_dispatcher()
    {
        WritePlugin("bad", PluginHooks.ReadingListChanged, "throw new System.InvalidOperationException(\"boom\");");
        var (engine, env) = Discover();
        var dispatcher = new DomainHookDispatcher(engine, _ => throw new InvalidOperationException("reporting failed"), TimeSpan.FromSeconds(10));

        dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, "x"));

        Assert.True(await dispatcher.WaitForIdleAsync(TimeSpan.FromSeconds(10)));
        Assert.Empty(env.RecordedActivity.Alerts);
    }

    // ---- Timeout and cancellation ----

    [Fact]
    public async Task A_cooperative_plugin_is_cancelled_at_the_timeout_and_reported_once_as_timed_out()
    {
        // Honours the token: the wait ends the moment it's tripped.
        WritePlugin("patient", PluginHooks.ReadingListChanged, """
            Environment.Activity.RaiseAlert(PluginAlertSeverity.Info, "started");
            await Task.Delay(System.Threading.Timeout.Infinite, CancellationToken);
            return null;
            """);
        var (engine, env) = Discover();
        var dispatcher = NewDispatcher(engine, timeout: TimeSpan.FromMilliseconds(150));

        dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, "x"));

        Assert.True(await dispatcher.WaitForIdleAsync(TimeSpan.FromSeconds(10)), "the cancelled lane should go idle");
        Assert.Single(env.RecordedActivity.Alerts);
        var problem = Assert.Single(Problems());
        Assert.Equal(DomainHookProblemKind.TimedOut, problem.Kind);
        Assert.False(dispatcher.IsHung(engine.AllCommands.Single()));
    }

    [Fact]
    public async Task The_cancellation_token_is_not_tripped_when_the_plugin_finishes_in_time()
    {
        WritePlugin("quick", PluginHooks.ReadingListChanged, """
            Environment.Activity.RaiseAlert(PluginAlertSeverity.Info, CancellationToken.IsCancellationRequested ? "cancelled" : "fine");
            return null;
            """);
        var (engine, env) = Discover();
        var dispatcher = NewDispatcher(engine, timeout: TimeSpan.FromSeconds(5));

        dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, "x"));
        Assert.True(await dispatcher.WaitForIdleAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal("fine", Assert.Single(env.RecordedActivity.Alerts).Title);
        Assert.Empty(Problems());
    }

    // ---- A plugin that ignores the token: hung + bounded queue ----

    private const string IgnoresTheTokenAndBlocks = """
        Environment.Activity.RaiseAlert(PluginAlertSeverity.Info, ListName);
        System.Threading.Thread.Sleep(700);
        return null;
        """;

    [Fact]
    public async Task A_command_that_ignores_the_token_is_hung_and_gets_no_new_invocation_until_it_returns()
    {
        WritePlugin("stubborn", PluginHooks.ReadingListChanged, IgnoresTheTokenAndBlocks);
        var (engine, env) = Discover();
        var command = engine.AllCommands.Single();
        var dispatcher = NewDispatcher(engine, timeout: TimeSpan.FromMilliseconds(100));

        dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, "first"));
        await Task.Delay(50);                                    // it is now running
        dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, "second"));

        // Past the 100 ms timeout, still inside the 700 ms sleep: hung, and "second" hasn't started.
        await Task.Delay(300);
        Assert.True(dispatcher.IsHung(command));
        Assert.Equal(new[] { "first" }, env.RecordedActivity.Alerts.Select(a => a.Title));
        Assert.Equal(1, dispatcher.QueuedCount(command));

        Assert.True(await dispatcher.WaitForIdleAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(new[] { "first", "second" }, env.RecordedActivity.Alerts.Select(a => a.Title));
        Assert.False(dispatcher.IsHung(command));
    }

    [Fact]
    public async Task The_queue_is_bounded_it_drops_the_oldest_and_reports_that_once()
    {
        WritePlugin("stubborn", PluginHooks.ReadingListChanged, IgnoresTheTokenAndBlocks);
        var (engine, env) = Discover();
        var command = engine.AllCommands.Single();
        var dispatcher = NewDispatcher(engine, timeout: TimeSpan.FromSeconds(30), capacity: 2);

        dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, "e1"));
        await Task.Delay(100);                                   // e1 is running
        foreach (string name in new[] { "e2", "e3", "e4", "e5" })
        {
            dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, name));
        }

        // Capacity 2: e2 and e3 were dropped as e4 and e5 arrived - oldest first.
        Assert.Equal(2, dispatcher.QueuedCount(command));
        var dropped = Assert.Single(Problems());
        Assert.Equal(DomainHookProblemKind.EventsDropped, dropped.Kind);

        Assert.True(await dispatcher.WaitForIdleAsync(TimeSpan.FromSeconds(20)));
        Assert.Equal(new[] { "e1", "e4", "e5" }, env.RecordedActivity.Alerts.Select(a => a.Title));
        Assert.Single(Problems());                               // reported once, not once per drop
    }

    [Fact]
    public async Task A_hung_plugin_costs_one_lane_not_a_thread_per_event()
    {
        // 200 events at a plugin that blocks: only the first runs, at most `capacity` wait, the rest are
        // dropped. The invocation count is what proves it never fanned out onto the thread pool.
        WritePlugin("stubborn", PluginHooks.ReadingListChanged, IgnoresTheTokenAndBlocks);
        var (engine, env) = Discover();
        var command = engine.AllCommands.Single();
        var dispatcher = NewDispatcher(engine, timeout: TimeSpan.FromSeconds(30), capacity: 3);

        dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, "first"));
        await Task.Delay(100);
        for (int i = 0; i < 200; i++)
        {
            dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, "burst"));
        }

        Assert.Equal(3, dispatcher.QueuedCount(command));
        Assert.Single(env.RecordedActivity.Alerts);              // still only "first" has started

        Assert.True(await dispatcher.WaitForIdleAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(1 + 3, env.RecordedActivity.Alerts.Count);
    }

    [Fact]
    public async Task A_different_commands_queue_is_independent_of_a_hung_one()
    {
        WritePlugin("stubborn", PluginHooks.ReadingListChanged, IgnoresTheTokenAndBlocks);
        WritePlugin("healthy", PluginHooks.ReadingListChanged, """
            Environment.Activity.RaiseAlert(PluginAlertSeverity.Info, "healthy:" + ListName);
            return null;
            """);
        var (engine, env) = Discover();
        var dispatcher = NewDispatcher(engine, timeout: TimeSpan.FromSeconds(30));

        dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, "one"));
        await Task.Delay(100);
        dispatcher.Dispatch(PluginHooks.ReadingListChanged, e => Change(e, "two"));

        // The healthy command finishes both events while the stubborn one is still sleeping.
        await Task.Delay(300);
        Assert.Equal(2, env.RecordedActivity.Alerts.Count(a => a.Title.StartsWith("healthy:")));

        Assert.True(await dispatcher.WaitForIdleAsync(TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void The_defaults_are_the_documented_ones()
    {
        Assert.Equal(16, DomainHookDispatcher.DefaultQueueCapacity);
        Assert.Equal(TimeSpan.FromSeconds(30), DomainHookDispatcher.DefaultTimeout);
    }
}
