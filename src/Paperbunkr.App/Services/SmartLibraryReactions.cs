using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services;

/// <summary>
/// The app's own reactions to two library events (docs/superpowers/specs/2026-10-06-smart-features-design.md §7.3, §7.4), kept apart
/// from the plugin host, which only relays events to plugins:
/// <list type="bullet">
/// <item>An issue was read to the end → check whether that finished a continuity or a story event; if so, announce it (the
/// <see cref="LibraryEvents.CollectionCompleted"/> event, which the <c>ContinuityCompleted</c> plugin hook follows) and tell the
/// reader in the Activity Center.</item>
/// <item>A scan completed → re-arm any finished collection that has unread issues again.</item>
/// <item>A linked series became Completed → refresh its provider data once, as an Activity Center job.</item>
/// </list>
/// All of it runs off the UI thread and none of it can fail the action that triggered it.
/// </summary>
public sealed class SmartLibraryReactions : IDisposable
{
    private readonly IReadingEventRecorder? _recorder;
    private readonly LibraryEvents _events;
    private readonly IActivityService _activity;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Func<int, CancellationToken, Task<string>> _refreshProviderData;
    private readonly Func<PaperbunkrDbContext, int, bool> _canRefresh;
    private readonly Func<Func<Task>, Task> _run;
    private readonly ConcurrentDictionary<int, byte> _finalizing = new();

    /// <param name="refreshProviderData">Refreshes one linked series' provider data and returns a one-line summary.</param>
    /// <param name="canRefresh">Whether the series has a provider link worth a network call.</param>
    /// <param name="run">How work leaves the calling thread; tests pass a runner that executes inline.</param>
    public SmartLibraryReactions(
        IReadingEventRecorder? recorder,
        LibraryEvents events,
        IActivityService activity,
        Func<int, CancellationToken, Task<string>> refreshProviderData,
        Func<PaperbunkrDbContext, int, bool> canRefresh,
        Func<PaperbunkrDbContext>? contextFactory = null,
        Func<Func<Task>, Task>? run = null)
    {
        _recorder = recorder;
        _events = events;
        _activity = activity;
        _refreshProviderData = refreshProviderData;
        _canRefresh = canRefresh;
        _contextFactory = contextFactory ?? PaperbunkrDb.CreateContext;
        _run = run ?? (work => Task.Run(work));

        if (_recorder is not null)
        {
            _recorder.ReadingFinished += OnReadingFinished;
        }

        _events.LibraryScanCompleted += OnLibraryScanCompleted;
        _events.SeriesStatusChanged += OnSeriesStatusChanged;
    }

    public void Dispose()
    {
        if (_recorder is not null)
        {
            _recorder.ReadingFinished -= OnReadingFinished;
        }

        _events.LibraryScanCompleted -= OnLibraryScanCompleted;
        _events.SeriesStatusChanged -= OnSeriesStatusChanged;
    }

    // ----- Completion (§7.4) -----

    private void OnReadingFinished(ReadingEvent finished)
    {
        if (finished.ItemType != ReadingItemType.Comic)
        {
            return;
        }

        int issueId = finished.ItemId;
        _ = _run(() =>
        {
            try
            {
                using var context = _contextFactory();
                foreach (var completed in CollectionCompletion.OnIssueFinished(context, issueId, DateTime.UtcNow))
                {
                    _events.Raise(completed);
                    _activity.RaiseAlert(new ActivityAlert
                    {
                        Severity = ActivityAlertSeverity.Info,
                        Title = $"You finished {completed.Name}",
                        Detail = completed.Kind == CompletedCollectionKind.Continuity
                            ? $"Every issue of every series in this continuity is read ({completed.IssueCount:N0} issues)."
                            : $"Every issue of this story event is read ({completed.IssueCount:N0} issues).",
                        ActionLabel = "Open Continuity",
                        ActionLink = new ActivityLink(ActivityLinkKind.StoryEventsScreen, string.Empty),
                        DedupeKey = $"collection-completed:{completed.Kind}:{completed.Id}",
                    });
                }
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogMilestone($"Completion check failed ({ex.GetType().Name}: {ex.Message}).");
            }

            return Task.CompletedTask;
        });
    }

    private void OnLibraryScanCompleted(LibraryScanCompletedEvent scan)
    {
        _ = _run(() =>
        {
            try
            {
                using var context = _contextFactory();
                CollectionCompletion.Rearm(context);
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogMilestone($"Completion re-arm failed ({ex.GetType().Name}: {ex.Message}).");
            }

            return Task.CompletedTask;
        });
    }

    // ----- Finalize a completed series (§7.3) -----

    private void OnSeriesStatusChanged(SeriesStatusChangedEvent change)
    {
        if (change.NewStatus != SeriesStatus.Completed || change.OldStatus == SeriesStatus.Completed)
        {
            return;
        }

        // One job per series at a time: bulk-editing a series twice must not queue two refreshes of it.
        if (!_finalizing.TryAdd(change.SeriesId, 0))
        {
            return;
        }

        _ = _run(async () =>
        {
            try
            {
                bool wanted;
                using (var context = _contextFactory())
                {
                    wanted = context.GetOrCreateAppSettings().RefreshProviderDataOnComplete
                             && _canRefresh(context, change.SeriesId);
                }

                // Not linked to a provider (or switched off): nothing to refresh, and no network call.
                if (!wanted)
                {
                    return;
                }

                using var job = _activity.StartJob(
                    ActivityJobKind.SyncMetadata, $"Refreshing {change.SeriesName}", cancellable: true,
                    trigger: ActivityTrigger.Watch, toastPolicy: ActivityToastPolicy.FailuresOnly);
                try
                {
                    job.Begin();
                    job.Report("The series was marked completed; refreshing its provider data…");
                    string summary = await _refreshProviderData(change.SeriesId, job.CancellationToken).ConfigureAwait(false);
                    job.Succeed(summary, new ActivityLink(ActivityLinkKind.SeriesDetail, change.SeriesId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                }
                catch (OperationCanceledException)
                {
                    job.Fail("Cancelled.");
                }
                catch (Exception ex)
                {
                    // Reported once, here; never retried on its own.
                    job.Fail($"Couldn't refresh {change.SeriesName}: {ex.Message}", ex: ex);
                }
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogMilestone($"Series finalize failed ({ex.GetType().Name}: {ex.Message}).");
            }
            finally
            {
                _finalizing.TryRemove(change.SeriesId, out _);
            }
        });
    }
}
