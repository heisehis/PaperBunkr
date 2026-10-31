using System;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;
using Paperbunkr.Data.Metadata.SeriesConnector;

namespace Paperbunkr.App.Services;

/// <summary>
/// Runs the series connector's local part after a library scan or a scrape (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md
/// §5.2-§5.3), off the UI thread. Overlapping triggers coalesce: while a run is going, further triggers ask for one more run, not one each.
/// It only reports to the Activity Center when relations were found or removed, so an ordinary scan adds no noise. Never fails the scan or scrape.
/// </summary>
public sealed class SeriesConnectorReactions : IDisposable
{
    private readonly LibraryEvents _events;
    private readonly IActivityService _activity;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Func<Func<Task>, Task> _run;
    private int _state; // 0 idle, 1 running, 2 running with another run wanted

    /// <param name="run">How work leaves the calling thread; tests pass a runner that executes inline.</param>
    public SeriesConnectorReactions(LibraryEvents events, IActivityService activity, Func<PaperbunkrDbContext>? contextFactory = null, Func<Func<Task>, Task>? run = null)
    {
        _events = events;
        _activity = activity;
        _contextFactory = contextFactory ?? PaperbunkrDb.CreateContext;
        _run = run ?? (work => Task.Run(work));
        _events.LibraryScanCompleted += OnScanCompleted;
        _events.ScrapeCompleted += OnScrapeCompleted;
    }

    public void Dispose()
    {
        _events.LibraryScanCompleted -= OnScanCompleted;
        _events.ScrapeCompleted -= OnScrapeCompleted;
    }

    private void OnScanCompleted(LibraryScanCompletedEvent e) => Trigger();

    private void OnScrapeCompleted(ScrapeCompletedEvent e)
    {
        if (e.SeriesIds.Count > 0)
        {
            Trigger();
        }
    }

    /// <summary>Starts a run, or - when one is going - asks for one more after it.</summary>
    public void Trigger()
    {
        // 0 → 1 starts a run; 1 → 2 marks "again"; 2 stays 2.
        while (true)
        {
            int state = Volatile.Read(ref _state);
            int next = state == 0 ? 1 : 2;
            if (Interlocked.CompareExchange(ref _state, next, state) != state)
            {
                continue;
            }

            if (state == 0)
            {
                _ = _run(RunLoopAsync);
            }

            return;
        }
    }

    private Task RunLoopAsync()
    {
        do
        {
            Volatile.Write(ref _state, 1);
            try
            {
                // Already off the UI thread (_run); synchronous so a test's inline runner finishes the run before Trigger returns.
                var summary = SeriesConnectorRunner.RunLocal(_contextFactory);
                if (summary is { Changed: true })
                {
                    Report(summary);
                }
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogMilestone($"Series connector after scan/scrape failed ({ex.GetType().Name}: {ex.Message}).");
            }
        }
        while (Interlocked.CompareExchange(ref _state, 0, 1) != 1);

        return Task.CompletedTask;
    }

    private void Report(SeriesConnectorSummary summary)
    {
        using var job = _activity.StartJob(ActivityJobKind.SyncMetadata, "Finding related series", cancellable: false,
            trigger: ActivityTrigger.Watch, toastPolicy: ActivityToastPolicy.FailuresOnly);
        job.Begin();
        job.Succeed(summary.Text);
    }
}
