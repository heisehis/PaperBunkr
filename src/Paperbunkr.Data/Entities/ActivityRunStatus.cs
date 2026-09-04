namespace Paperbunkr.Data.Entities;

/// <summary>
/// Terminal outcome of a persisted <see cref="ActivityRun"/>
/// (docs/superpowers/specs/2026-09-03-activity-center-design.md). Only settled jobs are written to
/// history, so there is no <c>Queued</c>/<c>Running</c> here. <see cref="Interrupted"/> is
/// reserved for a future "process died mid-job" case - v1 never persists a non-terminal run, so
/// nothing writes it yet.
/// </summary>
public enum ActivityRunStatus
{
    Succeeded,
    Failed,
    Cancelled,
    Interrupted,
}
