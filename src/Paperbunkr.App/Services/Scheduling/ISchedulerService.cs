using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.Scheduling;

/// <summary>
/// The maintenance-task scheduler
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 1).
/// Hand-composed in <c>MainViewModel</c> like <see cref="IActivityService"/>. Runs a startup pass
/// and a 15-minute in-session tick; due tasks go through a priority queue that runs up to two at a
/// time, never two of the same <see cref="SchedulerResourceClass"/>.
/// </summary>
public interface ISchedulerService
{
    /// <summary>Catalog ⋈ state, one row per task, for the Automation tab. UI-thread affine.</summary>
    IReadOnlyList<ScheduledTaskRow> Tasks { get; }

    /// <summary>Raised (on the UI thread) after any change to task state or run status.</summary>
    event EventHandler? Changed;

    /// <summary>Seed missing rows, run the startup pass, start the tick timer. Call once.</summary>
    void Start();

    /// <summary>Cancel the tick timer and any in-flight scheduled task. Call on app exit.</summary>
    void Stop();

    /// <summary>Run a task now, ignoring its enabled flag and schedule. Still goes through the queue.</summary>
    Task RunNowAsync(string taskId);

    /// <summary>Record that a task's underlying operation just ran (e.g. the user clicked the manual button).</summary>
    void NotifyRan(string taskId, ScheduledRunStatus status);

    void SetEnabled(string taskId, bool enabled);

    void SetSchedule(string taskId, ScheduleMode mode, int intervalHours, int dailyAtMinutes);
}
