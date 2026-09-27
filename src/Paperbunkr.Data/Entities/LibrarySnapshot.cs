namespace Paperbunkr.Data.Entities;

/// <summary>
/// One local calendar day's owned/backlog comic counts (docs/superpowers/specs/2026-09-22-insights-
/// backlog-burndown-design.md), written once a day by the <c>library-snapshot</c> scheduled task
/// (<c>LibrarySnapshotService</c>) and read by <c>StatsResolver.ComputeBurnDown</c> for the Insights
/// screen's Backlog burn-down chart.
///
/// A plain growable table (same category as <see cref="ReadingEvent"/>/<see cref="ActivityRun"/>), never
/// pruned - at roughly one row per day this is trivial storage, and it's the foundation this and future
/// Insights trend charts read from, the same "long-range history" reasoning <see cref="ReadingEvent"/>'s
/// own doc comment already gives for staying unpruned.
///
/// <see cref="SnapshotDate"/> is a unique key - the write side upserts by date rather than appending,
/// since the Automation tab's per-task "Run now" button can trigger a second capture on the same day even
/// though the scheduler's own <c>ScheduleMode.DailyAt</c> due-check normally prevents that.
///
/// Comics only, local library only (<c>Issue.RemoteSourceId == null</c>) - matches
/// <c>StatsResolver</c>'s own existing precedent that library-size figures use local issues only, since a
/// remote series isn't something you own. Books aren't counted (out of scope for this slice - see the
/// design doc).
/// </summary>
public class LibrarySnapshot
{
    public int Id { get; set; }

    public DateOnly SnapshotDate { get; set; }

    public int TotalOwnedComics { get; set; }

    /// <summary>Comics with <c>IssueMetadataExtensions.IsUnread</c> true - never opened at all.</summary>
    public int BacklogComics { get; set; }
}
