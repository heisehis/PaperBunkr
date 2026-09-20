using Microsoft.Extensions.Hosting;
using Paperbunkr.Data;

namespace Paperbunkr.Daemon.Services;

/// <summary>
/// The daemon's timer (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §3). A <see cref="PeriodicTimer"/> ticks
/// once a minute; each tick runs an <see cref="AcquisitionCycle"/> when the user's poll interval has elapsed (read live, so a
/// settings change takes effect without a restart) or when <see cref="RunNow"/> was called. After an unreachable indexer the wait
/// backs off exponentially (interval x 2, x 4, ... capped at 8x), so a down Prowlarr isn't hammered.
/// <para>A plain <see cref="BackgroundService"/>: the app has no generic host, so it calls <c>StartAsync</c>/<c>StopAsync</c> itself, as it does for the scheduler.</para>
/// </summary>
public sealed class AcquisitionService(AcquisitionCycle cycle, Func<PaperbunkrDbContext> createContext, Func<DateTime>? now = null, DownloadTracker? downloads = null) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    /// <summary>How often running downloads are checked. Cheap (one local API call), and only while something is downloading.</summary>
    private static readonly TimeSpan DownloadTick = TimeSpan.FromSeconds(10);
    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _runRequested;
    private DateTime _lastRun = DateTime.MinValue;

    /// <summary>"Search now", deferred: the next timer tick runs a manual cycle regardless of the poll interval.</summary>
    public void RunNow() => Interlocked.Exchange(ref _runRequested, 1);

    /// <summary>
    /// "Search now", immediate: runs a manual cycle right away. If a cycle is already running this waits for it to finish first, so two
    /// cycles never overlap (they would both search and rewrite the same candidates).
    /// </summary>
    public async Task RunNowAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Interlocked.Exchange(ref _runRequested, 0);
            _lastRun = _now();
            await cycle.RunAsync(manual: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Two independent loops: the slow search cycle, and the fast download follower (which does nothing when nothing is downloading).
        await Task.WhenAll(SearchLoopAsync(stoppingToken), DownloadLoopAsync(stoppingToken)).ConfigureAwait(false);
    }

    private async Task SearchLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Tick);
        try
        {
            do
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task DownloadLoopAsync(CancellationToken stoppingToken)
    {
        if (downloads is null)
        {
            return;
        }

        using var timer = new PeriodicTimer(DownloadTick);
        try
        {
            do
            {
                await DownloadTickAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    /// <summary>One download-follower tick; public so tests can drive it. Runs only when a grab is in flight.</summary>
    public async Task DownloadTickAsync(CancellationToken cancellationToken)
    {
        if (downloads is null)
        {
            return;
        }

        // The tracker tells the host when the last download settles, so it must run one more time after the final one finishes.
        if (downloads.HasActiveDownloads() || downloads.HasPendingReport)
        {
            await downloads.TickAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>One timer tick. Public so tests (and "Search now" callers that don't want to wait) can drive it directly.</summary>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        // A tick that lands while a cycle (scheduled or "Search now") is still running is simply skipped.
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            bool manual = Interlocked.Exchange(ref _runRequested, 0) == 1;

            if (!manual && !IntervalElapsed())
            {
                return;
            }

            _lastRun = _now();
            await cycle.RunAsync(manual, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IntervalElapsed()
    {
        int minutes;
        bool enabled;
        using (var context = createContext())
        {
            var settings = context.GetOrCreateAcquisitionSettings();
            minutes = Math.Max(15, settings.PollIntervalMinutes);
            enabled = settings.Enabled;
        }

        if (!enabled)
        {
            return false;
        }

        int backoff = 1 << Math.Min(cycle.ConsecutiveIndexerFailures, 3);   // 1, 2, 4, 8
        return _now() - _lastRun >= TimeSpan.FromMinutes(minutes * backoff);
    }
}
