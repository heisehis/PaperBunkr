using Paperbunkr.Data.Events;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins.Tests;

/// <summary>
/// Command telemetry (docs/superpowers/specs/2026-09-20-plugin-api-4-2-followons-design.md §2): the stats
/// arithmetic and wording, and that they're really recorded by running real <c>.csx</c> commands through the
/// engine and the domain-hook dispatcher.
/// </summary>
public sealed class CommandStatsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "paperbunkr-command-stats-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    // ---- the arithmetic ----

    [Fact]
    public void A_new_command_has_no_data_and_says_nothing()
    {
        var snapshot = new CommandStats().Snapshot();

        Assert.False(snapshot.HasData);
        Assert.Null(snapshot.Describe());
        Assert.Equal(TimeSpan.Zero, snapshot.Average);
    }

    [Fact]
    public void Runs_accumulate_into_count_total_average_max_and_last()
    {
        var stats = new CommandStats();
        stats.RecordRun(Ms(100), failed: false);
        stats.RecordRun(Ms(300), failed: false);
        stats.RecordRun(Ms(50), failed: false);

        var s = stats.Snapshot();
        Assert.Equal(3, s.Runs);
        Assert.Equal(Ms(450), s.Total);
        Assert.Equal(Ms(150), s.Average);
        Assert.Equal(Ms(300), s.Max);
        Assert.Equal(Ms(50), s.Last);
        Assert.Equal(0, s.Failures);
    }

    [Fact]
    public void A_failed_run_counts_as_a_run_and_a_failure()
    {
        var stats = new CommandStats();
        stats.RecordRun(Ms(10), failed: true);
        stats.RecordRun(Ms(10), failed: false);

        var s = stats.Snapshot();
        Assert.Equal(2, s.Runs);
        Assert.Equal(1, s.Failures);
    }

    [Fact]
    public void Timeouts_and_drops_are_counted_separately_from_runs()
    {
        var stats = new CommandStats();
        stats.RecordTimeout();
        stats.RecordDropped();
        stats.RecordDropped();

        var s = stats.Snapshot();
        Assert.Equal(0, s.Runs);
        Assert.Equal(1, s.TimedOut);
        Assert.Equal(2, s.Dropped);
        Assert.True(s.HasData);
    }

    [Fact]
    public void Concurrent_recording_loses_nothing()
    {
        var stats = new CommandStats();

        Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < 500; i++)
            {
                stats.RecordRun(Ms(1), failed: i % 10 == 0);
                stats.RecordDropped();
            }
        });

        var s = stats.Snapshot();
        Assert.Equal(4000, s.Runs);
        Assert.Equal(400, s.Failures);
        Assert.Equal(4000, s.Dropped);
        Assert.Equal(Ms(4000), s.Total);
    }

    // ---- the wording ----

    [Theory]
    [InlineData(1, 45, 45, 0, 0, 0, "1 run · avg 45 ms · max 45 ms")]
    [InlineData(12, 45, 210, 1, 0, 0, "12 runs · avg 45 ms · max 210 ms · 1 failed")]
    [InlineData(12, 45, 210, 1, 1, 2, "12 runs · avg 45 ms · max 210 ms · 1 failed · 1 timed out · 2 dropped")]
    public void Describe_names_only_the_parts_that_apply(long runs, double avgMs, double maxMs, long failures, long timedOut, long dropped, string expected)
    {
        var s = new CommandStatsSnapshot(runs, failures, timedOut, dropped, Ms(avgMs * runs), Ms(maxMs), Ms(avgMs));

        Assert.Equal(expected, s.Describe());
    }

    [Fact]
    public void Describe_still_reports_a_timeout_or_drop_for_a_command_that_never_completed_a_run()
    {
        var s = new CommandStatsSnapshot(0, 0, 1, 3, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

        Assert.Equal("1 timed out · 3 dropped", s.Describe());
    }

    [Theory]
    [InlineData(45, "45 ms")]
    [InlineData(999, "999 ms")]
    [InlineData(1000, "1.0 s")]
    [InlineData(12500, "12.5 s")]
    [InlineData(0.4, "0 ms")]
    public void Durations_read_naturally(double ms, string expected)
    {
        Assert.Equal(expected, CommandStatsSnapshot.Format(Ms(ms)));
    }

    // ---- recorded by real commands ----

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

    private PluginEngine Discover()
    {
        var engine = new PluginEngine();
        engine.Discover(_root, new FakePluginEnvironment());
        Assert.All(engine.AllCommands, c => Assert.False(c.IsBroken, c.CompileError));
        return engine;
    }

    [Fact]
    public async Task A_command_run_through_the_engine_is_timed()
    {
        WritePlugin("slowish", PluginHooks.Startup, "await Task.Delay(80); return 1;");
        PluginEngine engine = Discover();

        await engine.InvokeAsync(PluginHooks.Startup, e => new StartupHookGlobals { Environment = e });

        var s = engine.AllCommands.Single().Stats.Snapshot();
        Assert.Equal(1, s.Runs);
        Assert.Equal(0, s.Failures);
        Assert.True(s.Last >= Ms(60), $"expected the ~80 ms delay to be measured, got {s.Last.TotalMilliseconds:0} ms");
        Assert.Equal(s.Last, s.Max);
    }

    [Fact]
    public async Task Every_hook_is_covered_not_just_the_domain_ones()
    {
        WritePlugin("startup", PluginHooks.Startup, "return 1;");
        WritePlugin("shutdown", PluginHooks.Shutdown, "return 1;");
        PluginEngine engine = Discover();

        await engine.InvokeAsync(PluginHooks.Startup, e => new StartupHookGlobals { Environment = e });
        await engine.InvokeAsync(PluginHooks.Shutdown, e => new ShutdownHookGlobals { Environment = e });

        Assert.All(engine.AllCommands, c => Assert.Equal(1, c.Stats.Snapshot().Runs));
    }

    [Fact]
    public async Task A_failing_command_counts_a_failure_and_its_exception_is_unchanged()
    {
        WritePlugin("bad", PluginHooks.Startup, "throw new System.InvalidOperationException(\"boom\");");
        PluginEngine engine = Discover();

        var results = await engine.InvokeAsync(PluginHooks.Startup, e => new StartupHookGlobals { Environment = e });

        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.Equal("boom", result.Error!.Message);
        var s = engine.AllCommands.Single().Stats.Snapshot();
        Assert.Equal(1, s.Runs);
        Assert.Equal(1, s.Failures);
    }

    [Fact]
    public async Task Repeated_runs_accumulate()
    {
        WritePlugin("again", PluginHooks.Startup, "return 1;");
        PluginEngine engine = Discover();

        for (int i = 0; i < 5; i++)
        {
            await engine.InvokeAsync(PluginHooks.Startup, e => new StartupHookGlobals { Environment = e });
        }

        Assert.Equal(5, engine.AllCommands.Single().Stats.Snapshot().Runs);
    }

    [Fact]
    public void A_command_that_has_not_run_has_no_data()
    {
        WritePlugin("idle", PluginHooks.Startup, "return 1;");

        Assert.False(Discover().AllCommands.Single().Stats.Snapshot().HasData);
    }

    // ---- recorded by the dispatcher ----

    private static ReadingListChangedHookGlobals Change(IPluginEnvironment env) => new()
    {
        Environment = env, ListId = 1, ListName = "x", Kind = ReadingListChangeKind.Added,
        AddedIssueIds = new[] { 1 }, RemovedIssueIds = Array.Empty<int>(),
    };

    [Fact]
    public async Task A_dispatched_domain_hook_is_counted_as_a_run()
    {
        WritePlugin("mirror", PluginHooks.ReadingListChanged, "return null;");
        PluginEngine engine = Discover();
        var dispatcher = new DomainHookDispatcher(engine, _ => { }, TimeSpan.FromSeconds(10));

        dispatcher.Dispatch(PluginHooks.ReadingListChanged, Change);
        dispatcher.Dispatch(PluginHooks.ReadingListChanged, Change);
        Assert.True(await dispatcher.WaitForIdleAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal(2, engine.AllCommands.Single().Stats.Snapshot().Runs);
    }

    [Fact]
    public async Task A_timeout_is_counted()
    {
        WritePlugin("patient", PluginHooks.ReadingListChanged, "await Task.Delay(System.Threading.Timeout.Infinite, CancellationToken); return null;");
        PluginEngine engine = Discover();
        var dispatcher = new DomainHookDispatcher(engine, _ => { }, TimeSpan.FromMilliseconds(120));

        dispatcher.Dispatch(PluginHooks.ReadingListChanged, Change);
        Assert.True(await dispatcher.WaitForIdleAsync(TimeSpan.FromSeconds(10)));

        var s = engine.AllCommands.Single().Stats.Snapshot();
        Assert.Equal(1, s.TimedOut);
        Assert.Equal(1, s.Failures);   // the cancellation surfaces as the run's exception
    }

    [Fact]
    public async Task Every_dropped_event_is_counted_even_though_it_is_only_reported_once()
    {
        // The script marks when it has actually started (a cold Roslyn compile can take far longer than any fixed delay).
        string started = Path.Combine(Path.GetTempPath(), "paperbunkr-stats-started-" + Guid.NewGuid().ToString("N"));
        WritePlugin("stubborn", PluginHooks.ReadingListChanged,
            "System.IO.File.WriteAllText(@\"" + started + "\", \"1\"); System.Threading.Thread.Sleep(1500); return null;");
        var problems = new List<DomainHookProblem>();
        PluginEngine engine = Discover();
        var dispatcher = new DomainHookDispatcher(engine, problems.Add, TimeSpan.FromSeconds(30), queueCapacity: 2);

        dispatcher.Dispatch(PluginHooks.ReadingListChanged, Change);
        for (int i = 0; i < 300 && !File.Exists(started); i++)
        {
            await Task.Delay(100);                                // wait until the first is genuinely running
        }

        Assert.True(File.Exists(started));
        for (int i = 0; i < 6; i++)
        {
            dispatcher.Dispatch(PluginHooks.ReadingListChanged, Change);
        }

        // Six queued into room for two: four dropped, one report.
        Assert.Equal(4, engine.AllCommands.Single().Stats.Snapshot().Dropped);
        Assert.Single(problems, p => p.Kind == DomainHookProblemKind.EventsDropped);
        Assert.True(await dispatcher.WaitForIdleAsync(TimeSpan.FromSeconds(30)));
    }
}
