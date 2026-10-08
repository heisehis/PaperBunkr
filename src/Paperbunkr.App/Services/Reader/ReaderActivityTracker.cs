using System;
using Avalonia.Threading;

namespace Paperbunkr.App.Services.Reader;

/// <summary>
/// Active reading time for one item in a reader that has no session clock of its own - the EPUB and PDF readers
/// (docs/superpowers/specs/2026-10-06-smart-features-design.md §4.1). Wraps the comic reader's <see cref="ReadingSessionClock"/>, so
/// "active" means the same thing everywhere: time counts only while input arrived within <see cref="ReadingSessionClock.IdleCutoff"/>.
/// These readers have no window-activation signal, so the user counts as present for as long as the item is open and idle time is what
/// stops the clock. Driven by a 10-second timer; <see cref="Tick"/> and the clock are public to tests, which pass their own time.
/// </summary>
public sealed class ReaderActivityTracker
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(10);

    private readonly ReadingSessionClock _clock = new();
    private readonly Func<DateTime> _now;
    private readonly bool _useTimer;
    private DispatcherTimer? _timer;
    private bool _running;

    /// <param name="now">The current time (a test seam).</param>
    /// <param name="useTimer">False in tests that drive <see cref="Tick"/> themselves.</param>
    public ReaderActivityTracker(Func<DateTime>? now = null, bool useTimer = true)
    {
        _now = now ?? (() => DateTime.Now);
        _useTimer = useTimer;
    }

    /// <summary>Starts counting for a newly opened item; anything counted before is forgotten.</summary>
    public void Start()
    {
        var now = _now();
        _clock.Reset(now);
        _running = true;
        if (!_useTimer)
        {
            return;
        }

        if (_timer is null)
        {
            _timer = new DispatcherTimer { Interval = TickInterval };
            _timer.Tick += (_, _) => Tick();
        }

        _timer.Start();
    }

    /// <summary>The user did something (turned a page, scrolled): the idle countdown starts again.</summary>
    public void NoteInput()
    {
        if (_running)
        {
            _clock.NoteInput(_now());
        }
    }

    /// <summary>Advances the clock to now.</summary>
    public void Tick()
    {
        if (_running)
        {
            _clock.Tick(_now(), present: true);
        }
    }

    /// <summary>Stops counting and returns the whole seconds of active time since <see cref="Start"/> (0 if it was never started).</summary>
    public int Stop()
    {
        if (!_running)
        {
            return 0;
        }

        Tick();
        _running = false;
        _timer?.Stop();
        return (int)Math.Floor(_clock.ActiveTime.TotalSeconds);
    }
}
