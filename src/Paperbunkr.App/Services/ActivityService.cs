using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Default <see cref="IActivityService"/>. Constructed once in <c>MainViewModel</c>. Every mutation
/// of the observable collections is marshalled onto the UI thread via <see cref="_dispatch"/> so
/// background jobs can call their handle from the thread pool freely.
/// </summary>
public sealed class ActivityService : IActivityService
{
    private const int RecentCap = 20;

    private readonly ObservableCollection<ActivityJob> _active = new();
    private readonly ObservableCollection<ActivityJob> _recent = new();
    private readonly ObservableCollection<ActivityAlert> _alerts = new();
    private readonly object _gate = new();
    private readonly Action<Action> _dispatch;
    private readonly Action<ActivityRun> _recordRun;

    private UpkeepHandle? _upkeep;

    /// <param name="dispatch">Marshals a mutation onto the UI thread. Defaults to a <c>Dispatcher.UIThread</c>-aware post; tests pass <c>a =&gt; a()</c>.</param>
    /// <param name="recordRun">Persists one settled run. Defaults to <see cref="ActivityHistoryStore.Record"/>; tests capture it instead of touching the real DB.</param>
    public ActivityService(Action<Action>? dispatch = null, Action<ActivityRun>? recordRun = null)
    {
        _dispatch = dispatch ?? DefaultDispatch;
        _recordRun = recordRun ?? ActivityHistoryStore.Record;
        ActiveJobs = new ReadOnlyObservableCollection<ActivityJob>(_active);
        RecentJobs = new ReadOnlyObservableCollection<ActivityJob>(_recent);
        Alerts = new ReadOnlyObservableCollection<ActivityAlert>(_alerts);
    }

    private static void DefaultDispatch(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    public ReadOnlyObservableCollection<ActivityJob> ActiveJobs { get; }

    public ReadOnlyObservableCollection<ActivityJob> RecentJobs { get; }

    public ReadOnlyObservableCollection<ActivityAlert> Alerts { get; }

    public bool PanelIsOpen { get; set; }

    public event EventHandler? Changed;

    public event Action<string, string>? CompletionToastRequested;

    public IActivityJobHandle StartJob(ActivityJobKind kind, string title, bool cancellable = true, ActivityTrigger trigger = ActivityTrigger.Manual)
    {
        var job = new ActivityJob { Kind = kind, Title = title, Trigger = trigger, Status = ActivityJobStatus.Running };
        var handle = new JobHandle(this, job, cancellable);

        _dispatch(() =>
        {
            _active.Insert(0, job);
            RaiseChanged();
        });

        return handle;
    }

    public IActivityUpkeepHandle RegisterUpkeep(string title)
    {
        if (_upkeep is not null)
        {
            return _upkeep;
        }

        var job = new ActivityJob { Kind = ActivityJobKind.Upkeep, Title = title, IsUpkeep = true, Status = ActivityJobStatus.Running, Detail = "Idle" };
        _upkeep = new UpkeepHandle(this, job);

        _dispatch(() =>
        {
            _active.Add(job); // upkeep sits at the bottom, not the top
            RaiseChanged();
        });

        return _upkeep;
    }

    public void CancelJob(Guid jobId)
    {
        JobHandle? handle;
        lock (_gate)
        {
            handle = _liveHandles.FirstOrDefault(h => h.Job.Id == jobId);
        }

        handle?.CancelExternally();
    }

    public void RaiseAlert(ActivityAlert alert)
    {
        _dispatch(() =>
        {
            var existing = _alerts.FirstOrDefault(a => a.DedupeKey == alert.DedupeKey);
            if (existing is not null)
            {
                existing.CreatedUtc = DateTime.UtcNow;
                _alerts.Move(_alerts.IndexOf(existing), 0);
            }
            else
            {
                _alerts.Insert(0, alert);
            }

            RaiseChanged();
        });
    }

    public void DismissAlert(Guid alertId)
    {
        _dispatch(() =>
        {
            var match = _alerts.FirstOrDefault(a => a.Id == alertId);
            if (match is not null)
            {
                _alerts.Remove(match);
                RaiseChanged();
            }
        });
    }

    public void DismissAllAlerts()
    {
        _dispatch(() =>
        {
            if (_alerts.Count == 0)
            {
                return;
            }

            _alerts.Clear();
            RaiseChanged();
        });
    }

    public void ClearFinished()
    {
        _dispatch(() =>
        {
            if (_recent.Count == 0)
            {
                return;
            }

            _recent.Clear();
            RaiseChanged();
        });
    }

    public void StopAll()
    {
        // Snapshot under the gate; the actual cancellation happens outside the dispatch.
        JobHandle[] handles;
        lock (_gate)
        {
            handles = _liveHandles.ToArray();
        }

        foreach (var handle in handles)
        {
            handle.CancelExternally();
        }
    }

    // ---- internals shared with the handles ----

    private readonly List<JobHandle> _liveHandles = new();

    private void RegisterHandle(JobHandle handle)
    {
        lock (_gate)
        {
            _liveHandles.Add(handle);
        }
    }

    private void SettleJob(JobHandle handle, ActivityJobStatus status, string summary, ActivityLink? link, int? processed, int? failed)
    {
        lock (_gate)
        {
            _liveHandles.Remove(handle);
        }

        var job = handle.Job;

        _dispatch(() =>
        {
            job.Status = status;
            job.FinishedUtc = DateTime.UtcNow;
            job.Detail = summary;
            job.ResultSummary = summary;
            job.ResultLink = link;

            _active.Remove(job);
            _recent.Insert(0, job);
            while (_recent.Count > RecentCap)
            {
                _recent.RemoveAt(_recent.Count - 1);
            }

            RaiseChanged();

            if (!PanelIsOpen && status != ActivityJobStatus.Cancelled)
            {
                CompletionToastRequested?.Invoke(TitleForToast(job, status), summary);
            }
        });

        PersistRun(job, status, link, processed, failed);
    }

    private static string TitleForToast(ActivityJob job, ActivityJobStatus status) =>
        status == ActivityJobStatus.Failed ? $"{job.Title} failed" : $"{job.Title} finished";

    private void PersistRun(ActivityJob job, ActivityJobStatus status, ActivityLink? link, int? processed, int? failed)
    {
        var runStatus = status switch
        {
            ActivityJobStatus.Succeeded => ActivityRunStatus.Succeeded,
            ActivityJobStatus.Failed => ActivityRunStatus.Failed,
            _ => ActivityRunStatus.Cancelled,
        };

        try
        {
            _recordRun(new ActivityRun
            {
                Kind = job.Kind,
                Title = job.Title,
                Trigger = job.Trigger,
                StartedUtc = job.StartedUtc,
                FinishedUtc = job.FinishedUtc ?? DateTime.UtcNow,
                Status = runStatus,
                ResultSummary = job.ResultSummary,
                ResultLinkKind = link?.Kind.ToString(),
                ResultLinkPayload = link?.Payload,
                ItemsProcessed = processed,
                ItemsFailed = failed,
            });
        }
        catch
        {
            // History is best-effort - a write failure must never propagate into the caller's job.
        }
    }

    private void UpdateUpkeep(ActivityJob job, string detail, bool active)
    {
        _dispatch(() =>
        {
            job.Detail = detail;
            job.UpkeepActive = active;
            RaiseChanged();
        });
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    // ================= handles =================

    private sealed class JobHandle : IActivityJobHandle
    {
        private readonly ActivityService _owner;
        private readonly CancellationTokenSource? _cts;
        private long _lastReportTicks;
        private int _settled;

        public JobHandle(ActivityService owner, ActivityJob job, bool cancellable)
        {
            _owner = owner;
            Job = job;
            _cts = cancellable ? new CancellationTokenSource() : null;
            owner.RegisterHandle(this);
        }

        public ActivityJob Job { get; }

        public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

        public void Report(int done, int total, string? detail = null)
        {
            // Coalesce to ~10/s so a tight loop doesn't flood the dispatcher.
            long now = DateTime.UtcNow.Ticks;
            long last = Interlocked.Read(ref _lastReportTicks);
            bool complete = total > 0 && done >= total;
            if (!complete && now - last < TimeSpan.TicksPerMillisecond * 100)
            {
                return;
            }

            Interlocked.Exchange(ref _lastReportTicks, now);
            _owner._dispatch(() =>
            {
                Job.Done = done;
                Job.Total = total;
                if (detail is not null)
                {
                    Job.Detail = detail;
                }

                _owner.RaiseChanged();
            });
        }

        public void Report(string detail)
        {
            _owner._dispatch(() =>
            {
                Job.Detail = detail;
                _owner.RaiseChanged();
            });
        }

        public void Succeed(string summary, ActivityLink? link = null, int? itemsProcessed = null, int? itemsFailed = null)
        {
            if (Interlocked.Exchange(ref _settled, 1) == 1)
            {
                return;
            }

            _owner.SettleJob(this, ActivityJobStatus.Succeeded, summary, link, itemsProcessed, itemsFailed);
            _cts?.Dispose();
        }

        public void Fail(string summary, ActivityLink? link = null, Exception? ex = null)
        {
            if (Interlocked.Exchange(ref _settled, 1) == 1)
            {
                return;
            }

            _owner.SettleJob(this, ActivityJobStatus.Failed, summary, link, null, null);
            _cts?.Dispose();
        }

        public void CancelExternally()
        {
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _settled, 1) == 1)
            {
                return;
            }

            // Disposed without Succeed/Fail - the job was abandoned or cancelled.
            _owner.SettleJob(this, ActivityJobStatus.Cancelled, "Cancelled", null, null, null);
            _cts?.Dispose();
        }
    }

    private sealed class UpkeepHandle : IActivityUpkeepHandle
    {
        private readonly ActivityService _owner;
        private readonly ActivityJob _job;

        public UpkeepHandle(ActivityService owner, ActivityJob job)
        {
            _owner = owner;
            _job = job;
        }

        public void SetActive(string detail) => _owner.UpdateUpkeep(_job, detail, active: true);

        public void SetIdle() => _owner.UpdateUpkeep(_job, string.Empty, active: false);
    }
}
