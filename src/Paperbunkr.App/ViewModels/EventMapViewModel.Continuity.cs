using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services.EventMap;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The map's continuity scope (docs/superpowers/specs/2026-09-27-continuity-map-design.md §3-§4): every issue of every event touching a
/// continuity, events in chronological order with loose issues between them (or publication order when there are no events), shown
/// with the same cards, virtualization, inspector and keyboard as an event map.
/// </summary>
public sealed partial class EventMapViewModel
{
    private readonly Func<int, ContinuityMapData?> _loadContinuity;
    private readonly Action<int> _openEventMap;
    private readonly Action _showDuplicates;
    private readonly HashSet<int> _hiddenEvents = new();
    private ContinuityMapData? _continuityData;
    private ContinuityMapSource? _continuitySource;

    public int? ContinuityId { get; private set; }

    public bool IsContinuityScope => _continuityData is not null;

    public bool IsSpinePickerVisible => !IsContinuityScope;

    /// <summary>"Events only" exists only on a continuity map that has events.</summary>
    public bool CanFilterEventsOnly => IsContinuityScope && _continuitySource is { IsPublicationOrder: false };

    public bool HasEventChoices => EventChoices.Count > 0;

    /// <summary>The Events picker: one checkbox per event touching the continuity.</summary>
    public ObservableCollection<EventMapEventChoice> EventChoices { get; } = new();

    /// <summary>Relations drawn between event bands on the ruler.</summary>
    [ObservableProperty]
    private IReadOnlyList<EventMapConnector> _connectors = Array.Empty<EventMapConnector>();

    /// <summary>The ruler grows a band row (block names and connectors) on a continuity map.</summary>
    public double RulerHeight => IsContinuityScope ? 50 : 28;

    public bool HasPendingDuplicates => _continuitySource is { PendingDuplicatePairs: > 0 };

    public string PendingDuplicatesText => _continuitySource?.PendingDuplicatePairs is int n and > 0
        ? $"{n} possible duplicate event{(n == 1 ? "" : "s")} - review"
        : string.Empty;

    /// <summary>Loaded, but the filters leave nothing to show.</summary>
    public bool IsFilteredEmpty => HasMap && Layout.IsEmpty;

    public string EmptyText => IsContinuityScope ? "Add series to this continuity to see its map" : "Add issues to this event to see its map";

    public bool IsAddIssuesVisible => !IsContinuityScope;

    /// <summary>
    /// Loads a continuity's map. Same generation guarding and "same scope keeps the view" rules as <see cref="LoadAsync"/>; the query
    /// runs through the same background runner.
    /// </summary>
    public async Task LoadContinuityAsync(int continuityId, int? reselectIssueId = null)
    {
        int generation = ++_loadGeneration;
        bool sameScope = IsContinuityScope && ContinuityId == continuityId;
        IsLoading = true;
        HasError = false;

        ContinuityMapData? data = null;
        try
        {
            await _runInBackground(() =>
            {
                data = _loadContinuity(continuityId);
                return null;
            });
        }
        catch (Exception ex)
        {
            if (generation == _loadGeneration)
            {
                ShowLoadError(continuityId, ex);
            }

            return;
        }

        if (generation != _loadGeneration)
        {
            return;
        }

        IsLoading = false;
        int? keepIssue = reselectIssueId ?? (sameScope ? SelectedCard?.IssueId : null);
        StoryEventId = null;
        ContinuityId = continuityId;
        _continuityData = data ?? new ContinuityMapData(continuityId, EventName, Array.Empty<ContinuityLane>(), Array.Empty<ContinuityEventData>(),
            Array.Empty<ContinuityRelationData>(), Array.Empty<EventMapRow>(), new Dictionary<int, IReadOnlyList<EventMapEventRef>>(), 0);
        EventName = _continuityData.Name;
        _spine = SpineChoice.Relay;
        SpineOptions = Array.Empty<string>();

        if (!sameScope)
        {
            _hiddenEvents.Clear();
            _suppressRelayout = true;
            Filter = EventMapFilter.All;
            DensityIndex = (double)EventMapDensity.Standard;
            _suppressRelayout = false;
            IsInspectorOpen = false;
        }

        RebuildEventChoices();
        _source = new EventMapSource(continuityId, _continuityData.Name, null, AllRows(_continuityData));
        Relayout(keepIssue, firstView: !sameScope || keepIssue is null);
        RaiseScopeState();
        RaiseLoadState();
    }

    /// <summary>An event load leaves continuity scope.</summary>
    private void LeaveContinuityScope()
    {
        if (_continuityData is null)
        {
            return;
        }

        _continuityData = null;
        _continuitySource = null;
        ContinuityId = null;
        _hiddenEvents.Clear();
        EventChoices.Clear();
        Connectors = Array.Empty<EventMapConnector>();
        RaiseScopeState();
    }

    private void RaiseScopeState()
    {
        OnPropertyChanged(nameof(IsContinuityScope));
        OnPropertyChanged(nameof(IsSpinePickerVisible));
        OnPropertyChanged(nameof(CanFilterEventsOnly));
        OnPropertyChanged(nameof(CanFilterSpineOnly));
        OnPropertyChanged(nameof(RulerHeight));
        OnPropertyChanged(nameof(HasEventChoices));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(IsAddIssuesVisible));
        OnPropertyChanged(nameof(CornerLabel));
        OnPropertyChanged(nameof(SurfaceAutomationName));
    }

    private static string ContinuesAsTip(string seriesName, string? continuesAs) =>
        continuesAs is null ? seriesName : $"{seriesName} — continues as {continuesAs}";

    private void RelayoutContinuity(int? keepIssueId, bool firstView)
    {
        var data = _continuityData!;
        var options = new ContinuityMapOptions(
            new HashSet<int>(_hiddenEvents),
            HideOptional: Filter == EventMapFilter.HideOptional,
            EventsOnly: Filter == EventMapFilter.EventsOnly && data.Events.Count > 0);
        var source = ContinuityMapBuilder.Build(data, options);
        _continuitySource = source;

        var layout = EventMapLayout.ComputeBlocks(source, Density);
        Cards = layout.Cells.Select(c => new EventMapCardViewModel(c, layout.Tracks[c.Track])).ToList();
        LaneHeaders = layout.Tracks.Select(t => new EventMapLaneHeader(
            t.SeriesName, ContinuesAsTip(t.SeriesName, source.Lanes[t.Index].ContinuesAs), t.Count, t.ColorIndex, false, layout.Metrics.TrackHeight, t.IsOutside,
            source.Lanes[t.Index].ContinuesAs)).ToList();
        Connectors = source.Connectors;
        Layout = layout;

        int? index = keepIssueId is int issue ? layout.IndexOfIssue(issue) : null;
        if (index is null && firstView)
        {
            index = layout.FirstUnread();
        }

        SelectedIndex = null;
        Select(index, reveal: index is not null);
        UpdateStatusText();
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(HasPendingDuplicates));
        OnPropertyChanged(nameof(PendingDuplicatesText));
        OnPropertyChanged(nameof(CanFilterEventsOnly));
        OnPropertyChanged(nameof(CornerLabel));
    }

    private static string ContinuityStatusText(ContinuityMapSource source)
    {
        if (source.IsPublicationOrder)
        {
            return $"{source.Rows.Count:N0} issue{(source.Rows.Count == 1 ? "" : "s")} · publication order";
        }

        int events = source.Blocks.Count(b => b.Kind == Services.EventMap.EventMapBlockKind.Event);
        return $"{source.EventIssueCount:N0} issue{(source.EventIssueCount == 1 ? "" : "s")} in {events} event{(events == 1 ? "" : "s")} · "
               + $"{source.LooseIssueCount:N0} outside events";
    }

    private static List<EventMapRow> AllRows(ContinuityMapData data) =>
        data.Events.SelectMany(e => e.Members).Concat(data.LooseIssues).DistinctBy(r => r.IssueId).ToList();

    private void RebuildEventChoices()
    {
        EventChoices.Clear();
        foreach (var e in _continuityData?.Events.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase) ?? Enumerable.Empty<ContinuityEventData>())
        {
            EventChoices.Add(new EventMapEventChoice(e.Id, e.Name, !_hiddenEvents.Contains(e.Id), OnEventChoiceChanged));
        }

        OnPropertyChanged(nameof(HasEventChoices));
    }

    private void OnEventChoiceChanged(EventMapEventChoice choice)
    {
        if (choice.IsShown)
        {
            _hiddenEvents.Remove(choice.EventId);
        }
        else
        {
            _hiddenEvents.Add(choice.EventId);
        }

        if (!_suppressRelayout)
        {
            Relayout(SelectedCard?.IssueId);
        }
    }

    /// <summary>Clicking an event's name on the ruler opens that event's own map. Deferred: the click comes from the ruler this replaces.</summary>
    public void OpenBand(int blockIndex)
    {
        if (blockIndex >= 0 && blockIndex < Layout.Blocks.Count && Layout.Blocks[blockIndex].EventId is int eventId)
        {
            Dispatcher.UIThread.Post(() => _openEventMap(eventId));
        }
    }

    [RelayCommand]
    private void ShowDuplicates() => _showDuplicates();

    /// <summary>Inspector: the card's event and the other events it's also in, as links that open those events' maps.</summary>
    private void AddContinuityLinks(EventMapCardViewModel card)
    {
        if (!IsContinuityScope)
        {
            return;
        }

        if (card.OwnerEventId is int owner)
        {
            Connections.Add(new EventMapLinkViewModel("Event", card.OwnerEventName ?? "Event", -1, EventId: owner));
        }

        foreach (var other in card.AlsoIn)
        {
            Connections.Add(new EventMapLinkViewModel("Also in", other.Name, -1, EventId: other.Id));
        }
    }

    /// <summary>Keeps a read-state change made from the inspector through the next relayout.</summary>
    private void PatchContinuityReadState(int issueId, EventMapReadState state)
    {
        if (_continuityData is not { } data)
        {
            return;
        }

        EventMapRow Patch(EventMapRow r) => r.IssueId == issueId ? r with { ReadState = state } : r;
        _continuityData = data with
        {
            Events = data.Events.Select(e => e with { Members = e.Members.Select(Patch).ToList() }).ToList(),
            LooseIssues = data.LooseIssues.Select(Patch).ToList(),
        };
    }
}

/// <summary>One checkbox in the continuity map's Events picker.</summary>
public sealed partial class EventMapEventChoice : ObservableObject
{
    private readonly Action<EventMapEventChoice> _changed;

    public EventMapEventChoice(int eventId, string name, bool isShown, Action<EventMapEventChoice> changed)
    {
        EventId = eventId;
        Name = name;
        _isShown = isShown;
        _changed = changed;
    }

    public int EventId { get; }

    public string Name { get; }

    [ObservableProperty]
    private bool _isShown;

    partial void OnIsShownChanged(bool value) => _changed(this);
}
