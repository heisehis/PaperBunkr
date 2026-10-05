using System;
using System.Diagnostics;
using Avalonia.Threading;

namespace Paperbunkr.App.Services.Input;

/// <summary>Where controller snapshots come from (XInput on Windows; a fake in tests).</summary>
public interface IGamepadSource
{
    /// <summary>The current snapshot of controller <paramref name="slot"/> (0-3), or <see langword="false"/> when none is connected there.</summary>
    bool TryGetState(int slot, out GamepadState state);
}

/// <summary>
/// Polls a controller about 60 times a second and hands every snapshot to the input service (docs/superpowers/specs/2026-10-03-input-service-design.md §5.5), which does the edge
/// detection, key-repeat and binding lookup. Every poll is delivered while a controller is connected, not just the ones with something pressed: releasing a button has to reach
/// the service for the next press to count as a new one. It only runs between <see cref="Start"/> and <see cref="Stop"/>, which the owning screen calls while it is visible and
/// its window is active, and while no controller is connected it only probes the four slots every two seconds, so a machine without one pays almost nothing.
/// </summary>
public sealed class GamepadPoller
{
    /// <summary>How often the four slots are probed while no controller is connected.</summary>
    public static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(16);

    private readonly IGamepadSource _source;
    private readonly Action<GamepadState, TimeSpan> _onState;
    private readonly Action? _onReset;
    private DispatcherTimer? _timer;
    private long _lastTimestamp;
    private int _slot = -1;
    private TimeSpan _sinceProbe = ProbeInterval;

    /// <param name="source">Where snapshots come from.</param>
    /// <param name="onState">Receives each snapshot with the time since the previous one.</param>
    /// <param name="onReset">Called when the controller disappears or the poller stops, so held buttons are forgotten.</param>
    public GamepadPoller(IGamepadSource source, Action<GamepadState, TimeSpan> onState, Action? onReset = null)
    {
        _source = source;
        _onState = onState;
        _onReset = onReset;
    }

    public bool IsRunning { get; private set; }

    /// <summary>The slot of the connected controller, or -1.</summary>
    public int ConnectedSlot => _slot;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        _sinceProbe = ProbeInterval;
        _lastTimestamp = Stopwatch.GetTimestamp();
        _timer ??= CreateTimer();
        _timer.Start();
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        _timer?.Stop();
        _onReset?.Invoke();
        _slot = -1;
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer { Interval = PollInterval };
        timer.Tick += (_, _) =>
        {
            long now = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(_lastTimestamp, now);
            _lastTimestamp = now;
            Poll(elapsed);
        };
        return timer;
    }

    /// <summary>One poll step; the timer calls it, tests call it directly with their own elapsed time.</summary>
    internal void Poll(TimeSpan elapsed)
    {
        if (_slot < 0)
        {
            _sinceProbe += elapsed;
            if (_sinceProbe < ProbeInterval)
            {
                return;
            }

            _sinceProbe = TimeSpan.Zero;
            for (int slot = 0; slot < 4; slot++)
            {
                if (_source.TryGetState(slot, out _))
                {
                    _slot = slot;
                    _onReset?.Invoke();
                    break;
                }
            }

            if (_slot < 0)
            {
                return;
            }
        }

        if (!_source.TryGetState(_slot, out var state))
        {
            _slot = -1;
            _onReset?.Invoke();
            _sinceProbe = TimeSpan.Zero;
            return;
        }

        _onState(state, elapsed);
    }
}
