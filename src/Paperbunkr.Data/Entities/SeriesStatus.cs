namespace Paperbunkr.Data.Entities;

/// <summary>
/// Replaces the stored <c>bool IsComplete</c> as the real source of truth (docs/superpowers/specs/
/// 2026-08-17-metadata-model-phase1-canonical-metadata-design.md) - <see cref="Series.IsComplete"/>
/// becomes a computed property (<c>Status == Completed</c>) instead of its own column. Has no CE
/// precedent at all - a deliberate new feature, not parity (CE only ever had the per-issue
/// <c>SeriesComplete</c> flag this project already ports as <see cref="Issue.IsFinalIssue"/>).
/// </summary>
public enum SeriesStatus
{
    Unknown,
    Ongoing,
    Completed,
    Cancelled,
    Hiatus,
}
