using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Paperbunkr.App.Services;

/// <summary>
/// Detects a frozen Avalonia UI thread and reports it (docs/superpowers/specs/2026-10-21-freeze-
/// watchdog-redesign-design.md). A background thread pings the UI thread every second:
/// <list type="bullet">
///   <item><b>Stall</b> (no answer for over 10s): logged quietly, with a managed stack snapshot. No UI.</item>
///   <item><b>Freeze</b> (over 30s): crash log plus the "not responding" dialog (<see cref="IFreezeDialog"/>),
///     which closes itself if the UI thread answers again. Keep waiting re-arms a full 30s from the click.</item>
///   <item><b>Recovery</b>: the stall's length is logged; if the dialog was shown, an Activity Center alert follows.</item>
/// </list>
///
/// <para>Modelled on ComicRackCE's <c>cYo.Common.Runtime.CrashWatchDog</c> lock watcher (1s poll). CE's class
/// defaults to a 10s threshold, but <c>Program.cs</c> starts it via
/// <c>Diagnostic.StartWatchDog(CrashDialog.OnBark)</c> with the default <c>lockTestTimeSeconds = 0</c>, and its
/// loop's <c>now - d &gt; LockTestTime</c> guard then resets the clock every poll - so the shipped CE never
/// actually shows a lock dialog. Kept from CE: no second dialog while one is open (its <c>inBark</c>), and a
/// reset when the watcher's own loop gap is too long (machine sleep). The 30s dialog threshold is a deliberate
/// deviation (2026-10-21: 10s fired on long-but-recovering work such as big scans and plugin warm-up).
/// Detect-and-report only, no forced unblock - Avalonia has no equivalent to CE's Win32 lock-breaking trick.</para>
/// </summary>
public sealed class FreezeWatchdogService : IDisposable
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan DefaultStallThreshold = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultFreezeThreshold = TimeSpan.FromSeconds(30);

    /// <summary>Managed stack snapshots per session (dotnet-stack is heavy; one per episode, at most this many).</summary>
    public const int MaxStackCapturesPerSession = 3;

    private readonly object _lock = new();
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _stallThreshold;
    private readonly TimeSpan _freezeThreshold;
    private readonly Func<TimeSpan, bool> _pingUiThread;
    private readonly Func<DateTime> _clock;
    private readonly Func<bool> _isDebuggerAttached;
    private readonly IFreezeDialog _dialog;
    private readonly Action<string> _logMilestone;
    private readonly Action _logFreeze;
    private readonly Action _captureStack;
    private readonly Action<TimeSpan>? _onRecoveredAfterFreeze;
    private readonly Action<int> _exit;

    private DateTime _lastResponse;
    private DateTime _armFrom;
    private DateTime? _lastTick;
    private bool _stallReported;
    private bool _frozeThisEpisode;
    private bool _dialogOpen;
    private int _stackCaptures;
    private Thread? _thread;
    private volatile bool _running;

    /// <summary>
    /// Every collaborator is injectable so tests drive <see cref="Tick"/> directly with a fake clock, ping,
    /// dialog and sinks instead of real threads, real timeouts, real log files or a real dotnet-stack run.
    /// </summary>
    public FreezeWatchdogService(
        Func<TimeSpan, bool>? pingUiThread = null,
        IFreezeDialog? dialog = null,
        Func<DateTime>? clock = null,
        TimeSpan? pollInterval = null,
        TimeSpan? stallThreshold = null,
        TimeSpan? freezeThreshold = null,
        Func<bool>? isDebuggerAttached = null,
        Action<string>? logMilestone = null,
        Action? logFreeze = null,
        Action? captureStack = null,
        Action<TimeSpan>? onRecoveredAfterFreeze = null,
        Action<int>? exit = null)
    {
        _pingUiThread = pingUiThread ?? DefaultPingUiThread;
        _dialog = dialog ?? new NativeFreezeDialog();
        _clock = clock ?? (() => DateTime.UtcNow);
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _stallThreshold = stallThreshold ?? DefaultStallThreshold;
        _freezeThreshold = freezeThreshold ?? DefaultFreezeThreshold;
        _isDebuggerAttached = isDebuggerAttached ?? (() => Debugger.IsAttached);
        _logMilestone = logMilestone ?? DiagnosticsService.LogMilestone;
        _logFreeze = logFreeze ?? (() => DiagnosticsService.LogCrash("FreezeWatchdog", exception: null, isTerminating: false));
        _captureStack = captureStack ?? (() => Task.Run(TryCaptureManagedStack));
        _onRecoveredAfterFreeze = onRecoveredAfterFreeze;
        _exit = exit ?? Environment.Exit;

        // Set here, not just in Start() - Tick() is called directly by tests and must not see the
        // DateTime.MinValue default, which would make the first tick look stuck for decades.
        _lastResponse = _armFrom = _clock();
    }

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        lock (_lock)
        {
            _lastResponse = _armFrom = _clock();
            _lastTick = null;
        }

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
    /// One poll cycle - <c>internal</c> so tests can call it directly, bypassing the real thread/sleep loop.
    /// Never throws. Callbacks run outside the lock (the dialog's close callback takes it too).
    /// </summary>
    internal void Tick()
    {
        bool responded;
        try
        {
            responded = _isDebuggerAttached() || _pingUiThread(_pollInterval);
        }
        catch
        {
            responded = false;
        }

        var actions = new List<Action>();
        lock (_lock)
        {
            DateTime now = _clock();
            bool slept = _lastTick is { } previous && now - previous > _stallThreshold;
            _lastTick = now;

            if (slept)
            {
                // The watcher itself didn't run for longer than a stall (machine sleep, suspended
                // process) - that gap says nothing about the UI thread. Start over (CE's `now - d` guard).
                ResetEpisode(now);
            }
            else if (responded)
            {
                if (_stallReported || _frozeThisEpisode)
                {
                    TimeSpan duration = now - _lastResponse;
                    actions.Add(() => _logMilestone($"FreezeWatchdog: UI thread recovered after {FreezeDurationText.Format(duration)}"));
                    if (_frozeThisEpisode)
                    {
                        if (_dialogOpen)
                        {
                            actions.Add(_dialog.RequestClose);
                        }

                        if (_onRecoveredAfterFreeze is { } onRecovered)
                        {
                            actions.Add(() => onRecovered(duration));
                        }
                    }
                }

                ResetEpisode(now);
            }
            else
            {
                if (!_stallReported && now - _lastResponse > _stallThreshold)
                {
                    _stallReported = true;
                    string stalledFor = FreezeDurationText.Format(_stallThreshold);
                    actions.Add(() => _logMilestone($"FreezeWatchdog: UI thread stalled (no answer for {stalledFor})"));
                    if (_stackCaptures < MaxStackCapturesPerSession)
                    {
                        _stackCaptures++;
                        actions.Add(_captureStack);
                    }
                }

                if (!_dialogOpen && now - _armFrom > _freezeThreshold)
                {
                    if (!_frozeThisEpisode)
                    {
                        actions.Add(_logFreeze);
                    }

                    _frozeThisEpisode = true;
                    _dialogOpen = true;
                    actions.Add(() => _dialog.Show(ElapsedSinceLastResponse, OnDialogClosed));
                }
            }
        }

        foreach (Action action in actions)
        {
            try
            {
                action();
            }
            catch
            {
                // A failure in a logging/notification path must never take down the watchdog thread.
            }
        }
    }

    private void ResetEpisode(DateTime now)
    {
        _lastResponse = now;
        _armFrom = now;
        _stallReported = false;
        _frozeThisEpisode = false;
    }

    private TimeSpan ElapsedSinceLastResponse()
    {
        lock (_lock)
        {
            return _clock() - _lastResponse;
        }
    }

    /// <summary>Runs on the dialog's own thread (or synchronously in tests).</summary>
    private void OnDialogClosed(FreezeDialogResult result)
    {
        lock (_lock)
        {
            _dialogOpen = false;
            if (result == FreezeDialogResult.KeepWaiting)
            {
                // Ask again only after another full threshold, counted from the click.
                _armFrom = _clock();
            }
        }

        try
        {
            switch (result)
            {
                case FreezeDialogResult.KeepWaiting:
                    _logMilestone("FreezeWatchdog: user chose Keep waiting");
                    break;
                case FreezeDialogResult.Close:
                    _logMilestone("FreezeWatchdog: user chose Close Paperbunkr");
                    _exit(1);
                    break;
            }
        }
        catch
        {
            // Same rule as Tick: never let a notification failure escape.
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

    /// <summary>
    /// Captures the frozen UI thread's managed stack via out-of-process dotnet-stack, which reads the runtime
    /// over EventPipe and works even when the UI thread never yields. Runs on a background task (it can take
    /// up to 30s). Best-effort: if the tool isn't installed or times out, the stall log entry still lands.
    /// </summary>
    private static void TryCaptureManagedStack()
    {
        try
        {
            string? tool = ResolveDotnetStackPath();
            if (tool is null)
            {
                DiagnosticsService.LogMilestone("FreezeWatchdog: dotnet-stack not found - no managed stack captured");
                return;
            }

            string outPath = System.IO.Path.Combine(
                DiagnosticsService.LogDirectory,
                $"freeze-stack-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

            var psi = new ProcessStartInfo(tool)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "report", "-p", Environment.ProcessId.ToString() },
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return;
            }

            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(30_000);
            System.IO.File.WriteAllText(outPath, output);
            DiagnosticsService.LogMilestone($"FreezeWatchdog: managed stack captured -> {System.IO.Path.GetFileName(outPath)}");
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"FreezeWatchdog: managed stack capture failed - {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string? ResolveDotnetStackPath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates =
        {
            System.IO.Path.Combine(home, ".dotnet", "tools", "dotnet-stack.exe"),
            System.IO.Path.Combine(home, ".dotnet", "tools", "dotnet-stack"),
        };

        foreach (string candidate in candidates)
        {
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Fall back to PATH resolution.
        return "dotnet-stack";
    }
}
