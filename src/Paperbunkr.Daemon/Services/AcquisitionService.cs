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
public sealed class AcquisitionService(AcquisitionCycle cycle, Func<PaperbunkrDbContext> createContext, Func<DateTime>? now = null) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);
    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);
    private int _runRequested;
    private DateTime _lastRun = DateTime.MinValue;

    /// <summary>"Search now": runs a cycle at the next tick (within a minute) regardless of the poll interval.</summary>
    public void RunNow() => Interlocked.Exchange(ref _runRequested, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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

    /// <summary>One timer tick. Public so tests (and "Search now" callers that don't want to wait) can drive it directly.</summary>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        bool manual = Interlocked.Exchange(ref _runRequested, 0) == 1;

        if (!manual && !IntervalElapsed())
        {
            return;
        }

        _lastRun = _now();
        await cycle.RunAsync(manual, cancellationToken).ConfigureAwait(false);
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
