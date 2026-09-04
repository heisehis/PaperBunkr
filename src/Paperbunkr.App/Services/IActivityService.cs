using System;
using System.Collections.ObjectModel;
using System.Threading;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// The app-wide registry every background job and peripheral alert reports through
/// (docs/superpowers/specs/2026-09-03-activity-center-design.md). Constructed once in
/// <c>MainViewModel</c> (no DI container) and handed to every screen VM that starts background
/// work, replacing the old <c>ShowProgressToast</c>/<c>CloseProgressToast</c> delegate pair.
/// </summary>
public interface IActivityService
{
    /// <summary>Running jobs, newest first. UI-thread affine.</summary>
    ReadOnlyObservableCollection<ActivityJob> ActiveJobs { get; }

    /// <summary>A bounded in-memory tail of the most recently finished jobs (the peek's "Finished" section).</summary>
    ReadOnlyObservableCollection<ActivityJob> RecentJobs { get; }

    /// <summary>Live alerts, newest first.</summary>
    ReadOnlyObservableCollection<ActivityAlert> Alerts { get; }

    /// <summary>
    /// Set by <c>ActivityCenterViewModel</c> while the peek or drawer is showing - completion
    /// toasts are suppressed while the user is already looking at the panel.
    /// </summary>
    bool PanelIsOpen { get; set; }

    /// <summary>Raised (on the UI thread) after any change to jobs or alerts.</summary>
    event EventHandler? Changed;

    /// <summary>Raised when a job settles and a toast should surface it (title, message). Not raised while <see cref="PanelIsOpen"/>.</summary>
    event Action<string, string>? CompletionToastRequested;

    /// <summary>Start tracking a job. The caller drives it via the returned handle and must dispose it.</summary>
    IActivityJobHandle StartJob(ActivityJobKind kind, string title, bool cancellable = true, ActivityTrigger trigger = ActivityTrigger.Manual);

    /// <summary>Register the single ambient "Background upkeep" rollup row. Call once at startup.</summary>
    IActivityUpkeepHandle RegisterUpkeep(string title);

    /// <summary>Cancel one running job's token (the ✕ on a job row). No-op if it already settled.</summary>
    void CancelJob(Guid jobId);

    void RaiseAlert(ActivityAlert alert);

    void DismissAlert(Guid alertId);

    void DismissAllAlerts();

    /// <summary>Drop the finished-jobs tail. Never touches alerts or the upkeep row.</summary>
    void ClearFinished();

    /// <summary>
    /// Cancel every active job's token. v1's "Stop all" - jobs stop cooperatively and each records
    /// as <c>Cancelled</c>. There is no resume (a true pause/resume is deferred, see the design's
    /// "Later" list).
    /// </summary>
    void StopAll();
}

/// <summary>
/// Caller-side control of one job. Report progress as it runs, then call exactly one of
/// <see cref="Succeed"/>/<see cref="Fail"/> - or dispose without either to mark it cancelled.
/// </summary>
public interface IActivityJobHandle : IDisposable
{
    /// <summary>Trips when this job is cancelled individually or by <see cref="IActivityService.StopAll"/>.</summary>
    CancellationToken CancellationToken { get; }

    void Report(int done, int total, string? detail = null);

    void Report(string detail);

    void Succeed(string summary, ActivityLink? link = null, int? itemsProcessed = null, int? itemsFailed = null);

    void Fail(string summary, ActivityLink? link = null, Exception? ex = null);
}

/// <summary>The always-present ambient rollup row - toggled active/idle, never settles.</summary>
public interface IActivityUpkeepHandle
{
    void SetActive(string detail);

    void SetIdle();
}
