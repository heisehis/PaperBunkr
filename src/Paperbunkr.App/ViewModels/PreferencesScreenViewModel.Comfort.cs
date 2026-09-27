using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Preferences &gt; Reader &gt; COMFORT (docs/superpowers/specs/2026-09-25-comic-reader-comfort-design.md): the stats chip default, eye-rest reminders and the warm shift. Every change
/// persists and raises <see cref="ReaderDisplaySettingsChanged"/> so an open reader picks it up without a reload.
/// </summary>
public partial class PreferencesScreenViewModel
{
    /// <summary>Show the reading stats chip when the reader opens.</summary>
    [ObservableProperty]
    private bool _showSessionHud;

    /// <summary>Remind to rest the eyes after an interval of active reading.</summary>
    [ObservableProperty]
    private bool _breakNudgesEnabled;

    /// <summary>Minutes of active reading between reminders (10-60).</summary>
    [ObservableProperty]
    private int _breakNudgeIntervalMinutes = 20;

    /// <summary>Tint the comic pages warm on a schedule.</summary>
    [ObservableProperty]
    private bool _warmShiftEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WarmShiftStartText))]
    private int _warmShiftStartMinutes = 1260;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WarmShiftEndText))]
    private int _warmShiftEndMinutes = 420;

    /// <summary>Tint strength, 0-100.</summary>
    [ObservableProperty]
    private int _warmShiftStrength = 40;

    /// <summary>"21:00"-style text for the strict start-time picker; only a listed half-hour is accepted.</summary>
    public string WarmShiftStartText
    {
        get => WarmShiftSchedule.Format(WarmShiftStartMinutes);
        set { if (WarmShiftSchedule.TryParse(value, out int minutes)) WarmShiftStartMinutes = minutes; }
    }

    public string WarmShiftEndText
    {
        get => WarmShiftSchedule.Format(WarmShiftEndMinutes);
        set { if (WarmShiftSchedule.TryParse(value, out int minutes)) WarmShiftEndMinutes = minutes; }
    }

    public IReadOnlyList<string> WarmShiftTimeChoices => WarmShiftSchedule.HalfHourChoices;

    /// <summary>Open the reader in guided panel view (docs/superpowers/specs/2026-09-25-comic-reader-panels-and-zoom-design.md section 3).</summary>
    [ObservableProperty]
    private bool _guidedViewOnOpen;

    /// <summary>Double-click zooms to the panel under the pointer (section 4).</summary>
    [ObservableProperty]
    private bool _smartDoubleClickZoom = true;

    partial void OnGuidedViewOnOpenChanged(bool value) => PersistComfort(s => s.GuidedViewOnOpen = value);

    partial void OnSmartDoubleClickZoomChanged(bool value) => PersistComfort(s => s.SmartDoubleClickZoom = value);

    /// <summary>Loads the comfort settings; called from <c>Reload</c> inside its suppress-persist window.</summary>
    private void LoadComfortSettings(AppSettings settings)
    {
        GuidedViewOnOpen = settings.GuidedViewOnOpen;
        SmartDoubleClickZoom = settings.SmartDoubleClickZoom;
        ShowSessionHud = settings.ShowSessionHud;
        BreakNudgesEnabled = settings.BreakNudgesEnabled;
        BreakNudgeIntervalMinutes = settings.BreakNudgeIntervalMinutes;
        WarmShiftEnabled = settings.WarmShiftEnabled;
        WarmShiftStartMinutes = settings.WarmShiftStartMinutes;
        WarmShiftEndMinutes = settings.WarmShiftEndMinutes;
        WarmShiftStrength = settings.WarmShiftStrength;
    }

    partial void OnShowSessionHudChanged(bool value) => PersistComfort(s => s.ShowSessionHud = value);

    partial void OnBreakNudgesEnabledChanged(bool value) => PersistComfort(s => s.BreakNudgesEnabled = value);

    partial void OnBreakNudgeIntervalMinutesChanged(int value) => PersistComfort(s => s.BreakNudgeIntervalMinutes = Math.Clamp(value, 10, 60));

    partial void OnWarmShiftEnabledChanged(bool value) => PersistComfort(s => s.WarmShiftEnabled = value);

    partial void OnWarmShiftStartMinutesChanged(int value) => PersistComfort(s => s.WarmShiftStartMinutes = value);

    partial void OnWarmShiftEndMinutesChanged(int value) => PersistComfort(s => s.WarmShiftEndMinutes = value);

    partial void OnWarmShiftStrengthChanged(int value) => PersistComfort(s => s.WarmShiftStrength = Math.Clamp(value, 0, 100));

    private void PersistComfort(Action<AppSettings> apply)
    {
        PersistBehaviorSetting(apply);
        if (!_suppressBehaviorApply)
        {
            ReaderDisplaySettingsChanged?.Invoke();
        }
    }
}
