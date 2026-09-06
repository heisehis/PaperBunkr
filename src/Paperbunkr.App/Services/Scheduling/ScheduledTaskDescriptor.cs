using System;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.Scheduling;

/// <summary>
/// One entry in the code-defined maintenance-task catalog
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 1). The
/// catalog is fixed; per-task schedule + last-run state lives in <see cref="ScheduledTaskState"/>.
/// </summary>
/// <param name="Id">Stable key, e.g. <c>"library-scan"</c>. Matches <see cref="ScheduledTaskState.TaskId"/>.</param>
/// <param name="DisplayName">Shown in the Automation tab and as the Activity job title.</param>
/// <param name="Description">One-line explanation for the Automation tab.</param>
/// <param name="ActivityKind">Drives the Activity row icon + history filter, and the same-kind skip check.</param>
/// <param name="Priority">Queue order when several tasks are due at once (lower runs first).</param>
/// <param name="Resource">Concurrency gate - see <see cref="SchedulerResourceClass"/>.</param>
/// <param name="DefaultInterval">Seed interval for a fresh <see cref="ScheduledTaskState"/> row.</param>
/// <param name="DefaultEnabled">Seed enabled flag.</param>
/// <param name="DefaultMode">Seed schedule mode.</param>
/// <param name="RunAsync">The work. Reports progress via the handle; returns a one-line result summary.</param>
public sealed record ScheduledTaskDescriptor(
    string Id,
    string DisplayName,
    string Description,
    ActivityJobKind ActivityKind,
    int Priority,
    SchedulerResourceClass Resource,
    TimeSpan DefaultInterval,
    bool DefaultEnabled,
    ScheduleMode DefaultMode,
    Func<IActivityJobHandle, CancellationToken, Task<string>> RunAsync);
