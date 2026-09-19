using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Threading;
using Paperbunkr.App.Models;
using Paperbunkr.Daemon.Events;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// The only place the acquisition daemon's events meet the UI (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §3):
/// it drains the daemon's <see cref="ChannelReader{T}"/> and turns each <see cref="DaemonEvent"/> into Activity Center jobs and alerts.
/// The daemon never references the app; the app never reaches into the daemon's internals.
/// <para>
/// A scheduled cycle runs quietly (toast only on failure); a manual "Search now" always toasts its result. Alerts dedupe by the
/// daemon's own key, so a Prowlarr outage raises one alert however many cycles hit it, and the alert is dismissed when the daemon
/// reports the condition cleared.
/// </para>
/// </summary>
public sealed class AcquisitionActivityBridge : IDisposable
{
    private readonly IActivityService _activity;
    private readonly ChannelReader<DaemonEvent> _reader;
    private readonly Action<Action> _post;
    private readonly Func<ActivityLink?> _resultLink;
    private readonly CancellationTokenSource _cts = new();
    private IActivityJobHandle? _job;
    private Task? _pump;

    /// <param name="post">Marshals work onto the UI thread; defaults to the Avalonia dispatcher. Tests pass an inline executor.</param>
    /// <param name="resultLink">The link attached to a finished cycle (the Wanted screen); null until that screen exists.</param>
    public AcquisitionActivityBridge(IActivityService activity, ChannelReader<DaemonEvent> reader, Action<Action>? post = null, Func<ActivityLink?>? resultLink = null)
    {
        _activity = activity;
        _reader = reader;
        _post = post ?? (action => Dispatcher.UIThread.Post(action));
        _resultLink = resultLink ?? (() => null);
    }

    public void Start()
    {
        _pump ??= Task.Run(async () =>
        {
            try
            {
                await foreach (var daemonEvent in _reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
                {
                    _post(() => Handle(daemonEvent));
                }
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        });
    }

    /// <summary>Handles one event. UI-thread affine; public so tests can drive it directly.</summary>
    public void Handle(DaemonEvent daemonEvent)
    {
        switch (daemonEvent)
        {
            case CycleStartedEvent started:
                EndJobIfOpen();
                _job = _activity.StartJob(
                    ActivityJobKind.Acquisition,
                    "Checking for comics",
                    cancellable: false,
                    trigger: started.Manual ? ActivityTrigger.Manual : ActivityTrigger.Scheduled,
                    toastPolicy: started.Manual ? ActivityToastPolicy.Always : ActivityToastPolicy.FailuresOnly);
                break;

            case CycleProgressEvent progress:
                _job?.Report(progress.Done, progress.Total, progress.Detail);
                break;

            case CycleCompletedEvent completed:
                _job?.Succeed(completed.Summary, _resultLink(), completed.IssuesSearched);
                EndJobIfOpen();
                break;

            case CycleFailedEvent failed:
                _job?.Fail(failed.Message);
                EndJobIfOpen();
                break;

            case DaemonAlertEvent alert:
                _activity.RaiseAlert(new ActivityAlert
                {
                    Severity = alert.Severity switch
                    {
                        DaemonAlertSeverity.Error => ActivityAlertSeverity.Error,
                        DaemonAlertSeverity.Warning => ActivityAlertSeverity.Warning,
                        _ => ActivityAlertSeverity.Info,
                    },
                    Title = alert.Title,
                    Detail = alert.Detail,
                    DedupeKey = alert.Key,
                    ActionLabel = "Open settings",
                    ActionLink = new ActivityLink(ActivityLinkKind.Preferences, "Acquisition"),
                });
                break;

            case DaemonAlertClearedEvent cleared:
                var existing = _activity.Alerts.FirstOrDefault(a => a.DedupeKey == cleared.Key);
                if (existing is not null)
                {
                    _activity.DismissAlert(existing.Id);
                }

                break;
        }
    }

    private void EndJobIfOpen()
    {
        _job?.Dispose();
        _job = null;
    }

    public void Dispose()
    {
        _cts.Cancel();
        EndJobIfOpen();
        _cts.Dispose();
    }
}
