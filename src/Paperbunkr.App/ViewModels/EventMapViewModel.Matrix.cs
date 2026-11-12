using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services.EventMap;

namespace Paperbunkr.App.ViewModels;

public enum EventMapMode { Map, Matrix, List, Atlas }

public sealed partial class EventMapRollCallRow : ObservableObject
{
    private readonly Action<EventMapRollCallRow> _changed;
    public EventMapRollCallRow(int seriesId, string name, int color, int count, int read, bool shown, Action<EventMapRollCallRow> changed, int missing = 0)
    {
        SeriesId = seriesId; Name = name; ColorIndex = color; CountLabel = $"{count} · {read} read"; _isShown = shown; _changed = changed;
        MissingLabel = missing > 0 ? $"{missing} missing" : string.Empty;
    }
    /// <summary>"1 missing" (M4): issues the event's provider list has that the library doesn't.</summary>
    public string MissingLabel { get; }
    public bool HasMissing => MissingLabel.Length > 0;
    public int SeriesId { get; }
    public string Name { get; }
    public int ColorIndex { get; }
    public string CountLabel { get; }
    [ObservableProperty] private bool _isShown;
    partial void OnIsShownChanged(bool value) => _changed(this);
}

public sealed partial class EventMapViewModel
{
    private readonly HashSet<int> _hiddenSeriesIds = new();
    private EventMapMode _eventMode, _continuityMode;
    private string _rollCallSearch = string.Empty;
    private IReadOnlyList<EventMapRollCallRow> _allRollCall = Array.Empty<EventMapRollCallRow>();

    [ObservableProperty] private EventMatrix _matrix = EventMatrix.Empty;
    [ObservableProperty] private bool _matrixCovers;
    [ObservableProperty] private bool _isRollCallOpen;
    public ObservableCollection<EventMapRollCallRow> RollCallRows { get; } = new();
    public bool HasHiddenSeries => _hiddenSeriesIds.Count > 0;
    public bool ShowRollCallSearch => _allRollCall.Count > 12;
    public string RollCallSearch { get => _rollCallSearch; set { if (SetProperty(ref _rollCallSearch, value)) FilterRollCall(); } }
    public EventMapMode MapMode => IsContinuityScope ? _continuityMode : _eventMode;
    public bool IsMapMode => MapMode == EventMapMode.Map;
    public bool IsMatrixMode => MapMode == EventMapMode.Matrix;
    public string ProgressText
    {
        get
        {
            // Ghosts (M4) count only as "missing", never toward the read total.
            int members = Cards.Count(c => c.IsMember), read = Cards.Count(c => c.IsMember && c.IsRead), missing = Cards.Count(c => c.IsMissingGhost);
            return $"{read} / {members} read" + (missing > 0 ? $" · {missing} missing" : string.Empty);
        }
    }
    public event Action<int>? RevealSeriesRequested;

    [RelayCommand] private void ShowMapMode() => SetMapMode(EventMapMode.Map);
    [RelayCommand] private void ShowMatrixMode() => SetMapMode(EventMapMode.Matrix);
    [RelayCommand] private void ToggleRollCall() => IsRollCallOpen = !IsRollCallOpen;
    [RelayCommand] private void ToggleMatrixCovers() => MatrixCovers = !MatrixCovers;
    [RelayCommand] private void ShowAllSeries()
    {
        _hiddenSeriesIds.Clear(); OnPropertyChanged(nameof(HasHiddenSeries)); Relayout(SelectedCard?.IssueId);
    }
    [RelayCommand] private void RevealSeries(EventMapRollCallRow? row)
    { if (row is not null && row.IsShown) RevealSeriesRequested?.Invoke(row.SeriesId); }

    private void SetMapMode(EventMapMode mode)
    {
        // Atlas is continuity-only (M6a §2); an event map falls back to Map.
        if (mode == EventMapMode.Atlas && !IsContinuityScope) mode = EventMapMode.Map;
        if (IsContinuityScope) _continuityMode = mode; else _eventMode = mode;
        NotifyMapScopeMode();
    }
    private void NotifyMapScopeMode()
    {
        OnPropertyChanged(nameof(MapMode)); OnPropertyChanged(nameof(IsMapMode)); OnPropertyChanged(nameof(IsMatrixMode));
        OnPropertyChanged(nameof(IsListMode)); OnPropertyChanged(nameof(IsAtlasMode)); OnPropertyChanged(nameof(IsAtlasAvailable));
        OnPropertyChanged(nameof(CanSaveImage));
    }

    public void ResetHiddenSeries()
    {
        if (_hiddenSeriesIds.Count == 0) return;
        _hiddenSeriesIds.Clear(); OnPropertyChanged(nameof(HasHiddenSeries));
        if (_source is not null) Relayout(SelectedCard?.IssueId);
    }
    private void OnRollCallChanged(EventMapRollCallRow row)
    {
        if (row.IsShown) _hiddenSeriesIds.Remove(row.SeriesId); else _hiddenSeriesIds.Add(row.SeriesId);
        OnPropertyChanged(nameof(HasHiddenSeries));
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Relayout(SelectedCard?.IssueId));
    }
    private void FilterRollCall()
    {
        RollCallRows.Clear();
        foreach (var row in _allRollCall.Where(r => string.IsNullOrWhiteSpace(RollCallSearch) || r.Name.Contains(RollCallSearch, StringComparison.CurrentCultureIgnoreCase)))
            RollCallRows.Add(row);
    }
    private void RefreshMatrixAndRollCall()
    {
        Matrix = EventMatrixBuilder.Build(Layout, readState: i => i < Cards.Count ? Cards[i].ReadState : EventMapReadState.Unread);
        EventMapLayoutResult rollLayout;
        if (_continuityData is { } data)
        {
            var options = new ContinuityMapOptions(new HashSet<int>(_hiddenEvents), Filter == EventMapFilter.HideOptional,
                Filter == EventMapFilter.EventsOnly && data.Events.Count > 0, HideRead, _keptReadIssue);
            rollLayout = EventMapLayout.ComputeBlocks(ContinuityMapBuilder.Build(data, options), Density, IsRelayPacked);
        }
        else if (_source is { } source)
            rollLayout = EventMapLayout.Compute(source, _spine, EffectiveFilter, Density, IsRelayPacked, HideRead, _keptReadIssue);
        else rollLayout = EventMapLayoutResult.Empty;
        var groups = rollLayout.Cells.GroupBy(c => c.Row.SeriesId).ToList();
        // Ghosts (M4) live in the map's own layout, not this roll-call layout; count them per series from the cards.
        var missingBySeries = Cards.Where(c => c.IsMissingGhost).GroupBy(c => c.SeriesId).ToDictionary(g => g.Key, g => g.Count());
        _allRollCall = groups.Select(g => new EventMapRollCallRow(g.Key, g.First().Row.SeriesName,
            g.First().ColorIndexOverride ?? rollLayout.Tracks[g.First().Track].ColorIndex,
            g.Count(c => !c.Row.IsGhost), g.Count(c => c.Row.ReadState == EventMapReadState.Read && !c.Row.IsGhost), !_hiddenSeriesIds.Contains(g.Key), OnRollCallChanged,
            missingBySeries.GetValueOrDefault(g.Key))).ToArray();
        FilterRollCall(); OnPropertyChanged(nameof(ShowRollCallSearch)); OnPropertyChanged(nameof(ProgressText));
        RefreshList();
        RefreshAtlas();
        RefreshMissing();
        RefreshStructureView();
        OnPropertyChanged(nameof(CanSaveImage));
    }
    internal void OpenMatrixIssueOnMap(int index)
    {
        SetMapMode(EventMapMode.Map); Select(index, reveal: true);
    }
}
