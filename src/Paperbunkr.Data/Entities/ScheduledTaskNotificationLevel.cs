namespace Paperbunkr.Data.Entities;

/// <summary>
/// How loudly a finished <b>scheduled</b> task announces itself
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 1).
/// Governs only the transient completion toast - every scheduled run still shows in the Activity
/// Center peek and History regardless. App-wide setting on <see cref="AppSettings"/>.
/// </summary>
public enum ScheduledTaskNotificationLevel
{
    /// <summary>Toast on every scheduled run that finishes.</summary>
    EveryRun,

    /// <summary>Toast only when a scheduled run fails (the default).</summary>
    OnlyFailures,

    /// <summary>Never toast for a scheduled run.</summary>
    Never,
}
