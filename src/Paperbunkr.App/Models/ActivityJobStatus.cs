namespace Paperbunkr.App.Models;

/// <summary>
/// Live state of an <see cref="ActivityJob"/> in the Activity Center
/// (docs/superpowers/specs/2026-09-03-activity-center-design.md). Only the settled three
/// (<see cref="Succeeded"/>/<see cref="Failed"/>/<see cref="Cancelled"/>) map onto a persisted
/// <c>Paperbunkr.Data.Entities.ActivityRunStatus</c>; <see cref="Queued"/>/<see cref="Running"/>
/// are in-memory only.
/// </summary>
public enum ActivityJobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}
