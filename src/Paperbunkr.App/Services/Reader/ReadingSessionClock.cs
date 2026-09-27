using System;
using System.Collections.Generic;

namespace Paperbunkr.App.Services.Reader;

/// <summary>
/// The reading session tracker behind the reader's stats HUD and break nudges (docs/superpowers/specs/2026-09-25-comic-reader-comfort-design.md sections 1 and 3). The pitch has the
/// HUD read the <c>ReadingEvent</c> log, but that log only holds Opened/Finished rows with a page delta (no per-page timing), so this tracks the visit live in memory.
/// <para>
/// It accrues <b>active time</b> only while the user is present (the reader is showing and its window is active) <b>and</b> was last active less than
/// <see cref="IdleCutoff"/> ago. A stretch with no presence or no input for that long is a <em>break</em>: <see cref="ActiveSinceBreak"/> resets. Pure and driven by explicit
/// timestamps, so it is tested without a real clock.
/// </para>
/// </summary>
public sealed class ReadingSessionClock
{
    /// <summary>No input for this long pauses the clock and counts as a break.</summary>
    public static readonly TimeSpan IdleCutoff = TimeSpan.FromMinutes(2);

    /// <summary>Away (not present) for this long starts a whole new visit.</summary>
    public static readonly TimeSpan NewVisitAfter = TimeSpan.FromMinutes(30);

    /// <summary>The pace is only reported after this many distinct pages...</summary>
    public const int MinPagesForPace = 3;

    /// <summary>...and this much active time, so a first glance never shows a wild number.</summary>
    public static readonly TimeSpan MinActiveForPace = TimeSpan.FromMinutes(2);

    private readonly HashSet<(int IssueId, int PageIndex)> _pages = new();
    private DateTime? _lastTick;
    private DateTime _lastInput;
    private DateTime? _awaySince;
    private bool _wasBreak;

    /// <summary>Time the user was actively reading this visit.</summary>
    public TimeSpan ActiveTime { get; private set; }

    /// <summary>Active time since the last break (or the start of the visit).</summary>
    public TimeSpan ActiveSinceBreak { get; private set; }

    /// <summary>Distinct pages seen this visit (a page seen twice, or a page jumped over, counts once or not at all).</summary>
    public int PagesViewed => _pages.Count;

    /// <summary>Pages per active minute, or null until <see cref="MinPagesForPace"/> pages and <see cref="MinActiveForPace"/> of active time.</summary>
    public double? PagesPerMinute =>
        _pages.Count >= MinPagesForPace && ActiveTime >= MinActiveForPace ? _pages.Count / ActiveTime.TotalMinutes : null;

    /// <summary>Raised when a break has just been detected (idle cutoff reached, or presence returned after one). Handlers reset their own per-break state.</summary>
    public event Action? BreakStarted;

    /// <summary>Records that the user did something (page turn, key, click) at <paramref name="now"/>.</summary>
    public void NoteInput(DateTime now)
    {
        _lastInput = now;
    }

    /// <summary>Records that <paramref name="pageIndex"/> of issue <paramref name="issueId"/> was shown. Cheap: a hash-set add.</summary>
    public void NotePageViewed(int issueId, int pageIndex)
    {
        _pages.Add((issueId, pageIndex));
    }

    /// <summary>
    /// Advances the clock to <paramref name="now"/>. <paramref name="present"/> is true while the reader is showing and its window is active. Call it regularly (every few
    /// seconds); the time since the previous tick is credited only if the user was present and had given input within <see cref="IdleCutoff"/> of the previous tick.
    /// </summary>
    public void Tick(DateTime now, bool present)
    {
        if (_lastTick is null)
        {
            _lastTick = now;
            _lastInput = now;
            if (!present)
            {
                _awaySince = now;
            }

            return;
        }

        var elapsed = now - _lastTick.Value;
        _lastTick = now;
        if (elapsed <= TimeSpan.Zero)
        {
            return;
        }

        if (!present)
        {
            _awaySince ??= now - elapsed;
            if (now - _awaySince.Value >= NewVisitAfter)
            {
                Reset(now);
            }
            else if (!_wasBreak && now - _awaySince.Value >= IdleCutoff)
            {
                StartBreak();
            }

            return;
        }

        if (_awaySince is not null)
        {
            // Back after being away: an absence of two minutes or more was a break.
            bool longAway = now - _awaySince.Value >= IdleCutoff;
            _awaySince = null;
            if (longAway && !_wasBreak)
            {
                StartBreak();
            }

            _lastInput = now;   // coming back counts as input
            _wasBreak = false;
            return;
        }

        // Present: credit the part of this interval that was within the idle cutoff of the last input.
        var idleFor = now - _lastInput;
        var credited = idleFor <= IdleCutoff
            ? elapsed
            : TimeSpan.Zero;
        if (idleFor > IdleCutoff && idleFor - elapsed < IdleCutoff)
        {
            credited = IdleCutoff - (idleFor - elapsed);   // the input was fresh at the start of the interval and went stale inside it
        }

        if (credited > TimeSpan.Zero)
        {
            ActiveTime += credited;
            ActiveSinceBreak += credited;
            _wasBreak = false;
        }
        else if (!_wasBreak)
        {
            StartBreak();
        }
    }

    /// <summary>Starts a fresh visit: everything is forgotten.</summary>
    public void Reset(DateTime now)
    {
        _pages.Clear();
        ActiveTime = TimeSpan.Zero;
        ActiveSinceBreak = TimeSpan.Zero;
        _lastTick = now;
        _lastInput = now;
        _awaySince = null;
        _wasBreak = false;
    }

    private void StartBreak()
    {
        _wasBreak = true;
        ActiveSinceBreak = TimeSpan.Zero;
        BreakStarted?.Invoke();
    }
}

/// <summary>Formats the stats HUD chip text (docs/superpowers/specs/2026-09-25-comic-reader-comfort-design.md section 1). Pure.</summary>
public static class SessionHudFormatter
{
    /// <summary>"24 min · 18 pages · 0.8/min · ~12 min left"; pieces that are not available yet are dropped from the right.</summary>
    public static string Format(TimeSpan activeTime, int pagesViewed, double? pagesPerMinute, int remainingPages)
    {
        var parts = new List<string> { FormatDuration(activeTime), pagesViewed == 1 ? "1 page" : $"{pagesViewed} pages" };
        if (pagesPerMinute is { } pace)
        {
            parts.Add($"{pace:0.0}/min");
            if (EstimateLeft(remainingPages, pace) is { } left)
            {
                parts.Add(left);
            }
        }

        return string.Join(" · ", parts);
    }

    /// <summary>"12 min", "1 h 05 min", "&lt;1 min".</summary>
    public static string FormatDuration(TimeSpan span)
    {
        int minutes = (int)Math.Floor(span.TotalMinutes);
        if (minutes < 1)
        {
            return "<1 min";
        }

        return minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00} min";
    }

    /// <summary>"~12 min left", "&lt;1 min left", or null when the issue is finished or the pace is unusable.</summary>
    public static string? EstimateLeft(int remainingPages, double pagesPerMinute)
    {
        if (remainingPages <= 0 || pagesPerMinute <= 0)
        {
            return null;
        }

        double minutes = remainingPages / pagesPerMinute;
        return minutes < 1 ? "<1 min left" : $"~{FormatDuration(TimeSpan.FromMinutes(minutes))} left";
    }
}

/// <summary>
/// When to nudge the reader to rest their eyes (docs/superpowers/specs/2026-09-25-comic-reader-comfort-design.md section 3): after an interval of active reading since the last break,
/// then again every interval while they keep reading. Snooze pushes the next nudge back; a break resets everything. Pure: it is fed the active time since the last break.
/// </summary>
public sealed class BreakNudgePolicy
{
    /// <summary>How far "Snooze" pushes the next nudge.</summary>
    public static readonly TimeSpan SnoozeLength = TimeSpan.FromMinutes(10);

    private TimeSpan _nextDue;
    private TimeSpan _interval;

    public BreakNudgePolicy(TimeSpan interval)
    {
        _interval = interval;
        _nextDue = interval;
    }

    /// <summary>Changes the interval (from Preferences); a due time already set is moved to the new interval only when it would otherwise be later.</summary>
    public void SetInterval(TimeSpan interval)
    {
        _interval = interval;
        if (_nextDue > interval)
        {
            _nextDue = interval;
        }
    }

    /// <summary>
    /// True once when <paramref name="activeSinceBreak"/> reaches the due mark (which then moves one interval on). <paramref name="nudgeOpen"/> is true while the previous nudge
    /// toast is still showing: nothing stacks, the nudge waits.
    /// </summary>
    public bool ShouldNudge(TimeSpan activeSinceBreak, bool nudgeOpen)
    {
        if (nudgeOpen || activeSinceBreak < _nextDue)
        {
            return false;
        }

        _nextDue = activeSinceBreak + _interval;
        return true;
    }

    /// <summary>Snooze: the next nudge comes <see cref="SnoozeLength"/> after now.</summary>
    public void Snooze(TimeSpan activeSinceBreak) => _nextDue = activeSinceBreak + SnoozeLength;

    /// <summary>A break happened: the count starts again.</summary>
    public void Reset() => _nextDue = _interval;
}
