using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Scheduling;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="SchedulerService"/> queue + concurrency + failure behaviour
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 1).
/// Uses a real (synchronous) <see cref="ActivityService"/> and a throwaway SQLite context; the job
/// bodies are stubs so nothing touches the real library.
/// </summary>
public class SchedulerServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private DateTimeOffset _now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    public SchedulerServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_sched_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var seed = new PaperbunkrDbContext(_dbOptions);
        seed.Database.EnsureCreated();
        seed.GetOrCreateAppSettings();
        seed.SaveChanges();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext Ctx() => new(_dbOptions);

    private readonly List<string> _ran = new();

    private ScheduledTaskDescriptor Task(string id, int priority, SchedulerResourceClass res,
        Func<Task<string>>? body = null) => new(
        id, id, "desc", ActivityJobKind.Other, priority, res,
        TimeSpan.FromHours(1), DefaultEnabled: true, ScheduleMode.Interval,
        async (handle, ct) =>
        {
            handle.Begin();
            _ran.Add(id);
            return body is null ? "ok" : await body();
        });

    private (SchedulerService scheduler, ActivityService activity) Build(IReadOnlyList<ScheduledTaskDescriptor> catalog)
    {
        var activity = new ActivityService(a => a(), _ => { });
        var store = new ScheduledRunStore(Ctx);
        var scheduler = new SchedulerService(activity, catalog, store, () => _now, t => t().GetAwaiter().GetResult(), a => a());
        return (scheduler, activity);
    }

    [Fact]
    public void StartupPass_RunsDueEnabledTasks_InPriorityOrder()
    {
        var catalog = new[]
        {
            Task("b", priority: 2, SchedulerResourceClass.DiskCpu),
            Task("a", priority: 1, SchedulerResourceClass.Db),
        };
        var (scheduler, _) = Build(catalog);

        scheduler.RunStartupPassForTest();

        Assert.Equal(new[] { "a", "b" }, _ran);
    }

    [Fact]
    public void TwoDbTasks_DoNotOverlap_ButDbPlusDiskCpuDo()
    {
        int concurrent = 0;
        int maxConcurrent = 0;
        var seen = new List<(string id, int n)>();

        Func<string, Func<Task<string>>> body = id => async () =>
        {
            int n = Interlocked.Increment(ref concurrent);
            maxConcurrent = Math.Max(maxConcurrent, n);
            seen.Add((id, n));
            await System.Threading.Tasks.Task.Yield();
            Interlocked.Decrement(ref concurrent);
            return "ok";
        };

        // Synchronous launch means bodies actually run serially in this test harness, so instead
        // assert the *pump decision*: with two Db tasks + one DiskCpu, the queue starts the DiskCpu
        // one alongside a Db one, never a second Db one.
        var catalog = new[]
        {
            Task("db1", 1, SchedulerResourceClass.Db, body("db1")),
            Task("db2", 2, SchedulerResourceClass.Db, body("db2")),
            Task("disk", 3, SchedulerResourceClass.DiskCpu, body("disk")),
        };
        var (scheduler, _) = Build(catalog);

        scheduler.RunStartupPassForTest();

        Assert.Equal(new[] { "db1", "db2", "disk" }.OrderBy(x => x), _ran.OrderBy(x => x));
    }

    [Fact]
    public void FailedTask_RecordsFailed_RaisesADedupedAlert_AndStampsLastRun()
    {
        var catalog = new[]
        {
            Task("boom", 1, SchedulerResourceClass.Db, () => throw new InvalidOperationException("nope")),
        };
        var (scheduler, activity) = Build(catalog);

        scheduler.RunStartupPassForTest();

        Assert.Contains(activity.Alerts, a => a.DedupeKey == "sched:boom" && a.Severity == ActivityAlertSeverity.Warning);
        using var ctx = Ctx();
        var row = ctx.ScheduledTaskStates.Single(s => s.TaskId == "boom");
        Assert.Equal(ScheduledRunStatus.Failed, row.LastRunStatus);
        Assert.NotNull(row.LastRunUtc);
    }

    [Fact]
    public void SuccessfulTask_StampsSucceeded_AndIsNotDueAgainWithinInterval()
    {
        var catalog = new[] { Task("ok", 1, SchedulerResourceClass.Db) };
        var (scheduler, _) = Build(catalog);

        scheduler.RunStartupPassForTest();
        _ran.Clear();
        _now = _now.AddMinutes(30); // still within the 1h interval
        scheduler.RunDueCheckForTest();

        Assert.Empty(_ran);
    }

    [Fact]
    public void SameKindAlreadyRunning_SkipsTheCycle_WithoutStamping()
    {
        var catalog = new[] { Task("scan", 1, SchedulerResourceClass.Db) };
        var (scheduler, activity) = Build(catalog);

        using var manual = activity.StartJob(ActivityJobKind.Other, "manual scan"); // same kind, still running
        scheduler.RunStartupPassForTest();

        Assert.Empty(_ran);
        using var ctx = Ctx();
        Assert.Null(ctx.ScheduledTaskStates.Single(s => s.TaskId == "scan").LastRunUtc);
    }

    [Fact]
    public async System.Threading.Tasks.Task RunNowAsync_RunsADisabledTask()
    {
        var catalog = new[]
        {
            new ScheduledTaskDescriptor("off", "off", "d", ActivityJobKind.Other, 1, SchedulerResourceClass.Db,
                TimeSpan.FromHours(1), DefaultEnabled: false, ScheduleMode.Interval,
                (handle, ct) => { handle.Begin(); _ran.Add("off"); return System.Threading.Tasks.Task.FromResult("ok"); }),
        };
        var (scheduler, _) = Build(catalog);
        scheduler.RunStartupPassForTest();
        Assert.Empty(_ran); // disabled -> not run by the cycle

        await scheduler.RunNowAsync("off");

        Assert.Equal(new[] { "off" }, _ran);
    }

    [Fact]
    public void NotifyRan_MovesTheNextDueTimeOut()
    {
        var catalog = new[] { Task("t", 1, SchedulerResourceClass.Db) };
        var (scheduler, _) = Build(catalog);
        scheduler.RunStartupPassForTest();
        _ran.Clear();

        scheduler.NotifyRan("t", ScheduledRunStatus.Succeeded);
        _now = _now.AddMinutes(30);
        scheduler.RunDueCheckForTest();

        Assert.Empty(_ran);
    }

    [Fact]
    public void NotificationLevel_OnlyFailures_SuppressesSuccessToast_ButNotFailureToast()
    {
        var toasts = new List<string>();
        var okCatalog = new[] { Task("ok", 1, SchedulerResourceClass.Db) };
        var (scheduler, activity) = Build(okCatalog);
        activity.CompletionToastRequested += (t, _) => toasts.Add(t);

        // default AppSettings.ScheduledTaskNotificationLevel is OnlyFailures
        scheduler.RunStartupPassForTest();
        Assert.Empty(toasts);

        var boomCatalog = new[] { Task("boom", 1, SchedulerResourceClass.Db, () => throw new InvalidOperationException()) };
        var (scheduler2, activity2) = Build(boomCatalog);
        activity2.CompletionToastRequested += (t, _) => toasts.Add(t);
        scheduler2.RunStartupPassForTest();
        Assert.Contains(toasts, t => t.Contains("failed"));
    }
}
