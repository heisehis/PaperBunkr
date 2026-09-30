using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.EventMap;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The Event Map's view model (docs/superpowers/specs/2026-09-25-event-map-design.md §3-§4). Owns the loaded
/// <see cref="EventMapSource"/>, the spine / filter / density choices, the current <see cref="EventMapLayoutResult"/>
/// and its card view models, the selection with its dimming, and the inspector. All positions come from the pure
/// layout; this class only decides <i>which</i> layout to compute and what is selected.
/// </summary>
public sealed partial class EventMapViewModel : ViewModelBase
{
    public const string RelayOptionLabel = "None (relay)";

    private readonly Func<int, EventMapSource?> _load;
    private readonly Action<int, int?> _saveSpine;
    private readonly Func<int, bool, EventMapReadState?> _setRead;
    private readonly Action<int, int?> _openReader;
    private readonly Action<int> _openSeriesDetail;
    private readonly Action _showAddIssues;
    private readonly IActivityService _activity;
    private readonly Func<Func<EventMapSource?>, Task<EventMapSource?>> _runInBackground;

    private EventMapSource? _source;
    private SpineChoice _spine = SpineChoice.Relay;
    private Dictionary<string, int?> _spineOptionValues = new();
    private int _loadGeneration;
    private bool _suppressSpineWrite;
    private bool _suppressRelayout;

    public EventMapViewModel(
        Func<int, EventMapSource?>? load = null,
        Action<int, int?>? saveSpine = null,
        Func<int, bool, EventMapReadState?>? setRead = null,
        Action<int, int?>? openReader = null,
        Action<int>? openSeriesDetail = null,
        Action? showAddIssues = null,
        IActivityService? activity = null,
        Func<Func<EventMapSource?>, Task<EventMapSource?>>? runInBackground = null,
        Func<int, ContinuityMapData?>? loadContinuity = null,
        Action<int>? openEventMap = null,
        Action? showDuplicates = null)
    {
        _loadContinuity = loadContinuity ?? (id =>
        {
            using var context = PaperbunkrDb.CreateContext();
            using var gcd = Paperbunkr.Data.Gcd.GcdDataStore.TryOpen();
            return ContinuityMapLoader.LoadData(context, id, gcd);
        });
        _openEventMap = openEventMap ?? (_ => { });
        _showDuplicates = showDuplicates ?? (() => { });
        // Tests pass a synchronous runner so nothing hops threads (the headless suite's pinned dispatcher thread).
        _runInBackground = runInBackground ?? (work => Task.Run(work));
        _load = load ?? (id =>
        {
            using var context = PaperbunkrDb.CreateContext();
            return EventMapLoader.Load(context, id);
        });
        _saveSpine = saveSpine ?? ((id, spine) =>
        {
            using var context = PaperbunkrDb.CreateContext();
            EventMapLoader.SaveSpine(context, id, spine);
        });
        _setRead = setRead ?? ((issueId, read) =>
        {
            using var context = PaperbunkrDb.CreateContext();
            return EventMapLoader.SetRead(context, issueId, read);
        });
        _openReader = openReader ?? ((_, _) => { });
        _openSeriesDetail = openSeriesDetail ?? (_ => { });
        _showAddIssues = showAddIssues ?? (() => { });
        _activity = activity ?? new ActivityService();
    }

    /// <summary>Raised when the view should scroll a card into view (keyboard moves, link clicks, first view). Arg: cell index.</summary>
    public event Action<int>? RevealRequested;

    public int? StoryEventId { get; private set; }

    [ObservableProperty]
    private string _eventName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSpineMode), nameof(CornerLabel), nameof(HasMap), nameof(CanFilterSpineOnly))]
    private EventMapLayoutResult _layout = EventMapLayoutResult.Empty;

    [ObservableProperty]
    private IReadOnlyList<EventMapCardViewModel> _cards = Array.Empty<EventMapCardViewModel>();

    [ObservableProperty]
    private IReadOnlyList<EventMapLaneHeader> _laneHeaders = Array.Empty<EventMapLaneHeader>();

    /// <summary>Cell indices that stay undimmed for the current selection; null when nothing is selected. Read by the edge layer.</summary>
    [ObservableProperty]
    private IReadOnlySet<int>? _relatedSet;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCard), nameof(HasSelection))]
    private int? _selectedIndex;

    [ObservableProperty]
    private bool _isInspectorOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMap), nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMap), nameof(IsEmpty))]
    private bool _hasError;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterAll), nameof(IsFilterSpineOnly), nameof(IsFilterHideOptional), nameof(IsFilterEventsOnly))]
    private EventMapFilter _filter = EventMapFilter.All;

    /// <summary>Slider value 0-2 (Compact, Standard, Covers); snaps to the stops.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Density))]
    private double _densityIndex = (double)EventMapDensity.Standard;

    [ObservableProperty]
    private IReadOnlyList<string> _spineOptions = Array.Empty<string>();

    public ObservableCollection<EventMapLinkViewModel> Connections { get; } = new();

    public ObservableCollection<EventMapLinkViewModel> SegmentOrder { get; } = new();

    public EventMapDensity Density => (EventMapDensity)Math.Clamp((int)Math.Round(DensityIndex), 0, 2);

    public bool IsSpineMode => Layout.IsSpineMode;

    public string CornerLabel => IsContinuityScope ? (_continuitySource?.IsPublicationOrder == true ? "Publication order" : "Events in order") : IsSpineMode ? $"Spine: {Layout.Tracks[0].SeriesName}" : "Relay";

    public bool CanFilterSpineOnly => _spine.HasSpine && !IsContinuityScope;

    public bool IsFilterAll => Filter == EventMapFilter.All;

    public bool IsFilterSpineOnly => Filter == EventMapFilter.SpineOnly;

    public bool IsFilterHideOptional => Filter == EventMapFilter.HideOptional;

    public bool IsFilterEventsOnly => Filter == EventMapFilter.EventsOnly;

    public bool IsLoaded => _source is not null;

    /// <summary>The event has no members at all (not merely everything filtered away).</summary>
    public bool IsEmpty => !IsLoading && !HasError && _source is { Rows.Count: 0 };

    public bool HasMap => !IsLoading && !HasError && _source is { Rows.Count: > 0 };

    public EventMapCardViewModel? SelectedCard => SelectedIndex is int i && i < Cards.Count ? Cards[i] : null;

    public bool HasSelection => SelectedCard is not null;

    public string SurfaceAutomationName => IsContinuityScope ? $"Continuity map for {EventName}" : $"Event map for {EventName}";

    partial void OnEventNameChanged(string value) => OnPropertyChanged(nameof(SurfaceAutomationName));

    // ===================== Loading =====================

    /// <summary>
    /// Loads (or reloads) the map. The query runs off the UI thread; a newer call supersedes an older one still in
    /// flight. A different event gets the spec's first view (Standard, All, scrolled to <see cref="EventMapLayoutResult.FirstUnread"/>,
    /// inspector closed). Reloading the same event - e.g. on returning from the reader - keeps the view and, when
    /// <paramref name="reselectIssueId"/> is given, re-selects that issue; if nothing but read states changed, only the
    /// affected cards are touched.
    /// </summary>
    public async Task LoadAsync(int storyEventId, int? reselectIssueId = null)
    {
        int generation = ++_loadGeneration;
        bool sameEvent = !IsContinuityScope && StoryEventId == storyEventId && _source is not null;
        LeaveContinuityScope();
        IsLoading = true;
        HasError = false;

        EventMapSource? source;
        try
        {
            source = await _runInBackground(() => _load(storyEventId));
        }
        catch (Exception ex)
        {
            if (generation == _loadGeneration)
            {
                ShowLoadError(storyEventId, ex);
            }

            return;
        }

        if (generation != _loadGeneration)
        {
            return;
        }

        IsLoading = false;
        StoryEventId = storyEventId;
        source ??= new EventMapSource(storyEventId, EventName, null, Array.Empty<EventMapRow>());
        EventName = source.EventName;

        if (sameEvent && TryRefreshReadStatesInPlace(source))
        {
            if (reselectIssueId is int issue && Layout.IndexOfIssue(issue) is int index)
            {
                Select(index, reveal: true);
            }

            RaiseLoadState();
            return;
        }

        int? keepIssue = reselectIssueId ?? (sameEvent ? SelectedCard?.IssueId : null);
        _source = source;
        _spine = SpineResolver.Resolve(source);
        if (!sameEvent)
        {
            _suppressRelayout = true;
            Filter = EventMapFilter.All;
            DensityIndex = (double)EventMapDensity.Standard;
            _suppressRelayout = false;
            IsInspectorOpen = false;
        }

        RebuildSpineOptions();
        Relayout(keepIssue, firstView: !sameEvent || keepIssue is null);
        RaiseLoadState();
    }

    private void ShowLoadError(int storyEventId, Exception ex)
    {
        IsLoading = false;
        HasError = true;
        ErrorText = $"Couldn't load the map for this event: {ex.Message}";
        _source = null;
        Layout = EventMapLayoutResult.Empty;
        Cards = Array.Empty<EventMapCardViewModel>();
        LaneHeaders = Array.Empty<EventMapLaneHeader>();
        SelectedIndex = null;
        RelatedSet = null;
        IsInspectorOpen = false;
        _activity.RaiseAlert(new ActivityAlert
        {
            Severity = ActivityAlertSeverity.Error,
            Title = "Event map failed to load",
            Detail = $"Event {storyEventId}: {ex.Message}",
        });
        RaiseLoadState();
    }

    private void RaiseLoadState()
    {
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasMap));
    }

    /// <summary>Same rows in the same order with the same spine: copy the new read states onto the existing cards and source, nothing else.</summary>
    private bool TryRefreshReadStatesInPlace(EventMapSource fresh)
    {
        if (_source is null || fresh.SpineSeriesId != _source.SpineSeriesId || fresh.Rows.Count != _source.Rows.Count)
        {
            return false;
        }

        for (int i = 0; i < fresh.Rows.Count; i++)
        {
            var a = fresh.Rows[i];
            var b = _source.Rows[i];
            if (a.MembershipId != b.MembershipId || a.Position != b.Position || a.Role != b.Role || a.SeriesId != b.SeriesId || a.FileIsMissing != b.FileIsMissing)
            {
                return false;
            }
        }

        _source = fresh;
        var byIssue = fresh.Rows.ToDictionary(r => r.MembershipId, r => r.ReadState);
        foreach (var card in Cards)
        {
            if (byIssue.TryGetValue(card.MembershipId, out var state) && card.ReadState != state)
            {
                card.ReadState = state;
            }
        }

        UpdateStatusText();
        return true;
    }

    // ===================== Layout =====================

    private EventMapFilter EffectiveFilter => Filter == EventMapFilter.SpineOnly && !_spine.HasSpine ? EventMapFilter.All : Filter;

    private void Relayout(int? keepIssueId, bool firstView = false)
    {
        if (_continuityData is not null)
        {
            RelayoutContinuity(keepIssueId, firstView);
            return;
        }

        if (_source is null)
        {
            return;
        }

        var layout = EventMapLayout.Compute(_source, _spine, EffectiveFilter, Density);
        Cards = layout.Cells.Select(c => new EventMapCardViewModel(c, layout.Tracks[c.Track])).ToList();
        LaneHeaders = layout.Tracks.Select(t => new EventMapLaneHeader(
            t.IsTrunk ? $"Spine · {t.SeriesName}" : t.SeriesName, t.SeriesName, t.Count, t.ColorIndex, t.IsTrunk, layout.Metrics.TrackHeight)).ToList();
        Layout = layout;

        int? index = keepIssueId is int issue ? layout.IndexOfIssue(issue) : null;
        if (index is null && firstView)
        {
            index = layout.FirstUnread();
        }

        SelectedIndex = null;
        Select(index, reveal: index is not null);
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        if (_continuitySource is not null)
        {
            StatusText = ContinuityStatusText(_continuitySource);
            return;
        }

        if (_source is null)
        {
            StatusText = string.Empty;
            return;
        }

        int issues = _source.Rows.Count;
        int series = _source.Rows.Select(r => r.SeriesId).Distinct().Count();
        string counts = $"{issues} issue{(issues == 1 ? string.Empty : "s")} · {series} series";
        if (_spine.SeriesId is int spineId)
        {
            string name = _source.Rows.First(r => r.SeriesId == spineId).SeriesName;
            StatusText = $"{counts} · spine: {name} ({(_spine.Source == SpineSource.User ? "set" : "auto")})";
        }
        else
        {
            StatusText = _source.SpineSeriesId == SpineResolver.ForceRelay
                ? $"{counts} · relay: spine turned off"
                : $"{counts} · relay: no series matches the event name";
        }
    }

    partial void OnFilterChanged(EventMapFilter value)
    {
        if (!_suppressRelayout)
        {
            Relayout(SelectedCard?.IssueId);
        }
    }

    partial void OnDensityIndexChanged(double value)
    {
        // The slider reports fractional values while dragging; only a real stop change relays out.
        if (!_suppressRelayout && _source is not null && Layout.Density != Density)
        {
            Relayout(SelectedCard?.IssueId);
        }
    }

    [RelayCommand]
    private void SetFilter(EventMapFilter filter) => Filter = filter;

    /// <summary>Ctrl+wheel: one density stop at a time.</summary>
    public void StepDensity(int delta) => DensityIndex = Math.Clamp((int)Density + Math.Sign(delta), 0, 2);

    // ===================== Spine picker =====================

    /// <summary>The picker's text. Setting it to one of <see cref="SpineOptions"/> saves the choice and relays out.</summary>
    public string SpineText
    {
        get
        {
            if (_source is null)
            {
                return string.Empty;
            }

            if (_spine.SeriesId is int id)
            {
                return _spineOptionValues.FirstOrDefault(kv => kv.Value == id).Key ?? string.Empty;
            }

            return RelayOptionLabel;
        }
        set
        {
            if (_suppressSpineWrite || _source is null || StoryEventId is not int eventId || value == SpineText)
            {
                return;
            }

            if (!_spineOptionValues.TryGetValue(value, out int? seriesId))
            {
                return;
            }

            // Picking the "(auto)" entry goes back to automatic (null), so a later rename keeps working; "None" is the
            // explicit relay sentinel; anything else pins that series.
            int? stored = value == RelayOptionLabel
                ? SpineResolver.ForceRelay
                : seriesId == _spine.AutoMatchSeriesId ? null : seriesId;
            _saveSpine(eventId, stored);
            _source = _source with { SpineSeriesId = stored };
            _spine = SpineResolver.Resolve(_source);
            if (!_spine.HasSpine && Filter == EventMapFilter.SpineOnly)
            {
                Filter = EventMapFilter.All;    // relays out itself
            }
            else
            {
                Relayout(SelectedCard?.IssueId);
            }

            OnPropertyChanged(nameof(SpineText));
            OnPropertyChanged(nameof(CanFilterSpineOnly));
        }
    }

    private void RebuildSpineOptions()
    {
        var values = new Dictionary<string, int?>();
        if (_source is not null)
        {
            foreach (var group in _source.Rows.GroupBy(r => r.SeriesId))
            {
                string label = group.First().SeriesName + (group.Key == _spine.AutoMatchSeriesId ? " (auto)" : string.Empty);
                string unique = label;
                for (int n = 2; values.ContainsKey(unique); n++)
                {
                    unique = $"{label} ({n})";
                }

                values[unique] = group.Key;
            }

            values[RelayOptionLabel] = null;
        }

        _suppressSpineWrite = true;
        _spineOptionValues = values;
        SpineOptions = values.Keys.ToList();
        OnPropertyChanged(nameof(SpineText));
        OnPropertyChanged(nameof(CanFilterSpineOnly));
        _suppressSpineWrite = false;
    }

    // ===================== Selection, dimming, inspector =====================

    /// <summary>Selects a card (or clears the selection with null), updates dimming and the inspector, and optionally asks the view to reveal it.</summary>
    public void Select(int? index, bool reveal = true)
    {
        if (index is int i && (i < 0 || i >= Cards.Count))
        {
            index = null;
        }

        if (SelectedCard is { } previous)
        {
            previous.IsSelected = false;
        }

        SelectedIndex = index;
        var related = index is int s ? Layout.RelatedSet(s) : null;
        RelatedSet = related;
        foreach (var card in Cards)
        {
            card.IsDimmed = related is not null && !related.Contains(card.Index);
        }

        if (SelectedCard is { } current)
        {
            current.IsSelected = true;
        }

        RefreshInspector();
        if (reveal && index is int r)
        {
            RevealRequested?.Invoke(r);
        }
    }

    /// <summary>Pointer click on a card: select it and open the inspector.</summary>
    public void ActivateCard(int index)
    {
        Select(index, reveal: false);
        IsInspectorOpen = SelectedCard is not null;
    }

    private void RefreshInspector()
    {
        Connections.Clear();
        SegmentOrder.Clear();
        if (SelectedIndex is not int index)
        {
            return;
        }

        var links = Layout.LinksFor(index);
        void Add(string relation, int? target)
        {
            if (target is int t)
            {
                Connections.Add(new EventMapLinkViewModel(relation, Cards[t].Title, t, RoleLabel: Cards[t].RoleLabel));
            }
        }

        Add("Follows", links.Follows);
        Add("Leads to", links.LeadsTo);
        Add("Ties into", links.TiesInto);
        Add("Previous in series", links.PreviousInSeries);
        AddContinuityLinks(Cards[index]);
        if (IsSpineMode)
        {
            foreach (int t in links.SegmentOrder)
            {
                SegmentOrder.Add(new EventMapLinkViewModel(string.Empty, Cards[t].Title, t, t == index, Cards[t].RoleLabel));
            }
        }

        OnPropertyChanged(nameof(HasConnections));
        OnPropertyChanged(nameof(HasSegmentOrder));
    }

    public bool HasConnections => Connections.Count > 0;

    public bool HasSegmentOrder => SegmentOrder.Count > 1;

    /// <summary>
    /// Inspector link / segment-order entry. Deferred one dispatcher tick: the button raising this lives in a list that
    /// <see cref="RefreshInspector"/> rebuilds, and clearing it synchronously would detach the button mid-route (CLAUDE.md,
    /// "don't remove/detach a control from inside a routed event it's still raising").
    /// </summary>
    [RelayCommand]
    private void SelectLink(EventMapLinkViewModel? link)
    {
        if (link is null)
        {
            return;
        }

        if (link.EventId is int eventId)
        {
            Dispatcher.UIThread.Post(() => _openEventMap(eventId));
            return;
        }

        int target = link.Index;
        Dispatcher.UIThread.Post(() => Select(target, reveal: true));
    }

    [RelayCommand]
    private void CloseInspector() => IsInspectorOpen = false;

    [RelayCommand]
    private void OpenInspector() => IsInspectorOpen = SelectedCard is not null;

    [RelayCommand]
    private void OpenReader()
    {
        if (SelectedCard is { } card)
        {
            // Event map: the event itself. Continuity map: the event the card sits in (loose issues open unanchored).
            int? anchor = IsContinuityScope ? card.OwnerEventId : StoryEventId;
            if (IsContinuityScope || anchor is not null)
            {
                _openReader(card.IssueId, anchor);
            }
        }
    }

    [RelayCommand]
    private void OpenSeriesDetail()
    {
        if (SelectedCard is { } card)
        {
            _openSeriesDetail(card.SeriesId);
        }
    }

    /// <summary>Mark read / Mark unread: persists through <see cref="Paperbunkr.Data.Metadata.IssueReadStateResolver"/> and updates the card and inspector in place - no reload.</summary>
    [RelayCommand]
    private void ToggleRead()
    {
        if (SelectedCard is not { } card || _source is null)
        {
            return;
        }

        var state = _setRead(card.IssueId, !card.IsRead);
        if (state is not EventMapReadState newState)
        {
            return;
        }

        card.ReadState = newState;
        PatchContinuityReadState(card.IssueId, newState);
        _source = _source with
        {
            Rows = _source.Rows.Select(r => r.IssueId == card.IssueId ? r with { ReadState = newState } : r).ToList(),
        };
        foreach (var other in Cards.Where(c => c.IssueId == card.IssueId && !ReferenceEquals(c, card)))
        {
            other.ReadState = newState;
        }
    }

    [RelayCommand]
    private void JumpToFirstUnread()
    {
        if (Layout.FirstUnread() is int index)
        {
            Select(index, reveal: true);
        }
    }

    [RelayCommand]
    private void AddIssues() => _showAddIssues();

    // ===================== Keyboard verbs (EventMapView.OnKeyDown) =====================

    private void Move(Func<int, int?> step)
    {
        if (Layout.IsEmpty)
        {
            return;
        }

        if (SelectedIndex is not int current)
        {
            Select(Layout.FirstUnread(), reveal: true);
            return;
        }

        if (step(current) is int target)
        {
            Select(target, reveal: true);
        }
    }

    public void MoveNext() => Move(Layout.Next);

    public void MovePrevious() => Move(Layout.Prev);

    public void MoveUp() => Move(i => Layout.NearestInLane(i, -1));

    public void MoveDown() => Move(i => Layout.NearestInLane(i, +1));

    public void MoveFirst() => Move(_ => Layout.First());

    public void MoveLast() => Move(_ => Layout.Last());

    /// <summary>Esc: close the inspector first, then clear the selection. Returns false when there was nothing to do.</summary>
    public bool Escape()
    {
        if (IsInspectorOpen)
        {
            IsInspectorOpen = false;
            return true;
        }

        if (SelectedIndex is not null)
        {
            Select(null, reveal: false);
            return true;
        }

        return false;
    }
}
