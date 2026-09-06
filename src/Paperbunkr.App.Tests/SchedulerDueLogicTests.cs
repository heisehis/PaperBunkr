using System;
using Paperbunkr.App.Services.Scheduling;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="SchedulerDueLogic"/> - the pure "is this task due?" matrix
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 1).
/// </summary>
public class SchedulerDueLogicTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static ScheduledTaskState State(bool enabled = true, ScheduleMode mode = ScheduleMode.Interval,
        int intervalHours = 6, int dailyAtMinutes = 180, DateTime? lastRun = null) => new()
    {
        TaskId = "t",
        Enabled = enabled,
        Mode = mode,
        IntervalHours = intervalHours,
        DailyAtMinutes = dailyAtMinutes,
        LastRunUtc = lastRun,
    };

    [Fact]
    public void Disabled_IsNeverDue()
    {
        Assert.Equal(DueDecision.NotDue, SchedulerDueLogic.Evaluate(State(enabled: false, lastRun: null), Now));
    }

    [Fact]
    public void Interval_NeverRun_IsDue()
    {
        Assert.Equal(DueDecision.Run, SchedulerDueLogic.Evaluate(State(lastRun: null), Now));
    }

    [Fact]
    public void Interval_WithinWindow_IsNotDue()
    {
        Assert.Equal(DueDecision.NotDue, SchedulerDueLogic.Evaluate(State(intervalHours: 6, lastRun: Now.UtcDateTime.AddHours(-3)), Now));
    }

    [Fact]
    public void Interval_PastWindow_IsDue()
    {
        Assert.Equal(DueDecision.Run, SchedulerDueLogic.Evaluate(State(intervalHours: 6, lastRun: Now.UtcDateTime.AddHours(-7)), Now));
    }

    [Fact]
    public void Interval_LongGap_StillJustOneRun()
    {
        // Level-triggered: a week-old last run is "due", singular - the caller runs it once.
        Assert.Equal(DueDecision.Run, SchedulerDueLogic.Evaluate(State(intervalHours: 6, lastRun: Now.UtcDateTime.AddDays(-7)), Now));
    }

    [Fact]
    public void DailyAt_BeforeTheTime_IsNotDue()
    {
        // 12:00 now, target 23:00 (1380 min) - hasn't arrived.
        Assert.Equal(DueDecision.NotDue, SchedulerDueLogic.Evaluate(State(mode: ScheduleMode.DailyAt, dailyAtMinutes: 1380, lastRun: null), Now));
    }

    [Fact]
    public void DailyAt_AfterTheTime_NeverRun_IsDue()
    {
        Assert.Equal(DueDecision.Run, SchedulerDueLogic.Evaluate(State(mode: ScheduleMode.DailyAt, dailyAtMinutes: 60, lastRun: null), Now));
    }

    [Fact]
    public void DailyAt_AlreadyRanToday_IsNotDue()
    {
        var ranEarlierToday = Now.UtcDateTime.AddHours(-2);
        Assert.Equal(DueDecision.NotDue, SchedulerDueLogic.Evaluate(State(mode: ScheduleMode.DailyAt, dailyAtMinutes: 60, lastRun: ranEarlierToday), Now));
    }

    [Fact]
    public void DailyAt_RanYesterday_IsDue()
    {
        var ranYesterday = Now.UtcDateTime.AddDays(-1);
        Assert.Equal(DueDecision.Run, SchedulerDueLogic.Evaluate(State(mode: ScheduleMode.DailyAt, dailyAtMinutes: 60, lastRun: ranYesterday), Now));
    }

    [Fact]
    public void NextRun_Interval_NeverRun_IsNull()
    {
        Assert.Null(SchedulerDueLogic.NextRun(State(lastRun: null), Now));
    }

    [Fact]
    public void NextRun_Interval_FutureWindow_ReturnsThatTime()
    {
        var last = Now.UtcDateTime.AddHours(-2);
        var next = SchedulerDueLogic.NextRun(State(intervalHours: 6, lastRun: last), Now);
        Assert.NotNull(next);
        Assert.Equal(new DateTimeOffset(last.AddHours(6), TimeSpan.Zero), next!.Value);
    }
}
