using System;

namespace Paperbunkr.Data.Entities;

/// <summary>
/// One completed background job, persisted for the Activity Center's History tab
/// (docs/superpowers/specs/2026-09-03-activity-center-design.md). Written once, on the job's
/// terminal state - never updated afterwards. A plain growable table (like <see cref="KeyBinding"/>
/// / <see cref="Workspace"/>), not part of <see cref="AppSettings"/>. Pruned on startup by
/// <c>ActivityHistoryStore.PruneOnStartup</c> (keeps the newer of ~200 rows / &lt; 30 days).
/// </summary>
public class ActivityRun
{
    public int Id { get; set; }

    public ActivityJobKind Kind { get; set; }

    public string Title { get; set; } = string.Empty;

    public ActivityTrigger Trigger { get; set; }

    public DateTime StartedUtc { get; set; }

    public DateTime? FinishedUtc { get; set; }

    public ActivityRunStatus Status { get; set; }

    /// <summary>One-line human summary frozen from the job's detail line ("18 series updated, 4 status changes").</summary>
    public string? ResultSummary { get; set; }

    /// <summary>
    /// <c>Paperbunkr.App.Models.ActivityLinkKind</c> name, when the run produced something to click
    /// through to. Stored as the bare enum name rather than mapping the App-side enum into Data.
    /// </summary>
    public string? ResultLinkKind { get; set; }

    /// <summary>Opaque payload the App-side link resolver understands (a filter blob, a series id, …).</summary>
    public string? ResultLinkPayload { get; set; }

    public int? ItemsProcessed { get; set; }

    public int? ItemsFailed { get; set; }
}
