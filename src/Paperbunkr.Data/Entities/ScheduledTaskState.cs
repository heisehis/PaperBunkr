using System;

namespace Paperbunkr.Data.Entities;

/// <summary>
/// Per-task schedule + last-run state for the maintenance scheduler
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 1).
/// The task <b>catalog</b> is code-defined (<c>ScheduledTaskCatalog</c>); this table holds only the
/// mutable per-task state, one row per catalog id, seeded on first run. A plain growable table,
/// same shape as <see cref="ActivityRun"/> / <see cref="KeyBinding"/> - not part of
/// <see cref="AppSettings"/>.
/// </summary>
public class ScheduledTaskState
{
    /// <summary>Stable catalog id, e.g. <c>"library-scan"</c>. Primary key.</summary>
    public string TaskId { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public ScheduleMode Mode { get; set; } = ScheduleMode.Interval;

    /// <summary>Interval in hours, used when <see cref="Mode"/> is <see cref="ScheduleMode.Interval"/>.</summary>
    public int IntervalHours { get; set; }

    /// <summary>Minutes past local midnight (0..1439), used when <see cref="Mode"/> is <see cref="ScheduleMode.DailyAt"/>.</summary>
    public int DailyAtMinutes { get; set; }

    /// <summary>UTC time the task last actually completed (or failed). Null = never run.</summary>
    public DateTime? LastRunUtc { get; set; }

    public ScheduledRunStatus? LastRunStatus { get; set; }

    /// <summary>Id of the <see cref="ActivityRun"/> the last run produced - lets the Automation tab deep-link "last run" into History.</summary>
    public int? LastRunActivityId { get; set; }
}

/// <summary>How a scheduled task's next-due time is computed.</summary>
public enum ScheduleMode
{
    /// <summary>Runs when <c>now - LastRunUtc &gt;= IntervalHours</c>.</summary>
    Interval,

    /// <summary>Runs at most once per local calendar day, on or after <c>DailyAtMinutes</c>.</summary>
    DailyAt,
}

/// <summary>Outcome of a scheduled task's most recent run.</summary>
public enum ScheduledRunStatus
{
    Succeeded,
    Failed,

    /// <summary>The cycle was skipped (e.g. the same work was already running) - not a real run.</summary>
    Skipped,
}
