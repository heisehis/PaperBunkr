using System;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.Scheduling;

/// <summary>What the scheduler should do with a task at a given moment.</summary>
public enum DueDecision
{
    /// <summary>Not enabled, or not yet time.</summary>
    NotDue,

    /// <summary>Due now - enqueue it.</summary>
    Run,
}

/// <summary>
/// Pure "is this task due?" logic
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 1) -
/// no I/O, no clock of its own, so the whole matrix is unit-testable. Level-triggered: "due" is a
/// function of <see cref="ScheduledTaskState.LastRunUtc"/> / "ran today", never a count of missed
/// windows, so a long gap produces exactly one run.
/// </summary>
public static class SchedulerDueLogic
{
    public static DueDecision Evaluate(ScheduledTaskState state, DateTimeOffset nowUtc)
    {
        if (!state.Enabled)
        {
            return DueDecision.NotDue;
        }

        return state.Mode switch
        {
            ScheduleMode.Interval => EvaluateInterval(state, nowUtc),
            ScheduleMode.DailyAt => EvaluateDailyAt(state, nowUtc),
            _ => DueDecision.NotDue,
        };
    }

    private static DueDecision EvaluateInterval(ScheduledTaskState state, DateTimeOffset nowUtc)
    {
        if (state.LastRunUtc is not DateTime last)
        {
            return DueDecision.Run;
        }

        int hours = Math.Max(1, state.IntervalHours);
        return nowUtc.UtcDateTime - DateTime.SpecifyKind(last, DateTimeKind.Utc) >= TimeSpan.FromHours(hours)
            ? DueDecision.Run
            : DueDecision.NotDue;
    }

    private static DueDecision EvaluateDailyAt(ScheduledTaskState state, DateTimeOffset nowUtc)
    {
        DateTime localNow = nowUtc.ToLocalTime().DateTime;
        int minutes = Math.Clamp(state.DailyAtMinutes, 0, 1439);
        DateTime todaysTarget = localNow.Date.AddMinutes(minutes);

        if (localNow < todaysTarget)
        {
            return DueDecision.NotDue; // the scheduled time hasn't arrived today
        }

        if (state.LastRunUtc is not DateTime last)
        {
            return DueDecision.Run;
        }

        DateTime lastLocalDate = DateTime.SpecifyKind(last, DateTimeKind.Utc).ToLocalTime().Date;
        return lastLocalDate < localNow.Date ? DueDecision.Run : DueDecision.NotDue; // once per calendar day
    }

    /// <summary>
    /// The next time <paramref name="state"/> will become due, for the Automation tab's "Next run"
    /// column. Null when it would run on the next check point (interval task that's already overdue
    /// or never run).
    /// </summary>
    public static DateTimeOffset? NextRun(ScheduledTaskState state, DateTimeOffset nowUtc)
    {
        if (!state.Enabled)
        {
            return null;
        }

        if (state.Mode == ScheduleMode.Interval)
        {
            if (state.LastRunUtc is not DateTime last)
            {
                return null;
            }

            var next = new DateTimeOffset(DateTime.SpecifyKind(last, DateTimeKind.Utc), TimeSpan.Zero)
                .AddHours(Math.Max(1, state.IntervalHours));
            return next <= nowUtc ? null : next;
        }

        DateTime localNow = nowUtc.ToLocalTime().DateTime;
        int minutes = Math.Clamp(state.DailyAtMinutes, 0, 1439);
        DateTime candidate = localNow.Date.AddMinutes(minutes);
        if (Evaluate(state, nowUtc) == DueDecision.Run)
        {
            return null;
        }

        if (candidate <= localNow)
        {
            candidate = candidate.AddDays(1);
        }

        return new DateTimeOffset(candidate, TimeZoneInfo.Local.GetUtcOffset(candidate)).ToUniversalTime();
    }
}
