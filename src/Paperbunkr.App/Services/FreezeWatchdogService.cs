using System;
using System.Threading;
using Avalonia.Threading;

namespace Paperbunkr.App.Services;

/// <summary>
/// Detects a frozen Avalonia UI thread and reports it, mirroring ComicRackCE's
/// <c>cYo.Common.Runtime.CrashWatchDog</c>'s background lock-watcher thread (verified from its
/// source: a 1s poll, a 10s stuck-threshold). Deliberately detect-and-report only, no forced
/// unblock - Avalonia has no equivalent to CE's Win32-specific lock-breaking trick, and a fake
/// "Retry" that can't actually unstick anything would be worse than not offering one. See
/// docs/superpowers/specs/2026-08-23-app-chrome-crash-reporter-and-tray-design.md §3.
/// </summary>
public sealed class FreezeWatchdogService : IDisposable
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan DefaultFreezeThreshold = TimeSpan.FromSeconds(10);

    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _freezeThreshold;
    private readonly Func<TimeSpan, bool> _pingUiThread;
    private readonly Action _onFrozen;
    private readonly Func<DateTime> _clock;
    private DateTime _lastResponse;
    private Thread? _thread;
    private volatile bool _running;

    /// <summary>
    /// All parameters are injectable so tests can drive <see cref="Tick"/> directly with a fake
    /// clock/ping/frozen-callback instead of waiting on real background threads and real 10-second
    /// timeouts.
    /// </summary>
    public FreezeWatchdogService(
        Func<TimeSpan, bool>? pingUiThread = null,
        Action? onFrozen = null,
        Func<DateTime>? clock = null,
        TimeSpan? pollInterval = null,
        TimeSpan? freezeThreshold = null)
    {
        _pingUiThread = pingUiThread ?? DefaultPingUiThread;
        _onFrozen = onFrozen ?? DefaultOnFrozen;
        _clock = clock ?? (() => DateTime.UtcNow);
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _freezeThreshold = freezeThreshold ?? DefaultFreezeThreshold;
        // Set here, not just in Start() - Tick() is called directly (by tests, bypassing Start()
        // entirely) and must not see the DateTime.MinValue default, which would make the very first
        // tick look like it's already been stuck for decades.
        _lastResponse = _clock();
    }

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _lastResponse = _clock();
        _thread = new Thread(RunLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest,
            Name = "Paperbunkr Freeze Watchdog",
        };
        _thread.Start();
    }

    public void Stop() => _running = false;

    public void Dispose() => Stop();

    private void RunLoop()
    {
        while (_running)
        {
            Tick();
            Thread.Sleep(_pollInterval);
        }
    }

    /// <summary>
    /// One poll cycle - <c>internal</c> so tests can call it directly, bypassing the real
    /// thread/sleep loop entirely.
    /// </summary>
    internal void Tick()
    {
        bool responded;
        try
        {
            responded = _pingUiThread(_pollInterval);
        }
        catch
        {
            responded = false;
        }

        DateTime now = _clock();
        if (responded)
        {
            _lastResponse = now;
            return;
        }

        if (now - _lastResponse <= _freezeThreshold)
        {
            return;
        }

        // Reset before firing (matching CE's own LockWatcher) so a still-stuck thread waits a full
        // threshold before the notification fires again, instead of re-firing every single poll.
        _lastResponse = now;
        try
        {
            _onFrozen();
        }
        catch
        {
            // A failure in the notification path must never take down the watchdog thread itself.
        }
    }

    private static bool DefaultPingUiThread(TimeSpan timeout)
    {
        using var responded = new ManualResetEventSlim(false);
        try
        {
            Dispatcher.UIThread.Post(() => responded.Set(), DispatcherPriority.Send);
        }
        catch
        {
            return false;
        }

        return responded.Wait(timeout);
    }

    private static void DefaultOnFrozen()
    {
        DiagnosticsService.LogCrash("FreezeWatchdog", exception: null, isTerminating: false);
        if (NativeMessageBox.ShowNotResponding() == NativeMessageBoxResult.ForceExit)
        {
            Environment.Exit(1);
        }
    }
}
