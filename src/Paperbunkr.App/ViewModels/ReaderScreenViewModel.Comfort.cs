using System;
using System.Collections.Generic;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The reader's comfort features (docs/superpowers/specs/2026-09-25-comic-reader-comfort-design.md): the session stats chip, eye-rest nudges and the warm shift. All three hang off one
/// <see cref="ReadingSessionClock"/> and one 10-second timer, so the per-page-turn paths stay O(1). Kept in its own file so the already-large reader view model does not grow further.
/// </summary>
public partial class ReaderScreenViewModel
{
    private static readonly TimeSpan SessionTickInterval = TimeSpan.FromSeconds(10);

    /// <summary>A nudge toast that was never answered is treated as gone after this long (the toast host does not tell us when the user dismissed it).</summary>
    private static readonly TimeSpan NudgeToastLifetime = TimeSpan.FromMinutes(2);

    private ReadingSessionClock? _sessionClockField;
    private DispatcherTimer? _sessionTimer;
    private bool _userPresent;

    /// <summary>The current time; a test seam.</summary>
    internal Func<DateTime> NowProvider { get; set; } = () => DateTime.Now;

    private ReadingSessionClock SessionClock => _sessionClockField ??= CreateSessionClock();

    private ReadingSessionClock CreateSessionClock()
    {
        var clock = new ReadingSessionClock();
        clock.BreakStarted += OnBreakStarted;
        return clock;
    }

    /// <summary>The stats chip's live numbers, for tests and the perf overlay.</summary>
    internal ReadingSessionClock Session => SessionClock;

    /// <summary>Called by the screen while it decides whether the reader is showing in an active window: only then does reading time accrue.</summary>
    public void SetUserPresent(bool present)
    {
        if (_userPresent == present)
        {
            return;
        }

        _userPresent = present;
        if (present)
        {
            SessionClock.NoteInput(NowProvider());
        }
    }

    /// <summary>Called by the screen for every key press, click and wheel turn over the reader (and by gamepad frames).</summary>
    public void NoteReaderInput() => SessionClock.NoteInput(NowProvider());

    // ===================== Stats chip (#17) =====================

    /// <summary>Whether the chip is showing: the Preferences default at open, flipped for this visit by <see cref="ToggleSessionHudCommand"/>.</summary>
    [ObservableProperty]
    private bool _isSessionHudVisible;

    [ObservableProperty]
    private string _sessionHudText = string.Empty;

    private bool? _configuredSessionHud;

    [RelayCommand]
    private void ToggleSessionHud()
    {
        IsSessionHudVisible = !IsSessionHudVisible;
        UpdateSessionHud();
    }

    private void UpdateSessionHud()
    {
        if (!IsSessionHudVisible)
        {
            return;
        }

        int lastPage = _storyEndIndex >= 0 ? _storyEndIndex : PageCount - 1;
        int remaining = Math.Max(0, lastPage - _currentPageIndex);
        SessionHudText = SessionHudFormatter.Format(SessionClock.ActiveTime, SessionClock.PagesViewed, SessionClock.PagesPerMinute, remaining);
    }

    private void NoteSessionPage(int pageIndex)
    {
        if (_loadedIssueId is int issueId)
        {
            SessionClock.NotePageViewed(issueId, pageIndex);
            SessionClock.NoteInput(NowProvider());
        }
    }

    // ===================== Eye-rest nudges (#18) =====================

    private BreakNudgePolicy _nudgePolicy = new(TimeSpan.FromMinutes(20));
    private bool _breakNudgesEnabled;
    private ToastRequest? _nudgeToast;
    private DateTime _nudgeShownAt;

    /// <summary>Raised to close a toast that <see cref="ToastRequested"/> showed and that has actions (so it would otherwise stay); <see cref="MainViewModel"/> wires it to the toast host.</summary>
    public event Action<ToastRequest>? ToastCloseRequested;

    private void OnBreakStarted()
    {
        _nudgePolicy.Reset();
        CloseNudgeToast();
    }

    private void EvaluateBreakNudge()
    {
        // Never while the reader is away (hidden, or its window inactive): the clock is not counting then either.
        if (!_breakNudgesEnabled || !_userPresent)
        {
            return;
        }

        var now = NowProvider();
        bool open = _nudgeToast is not null && now - _nudgeShownAt < NudgeToastLifetime;
        if (!open && _nudgeToast is not null)
        {
            CloseNudgeToast();
        }

        if (!_nudgePolicy.ShouldNudge(SessionClock.ActiveSinceBreak, open))
        {
            return;
        }

        _nudgeToast = new ToastRequest(
            "Time for a break",
            "Look at something 20 feet away for 20 seconds.",
            ToastSeverity.Info,
            [new ToastAction("Snooze 10 min", SnoozeBreakNudgeCommand)]);
        _nudgeShownAt = now;
        ToastRequested?.Invoke(_nudgeToast);
    }

    [RelayCommand]
    private void SnoozeBreakNudge()
    {
        _nudgePolicy.Snooze(SessionClock.ActiveSinceBreak);
        CloseNudgeToast();
    }

    private void CloseNudgeToast()
    {
        if (_nudgeToast is { } toast)
        {
            _nudgeToast = null;
            ToastCloseRequested?.Invoke(toast);
        }
    }

    // ===================== Warm shift (#18) =====================

    private bool _warmShiftEnabled;
    private int _warmShiftStrength;
    private int _warmShiftStart = 1260;
    private int _warmShiftEnd = 420;
    private bool? _warmShiftForced;

    /// <summary>The warm tint on the pages right now, 0-1, bound to <c>PageCanvas.Warmth</c>.</summary>
    [ObservableProperty]
    private double _warmth;

    [RelayCommand]
    private void ToggleWarmShift()
    {
        _warmShiftForced = Warmth <= 0;
        RecomputeWarmth();
        ToastRequested?.Invoke(new ToastRequest(_warmShiftForced == true ? "Warm tint on" : "Warm tint off"));
    }

    private void RecomputeWarmth()
    {
        bool on = _warmShiftForced
            ?? (_warmShiftEnabled && WarmShiftSchedule.IsActive(TimeOnly.FromDateTime(NowProvider()), _warmShiftStart, _warmShiftEnd));
        Warmth = on ? WarmShiftSchedule.WarmthFor(_warmShiftStrength) : 0;
    }

    // ===================== Settings, timer and visit lifetime =====================

    /// <summary>Applies the comfort settings; called from <see cref="RefreshDisplaySettings"/> with the raw settings and the profile-overlaid copy (a profile may set the chip and the tint).</summary>
    private void ApplyComfortSettings(AppSettings appSettings, AppSettings effective)
    {
        _breakNudgesEnabled = appSettings.BreakNudgesEnabled;
        _nudgePolicy.SetInterval(TimeSpan.FromMinutes(Math.Clamp(appSettings.BreakNudgeIntervalMinutes, 10, 60)));
        if (!_breakNudgesEnabled)
        {
            CloseNudgeToast();
        }

        _warmShiftEnabled = effective.WarmShiftEnabled;
        _warmShiftStrength = effective.WarmShiftStrength;
        _warmShiftStart = appSettings.WarmShiftStartMinutes;
        _warmShiftEnd = appSettings.WarmShiftEndMinutes;
        RecomputeWarmth();

        if (_configuredSessionHud != effective.ShowSessionHud)
        {
            _configuredSessionHud = effective.ShowSessionHud;
            IsSessionHudVisible = effective.ShowSessionHud;
            UpdateSessionHud();
        }
    }

    /// <summary>Starts the 10-second timer if it is not already running; called when an issue is loaded.</summary>
    private void StartSessionTimer()
    {
        if (_sessionTimer is null)
        {
            _sessionTimer = new DispatcherTimer { Interval = SessionTickInterval };
            _sessionTimer.Tick += OnSessionTick;
        }

        if (!_sessionTimer.IsEnabled)
        {
            _sessionTimer.Start();
        }
    }

    /// <summary>Test seam, same rationale as <see cref="OnSkippedPagesHintExpired"/>.</summary>
    internal void OnSessionTick(object? sender, EventArgs e)
    {
        var now = NowProvider();
        if (_loadedIssueId is int issueId && PageCount > 0 && _userPresent)
        {
            SessionClock.NotePageViewed(issueId, _currentPageIndex);
        }

        SessionClock.Tick(now, _userPresent);
        UpdateSessionHud();
        EvaluateBreakNudge();
        RecomputeWarmth();
    }

    /// <summary>Leaving the reader ends the visit: the clock starts over, the nudge toast closes and the session-only warm shift override is dropped (called from <c>GoBack</c>).</summary>
    private void EndComfortVisit()
    {
        _sessionTimer?.Stop();
        SessionClock.Reset(NowProvider());
        _nudgePolicy.Reset();
        CloseNudgeToast();
        _warmShiftForced = null;
        RecomputeWarmth();
        SessionHudText = string.Empty;
    }
}
