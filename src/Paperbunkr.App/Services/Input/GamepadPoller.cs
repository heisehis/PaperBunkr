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
/// Polls a controller about 60 times a second and hands each non-empty <see cref="GamepadFrame"/> to the reader (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md
/// section 3). It only runs between <see cref="Start"/> and <see cref="Stop"/>, which the reader screen calls while it is visible and its window is active, and while no
/// controller is connected it only probes the four slots every two seconds, so a machine without one pays almost nothing.
/// </summary>
public sealed class GamepadPoller
{
    /// <summary>How often the four slots are probed while no controller is connected.</summary>
    public static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(16);

    private readonly IGamepadSource _source;
    private readonly Action<GamepadFrame, TimeSpan> _onFrame;
    private readonly GamepadMapper _mapper = new();
    private DispatcherTimer? _timer;
    private long _lastTimestamp;
    private int _slot = -1;
    private TimeSpan _sinceProbe = ProbeInterval;

    public GamepadPoller(IGamepadSource source, Action<GamepadFrame, TimeSpan> onFrame)
    {
        _source = source;
        _onFrame = onFrame;
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
        _mapper.Reset();
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
                    _mapper.Reset();
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
            _mapper.Reset();
            _sinceProbe = TimeSpan.Zero;
            return;
        }

        var frame = _mapper.Update(state, elapsed);
        if (frame.HasAny)
        {
            _onFrame(frame, elapsed);
        }
    }
}
