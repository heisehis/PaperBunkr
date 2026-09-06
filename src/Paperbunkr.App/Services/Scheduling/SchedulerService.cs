using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.Scheduling;

/// <summary>
/// Default <see cref="ISchedulerService"/>
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 1).
/// Hand-composed in <c>MainViewModel</c>. Never touches the UI thread directly - it only calls
/// <see cref="IActivityService"/> (which marshals) and raises its own <see cref="Changed"/> event
/// through the injected dispatch seam.
/// </summary>
public sealed class SchedulerService : ISchedulerService, IDisposable
{
    private const int MaxConcurrent = 2;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(15);

    private readonly IActivityService _activity;
    private readonly IReadOnlyList<ScheduledTaskDescriptor> _catalog;
    private readonly ScheduledRunStore _store;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<Func<Task>> _launch;
    private readonly Action<Action> _dispatch;

    private readonly object _gate = new();
    private readonly HashSet<string> _inFlight = new();       // queued or running
    private readonly List<SchedulerResourceClass> _runningClasses = new();
    private readonly List<string> _queue = new();             // task ids, priority order
    private CancellationTokenSource _lifetime = new();
    private PeriodicTimer? _timer;
    private IReadOnlyList<ScheduledTaskRow> _rows = Array.Empty<ScheduledTaskRow>();

    public SchedulerService(IActivityService activity)
        : this(activity, ScheduledTaskCatalog.All, new ScheduledRunStore(), () => DateTimeOffset.UtcNow, null, null)
    {
    }

    internal SchedulerService(
        IActivityService activity,
        IReadOnlyList<ScheduledTaskDescriptor> catalog,
        ScheduledRunStore store,
        Func<DateTimeOffset> now,
        Action<Func<Task>>? launch,
        Action<Action>? dispatch)
    {
        _activity = activity;
        _catalog = catalog;
        _store = store;
        _now = now;
        _launch = launch ?? (t => _ = Task.Run(t));
        _dispatch = dispatch ?? DefaultDispatch;
        _activity.Changed += (_, _) => RefreshRunStatus();
    }

    public IReadOnlyList<ScheduledTaskRow> Tasks => _rows;

    public event EventHandler? Changed;

    /// <summary>Test seam - seed + one due-check, without starting the real timer.</summary>
    internal void RunStartupPassForTest()
    {
        _store.SeedMissing(_catalog);
        RebuildRows();
        CheckCycle();
    }

    /// <summary>Test seam - re-evaluate due tasks now.</summary>
    internal void RunDueCheckForTest() => CheckCycle();

    public void Start()
    {
        _store.SeedMissing(_catalog);
        RebuildRows();
        CheckCycle();

        _timer = new PeriodicTimer(TickInterval);
        _launch(async () =>
        {
            try
            {
                while (await _timer.WaitForNextTickAsync(_lifetime.Token))
                {
                    CheckCycle();
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    public void Stop()
    {
        try
        {
            _lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _timer?.Dispose();
    }

    public void Dispose() => Stop();

    public Task RunNowAsync(string taskId)
    {
        var descriptor = _catalog.FirstOrDefault(d => d.Id == taskId);
        if (descriptor is null)
        {
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource();
        lock (_gate)
        {
            if (_inFlight.Contains(taskId))
            {
                return Task.CompletedTask;
            }

            _inFlight.Add(taskId);
            _queue.Add(taskId);
            SortQueue();
        }

        RebuildRows();
        PumpQueue(manualCompletion: (id, ok) =>
        {
            if (id == taskId)
            {
                tcs.TrySetResult();
            }
        }, manualTrigger: taskId);
        return tcs.Task;
    }

    public void NotifyRan(string taskId, ScheduledRunStatus status)
    {
        _store.RecordRun(taskId, status, _now().UtcDateTime);
        RebuildRows();
    }

    public void SetEnabled(string taskId, bool enabled)
    {
        _store.SetEnabled(taskId, enabled);
        RebuildRows();
        CheckCycle();
    }

    public void SetSchedule(string taskId, ScheduleMode mode, int intervalHours, int dailyAtMinutes)
    {
        _store.SetSchedule(taskId, mode, intervalHours, dailyAtMinutes);
        RebuildRows();
        CheckCycle();
    }

    // ----- the cycle + queue -----

    private void CheckCycle()
    {
        var states = _store.LoadAll().ToDictionary(s => s.TaskId);
        var now = _now();

        lock (_gate)
        {
            foreach (var d in _catalog.OrderBy(d => d.Priority))
            {
                if (_inFlight.Contains(d.Id))
                {
                    continue;
                }

                if (!states.TryGetValue(d.Id, out var state))
                {
                    continue;
                }

                if (SchedulerDueLogic.Evaluate(state, now) != DueDecision.Run)
                {
                    continue;
                }

                if (SameKindActive(d.ActivityKind))
                {
                    _store.RecordRun(d.Id, ScheduledRunStatus.Skipped, now.UtcDateTime); // no-op, kept explicit
                    continue;
                }

                _inFlight.Add(d.Id);
                _queue.Add(d.Id);
            }

            SortQueue();
        }

        RebuildRows();
        PumpQueue();
    }

    private void PumpQueue(Action<string, bool>? manualCompletion = null, string? manualTrigger = null)
    {
        while (true)
        {
            ScheduledTaskDescriptor? next = null;
            lock (_gate)
            {
                if (_runningClasses.Count >= MaxConcurrent || _queue.Count == 0)
                {
                    return;
                }

                foreach (string id in _queue)
                {
                    var d = _catalog.First(x => x.Id == id);
                    if (!_runningClasses.Contains(d.Resource))
                    {
                        next = d;
                        break;
                    }
                }

                if (next is null)
                {
                    return; // everything queued shares a class with something running
                }

                _queue.Remove(next.Id);
                _runningClasses.Add(next.Resource);
            }

            RunOne(next, manualCompletion, manualTrigger);
        }
    }

    private void RunOne(ScheduledTaskDescriptor descriptor, Action<string, bool>? manualCompletion, string? manualTrigger)
    {
        bool isManual = descriptor.Id == manualTrigger;
        var policy = isManual ? ActivityToastPolicy.Always : ToastPolicyForLevel(CurrentNotificationLevel());
        var handle = _activity.StartJob(
            descriptor.ActivityKind,
            descriptor.DisplayName,
            cancellable: true,
            trigger: isManual ? ActivityTrigger.Manual : ActivityTrigger.Scheduled,
            toastPolicy: policy,
            startQueued: true);

        _launch(async () =>
        {
            handle.Begin();
            bool ok = false;
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, handle.CancellationToken);
                string summary = await descriptor.RunAsync(handle, linked.Token).ConfigureAwait(false);
                handle.Succeed(summary);
                _store.RecordRun(descriptor.Id, ScheduledRunStatus.Succeeded, _now().UtcDateTime);
                ok = true;
            }
            catch (OperationCanceledException)
            {
                handle.Dispose(); // records as Cancelled; no LastRunUtc stamp
            }
            catch (Exception ex)
            {
                handle.Fail($"{descriptor.DisplayName} failed", ex: ex);
                _store.RecordRun(descriptor.Id, ScheduledRunStatus.Failed, _now().UtcDateTime);
                RaiseFailureAlert(descriptor);
            }
            finally
            {
                lock (_gate)
                {
                    _runningClasses.Remove(descriptor.Resource);
                    _inFlight.Remove(descriptor.Id);
                }

                manualCompletion?.Invoke(descriptor.Id, ok);
                RebuildRows();
                PumpQueue(manualCompletion, manualTrigger);
            }
        });
    }

    private void RaiseFailureAlert(ScheduledTaskDescriptor descriptor)
    {
        _activity.RaiseAlert(new ActivityAlert
        {
            Severity = ActivityAlertSeverity.Warning,
            Title = $"{descriptor.DisplayName} failed",
            Detail = "The scheduled task didn't complete. It will try again at its next scheduled time, or use Run now.",
            DedupeKey = $"sched:{descriptor.Id}",
        });
    }

    private bool SameKindActive(ActivityJobKind kind) =>
        _activity.ActiveJobs.Any(j => j.Kind == kind && j.IsRunning);

    private void SortQueue()
    {
        var priority = _catalog.ToDictionary(d => d.Id, d => d.Priority);
        _queue.Sort((a, b) => priority.GetValueOrDefault(a, 99).CompareTo(priority.GetValueOrDefault(b, 99)));
    }

    // ----- row projection -----

    private void RebuildRows()
    {
        var states = _store.LoadAll().ToDictionary(s => s.TaskId);
        var now = _now();
        var rows = new List<ScheduledTaskRow>();

        foreach (var d in _catalog)
        {
            if (!states.TryGetValue(d.Id, out var state))
            {
                state = new ScheduledTaskState
                {
                    TaskId = d.Id,
                    Mode = d.DefaultMode,
                    IntervalHours = (int)Math.Round(d.DefaultInterval.TotalHours),
                    DailyAtMinutes = 180,
                    Enabled = d.DefaultEnabled,
                };
            }

            bool running, queued;
            lock (_gate)
            {
                queued = _queue.Contains(d.Id);
                running = _inFlight.Contains(d.Id) && !queued;
            }

            var next = SchedulerDueLogic.NextRun(state, now);
            rows.Add(new ScheduledTaskRow
            {
                TaskId = d.Id,
                DisplayName = d.DisplayName,
                Description = d.Description,
                Enabled = state.Enabled,
                Mode = state.Mode,
                IntervalHours = Math.Max(1, state.IntervalHours),
                DailyAtTime = TimeSpan.FromMinutes(Math.Clamp(state.DailyAtMinutes, 0, 1439)),
                LastRunUtc = state.LastRunUtc,
                LastRunStatus = state.LastRunStatus,
                IsRunning = running,
                IsQueued = queued,
                NextRunLabel = NextRunLabel(state, next, running, queued),
            });
        }

        _rows = rows;
        _dispatch(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    private void RefreshRunStatus()
    {
        // ActivityService changed (a job settled elsewhere) - re-project so IsRunning/IsQueued track.
        RebuildRows();
    }

    private static string NextRunLabel(ScheduledTaskState state, DateTimeOffset? next, bool running, bool queued)
    {
        if (running)
        {
            return "Running now";
        }

        if (queued)
        {
            return "Queued";
        }

        if (!state.Enabled)
        {
            return "Off";
        }

        if (next is null)
        {
            return "On next launch";
        }

        var delta = next.Value - DateTimeOffset.UtcNow;
        if (delta <= TimeSpan.Zero)
        {
            return "Due now";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return $"in {(int)delta.TotalMinutes} min";
        }

        if (delta < TimeSpan.FromDays(1))
        {
            return $"in {(int)delta.TotalHours} h";
        }

        return $"in {(int)delta.TotalDays} d";
    }

    // ----- notification level -----

    private ScheduledTaskNotificationLevel CurrentNotificationLevel()
    {
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            return context.GetOrCreateAppSettings().ScheduledTaskNotificationLevel;
        }
        catch
        {
            return ScheduledTaskNotificationLevel.OnlyFailures;
        }
    }

    private static ActivityToastPolicy ToastPolicyForLevel(ScheduledTaskNotificationLevel level) => level switch
    {
        ScheduledTaskNotificationLevel.EveryRun => ActivityToastPolicy.Always,
        ScheduledTaskNotificationLevel.Never => ActivityToastPolicy.Never,
        _ => ActivityToastPolicy.FailuresOnly,
    };

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
}
