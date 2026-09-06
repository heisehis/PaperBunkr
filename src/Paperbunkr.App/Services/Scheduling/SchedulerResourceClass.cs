namespace Paperbunkr.App.Services.Scheduling;

/// <summary>
/// The shared resource a scheduled task contends for
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 1).
/// The runner allows up to two tasks at once but never two of the same class - structurally
/// preventing e.g. two SQLite writers overlapping.
/// </summary>
public enum SchedulerResourceClass
{
    /// <summary>Writes the SQLite database (scans, sweep, metadata sync, backup).</summary>
    Db,

    /// <summary>Heavy disk + CPU, no DB writes of consequence (cover decode/encode).</summary>
    DiskCpu,

    /// <summary>Network-bound (reserved for the future bulk tracker-refresh task).</summary>
    Network,
}
