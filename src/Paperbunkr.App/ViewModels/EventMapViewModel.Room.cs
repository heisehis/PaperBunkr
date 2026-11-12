using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.EventMap;

namespace Paperbunkr.App.ViewModels;

public sealed partial class EventMapViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Look), nameof(SliderValue))]
    private double _scale = 1;

    [ObservableProperty]
    private bool _isLaneRailWide;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInspectorDockedOpen), nameof(InspectorDockLabel), nameof(FloatingInspectorFootprint))]
    private bool _isInspectorDocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MapMaximizeLabel))]
    private bool _isMapMaximized;

    public string MapMaximizeLabel => IsMapMaximized ? "Restore map" : "Maximize map";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPeekOpen))]
    private EventMapCardViewModel? _peekCard;

    public bool IsPeekOpen => PeekCard is not null;

    public bool IsInspectorDockedOpen => IsInspectorDocked && IsInspectorOpen;
    public double FloatingInspectorFootprint => IsInspectorOpen && !IsInspectorDocked ? 364 : 0;
    public string InspectorDockLabel => IsInspectorDocked ? "Float inspector" : "Dock inspector";

    private double _minimumScale = EventMapZoom.AbsoluteMinimum;
    private readonly SemaphoreSlim _roomSettingsWrite = new(1, 1);
    private bool _roomSettingsLoaded;
    private bool _railChangedBeforeLoad;
    private bool _dockChangedBeforeLoad;
    private bool _applyingRoomSettings;
    private bool _packedChangedBeforeLoad;
    private bool _progressLineChangedBeforeLoad;
    private bool _hideReadEvent, _hideReadContinuity;
    private int? _keptReadIssue;
    private readonly System.Collections.Generic.HashSet<string> _unfoldedMaps = new();
    private string FoldKey => IsContinuityScope ? $"c:{ContinuityId}" : $"e:{StoryEventId}";
    public bool ShortLanesUnfolded => _unfoldedMaps.Contains(FoldKey);
    public string FoldShortLanesLabel => ShortLanesUnfolded ? "Fold short tie-ins" : "Unfold short tie-ins";
    public bool CanFoldShortLanes { get; private set; }

    private void UpdateFoldAvailability(System.Collections.Generic.IEnumerable<EventMapRow> rows, int? spineId)
    {
        var groups = rows.GroupBy(r => r.SeriesId).ToList();
        CanFoldShortLanes = groups.Count >= 8 && groups.Count(g => g.Key != spineId && g.Count() <= 2) >= 3;
        OnPropertyChanged(nameof(CanFoldShortLanes));
        OnPropertyChanged(nameof(FoldShortLanesLabel));
    }

    [RelayCommand]
    private void ToggleShortLanes()
    {
        if (!_unfoldedMaps.Add(FoldKey)) _unfoldedMaps.Remove(FoldKey);
        OnPropertyChanged(nameof(ShortLanesUnfolded));
        OnPropertyChanged(nameof(FoldShortLanesLabel));
        Relayout(SelectedCard?.IssueId);
    }

    public bool HideRead
    {
        get => IsContinuityScope ? _hideReadContinuity : _hideReadEvent;
        set
        {
            if (HideRead == value) return;
            if (IsContinuityScope) _hideReadContinuity = value; else _hideReadEvent = value;
            _keptReadIssue = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HideReadLabel));
            OnPropertyChanged(nameof(IsEverythingRead));
            Relayout(SelectedCard?.IssueId, firstView: true);
        }
    }

    public bool IsEverythingRead => HideRead && IsFilteredEmpty;
    public string HideReadLabel => HideRead ? "Show read" : "Hide read";

    [RelayCommand]
    private void ToggleHideRead() => HideRead = !HideRead;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnePerColumn))]
    private bool _isRelayPacked = true;

    public bool IsOnePerColumn => !IsRelayPacked;
    public bool IsPackingVisible => IsContinuityScope || !_spine.HasSpine;

    [RelayCommand]
    private void UsePackedRelay() => IsRelayPacked = true;

    [RelayCommand]
    private void UseOnePerColumn() => IsRelayPacked = false;

    partial void OnIsRelayPackedChanged(bool value)
    {
        if (!_applyingRoomSettings)
        {
            if (!_roomSettingsLoaded) _packedChangedBeforeLoad = true;
            else _ = SaveRoomSettingsAsync();
        }
        if (!_suppressRelayout) Relayout(SelectedCard?.IssueId);
    }
    public EventMapCardLook Look => EventMapCardLooks.For(150 * Scale);
    public double MinimumScale
    {
        get => _minimumScale;
        set
        {
            double next = Math.Clamp(value, EventMapZoom.AbsoluteMinimum, EventMapZoom.Maximum);
            if (SetProperty(ref _minimumScale, next)) OnPropertyChanged(nameof(SliderValue));
        }
    }
    public double SliderValue
    {
        get => EventMapZoom.ToSlider(Scale, MinimumScale);
        set => Scale = EventMapZoom.FromSlider(value, MinimumScale);
    }

    public event Action? FitRequested;
    public event Action? ToggleMaximizeRequested;
    internal Func<bool>? RestoreMaximize { get; set; }

    [RelayCommand]
    private void FitAll() => FitRequested?.Invoke();

    [RelayCommand]
    private void ToggleLaneRail() => IsLaneRailWide = !IsLaneRailWide;

    [RelayCommand]
    private void ToggleInspectorDock() => IsInspectorDocked = !IsInspectorDocked;

    [RelayCommand]
    private void ToggleMapMaximize() => ToggleMaximizeRequested?.Invoke();

    partial void OnIsLaneRailWideChanged(bool value)
    {
        if (_applyingRoomSettings) return;
        if (!_roomSettingsLoaded) _railChangedBeforeLoad = true;
        else _ = SaveRoomSettingsAsync();
    }

    partial void OnIsInspectorDockedChanged(bool value)
    {
        if (_applyingRoomSettings) return;
        if (!_roomSettingsLoaded) _dockChangedBeforeLoad = true;
        else _ = SaveRoomSettingsAsync();
    }

    private async Task LoadRoomSettingsAsync()
    {
        try
        {
            var settings = await Task.Run(() =>
            {
                using var context = PaperbunkrDb.CreateContext();
                return context.AppSettings.AsNoTracking().Select(s => new { s.EventMapLaneRailWide, s.EventMapInspectorDocked, s.EventMapRelayPacked, s.EventMapProgressLine, s.EventMapShowMissing }).FirstOrDefault();
            });
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _applyingRoomSettings = true;
                if (!_railChangedBeforeLoad) IsLaneRailWide = settings?.EventMapLaneRailWide ?? false;
                if (!_dockChangedBeforeLoad) IsInspectorDocked = settings?.EventMapInspectorDocked ?? false;
                if (!_packedChangedBeforeLoad) IsRelayPacked = settings?.EventMapRelayPacked ?? true;
                if (!_progressLineChangedBeforeLoad) ShowProgressLine = settings?.EventMapProgressLine ?? true;
                if (!_showMissingChangedBeforeLoad) ShowMissing = settings?.EventMapShowMissing ?? false;
                _applyingRoomSettings = false;
                _roomSettingsLoaded = true;
                if (_railChangedBeforeLoad || _dockChangedBeforeLoad || _packedChangedBeforeLoad || _progressLineChangedBeforeLoad || _showMissingChangedBeforeLoad) _ = SaveRoomSettingsAsync();
            });
        }
        catch { /* Settings are optional; the map remains usable if the database is unavailable. */ }
    }

    private async Task SaveRoomSettingsAsync()
    {
        bool rail = IsLaneRailWide, dock = IsInspectorDocked, packed = IsRelayPacked, progressLine = ShowProgressLine, showMissing = ShowMissing;
        await _roomSettingsWrite.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                using var context = PaperbunkrDb.CreateContext();
                var settings = context.GetOrCreateAppSettings();
                settings.EventMapLaneRailWide = rail;
                settings.EventMapInspectorDocked = dock;
                settings.EventMapRelayPacked = packed;
                settings.EventMapProgressLine = progressLine;
                settings.EventMapShowMissing = showMissing;
                context.SaveChanges();
            });
        }
        catch { /* Preserve the in-session setting if persistence is unavailable. */ }
        finally { _roomSettingsWrite.Release(); }
    }
}
