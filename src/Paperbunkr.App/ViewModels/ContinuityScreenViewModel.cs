using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>What the Continuity screen's main area shows.</summary>
public enum ContinuityScreenPage
{
    None,
    Continuity,
    Event,
    Suggestions,
}

/// <summary>
/// The Continuity screen's shell (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md, "Architecture"), replacing the
/// old Story Events screen: the sidebar, what's open (a continuity, an event, or Suggestions &amp; checks), the Overview | Map | Timeline
/// switch, the Event Map, and navigation between them. The pages do their own work (<see cref="ContinuityPageViewModel"/>,
/// <see cref="EventPageViewModel"/>, <see cref="SuggestionsChecksViewModel"/>, <see cref="ContinuityTimelineViewModel"/>); this keeps
/// the handful of members <see cref="MainViewModel"/> calls.
/// </summary>
public partial class ContinuityScreenViewModel : ViewModelBase
{
    private int? _activeEventId;
    private int? _activeContinuityId;
    private readonly Action<int> _goToSeriesDetail;
    private readonly Action<int> _goToReader;
    private readonly IActivityService _activity;
    private Action<int, int> _goToReaderInEvent = (_, _) => { };

    public ContinuityScreenViewModel(
        Action<int>? goToSeriesDetail = null,
        Action<int>? goToReader = null,
        Action<int>? goToReadingList = null,
        Action<string, string>? notify = null,
        IActivityService? activity = null,
        bool loadOnConstruction = true,
        Func<Func<object?>, Task<object?>>? runner = null,
        Func<Paperbunkr.Data.Gcd.GcdDataStore?>? openGcd = null)
    {
        _goToSeriesDetail = goToSeriesDetail ?? (_ => { });
        _goToReader = goToReader ?? (_ => { });
        _activity = activity ?? new ActivityService();
        var notifyAction = notify ?? ((_, _) => { });

        Navigation = new ContinuityScreenNavigation
        {
            GoToSeriesDetail = id => _goToSeriesDetail(id),
            GoToReader = id => _goToReader(id),
            GoToReaderInEvent = (issueId, eventId) => _goToReaderInEvent(issueId, eventId),
            GoToReadingList = goToReadingList ?? (_ => { }),
            Notify = notifyAction,
            Activity = _activity,
            OpenEvent = id => LoadEvent(id),
            OpenEventMap = id =>
            {
                LoadEvent(id);
                DetailView = EventsDetailView.Map;
            },
            OpenContinuity = id => LoadContinuity(id),
            OpenSuggestions = OpenSuggestionsAt,
            DeleteEvent = DeleteEvent,
            DeleteContinuity = DeleteContinuity,
            SidebarChanged = () =>
            {
                Sidebar!.RefreshEvents();
                Sidebar.RefreshContinuities();
            },
            Runner = runner ?? (work => Task.Run(work)),
            OpenGcd = openGcd ?? (() => Paperbunkr.Data.Gcd.GcdDataStore.TryOpen()),
        };

        Sidebar = new ContinuitySidebarViewModel(() => _activeContinuityId, () => _activeEventId, DeleteContinuity, DeleteEvent);
        Suggestions = new SuggestionsChecksViewModel(notifyAction, _activity, id => LoadEvent(id), id => LoadContinuity(id), AfterIdentityWork,
            () => Sidebar.SuggestionsCount = Suggestions!.TotalCount);
        ContinuityPage = new ContinuityPageViewModel(Navigation);
        EventPage = new EventPageViewModel(Navigation);
        Timeline = new ContinuityTimelineViewModel(id => _goToReader(id));

        // Production passes false: MainViewModel.GoEvents() refreshes on navigation, so startup doesn't pay for two list queries.
        if (loadOnConstruction)
        {
            RefreshSidebar();
            RefreshContinuitiesSidebar();
        }
    }

    public ContinuityScreenNavigation Navigation { get; }

    public ContinuitySidebarViewModel Sidebar { get; }

    public SuggestionsChecksViewModel Suggestions { get; }

    public ContinuityPageViewModel ContinuityPage { get; }

    public EventPageViewModel EventPage { get; }

    public ContinuityTimelineViewModel Timeline { get; }

    /// <summary>Set by the shell: opens the reader anchored to a story event's order.</summary>
    public Action<int, int> GoToReaderInEvent
    {
        set => _goToReaderInEvent = value ?? ((_, _) => { });
    }

    /// <summary>Set by the shell: the issue the reader currently shows, re-selected on the map when the user comes back.</summary>
    public Func<int?>? ReaderIssueId { get; set; }

    // --- What's open ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEventSelected), nameof(IsContinuitySelected), nameof(IsSuggestionsOpen), nameof(HasPage),
        nameof(ShowEmptyPrompt), nameof(ShowEventMap), nameof(ShowDetailScroller), nameof(ActiveMap), nameof(ShowOverview), nameof(ShowTimeline))]
    private ContinuityScreenPage _page = ContinuityScreenPage.None;

    public bool IsEventSelected => Page == ContinuityScreenPage.Event;

    public bool IsContinuitySelected => Page == ContinuityScreenPage.Continuity;

    public bool IsSuggestionsOpen => Page == ContinuityScreenPage.Suggestions;

    public bool HasPage => IsEventSelected || IsContinuitySelected;

    /// <summary>Nothing to show at all: no continuities and no events.</summary>
    public bool HasNothing => Sidebar.HasNoEvents && Sidebar.HasNoContinuities;

    public bool ShowEmptyPrompt => HasNothing && !IsSuggestionsOpen;

    /// <summary>The open story event's id, or null. Read by "Edit details".</summary>
    public int? ActiveEventId => IsEventSelected ? _activeEventId : null;

    /// <summary>The open continuity's id, or null. Read by "Edit details".</summary>
    public int? ActiveContinuityId => IsContinuitySelected ? _activeContinuityId : null;

    // --- Overview | Map | Timeline ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewView), nameof(IsMapView), nameof(IsTimelineView), nameof(ShowEventMap), nameof(ShowDetailScroller), nameof(ActiveMap), nameof(ShowOverview), nameof(ShowTimeline))]
    private EventsDetailView _detailView = EventsDetailView.Primary;

    public bool IsOverviewView => DetailView == EventsDetailView.Primary;

    public bool IsMapView => DetailView == EventsDetailView.Map;

    public bool IsTimelineView => DetailView == EventsDetailView.Timeline;

    /// <summary>The open page's Overview is what the body shows.</summary>
    public bool ShowOverview => HasPage && IsOverviewView;

    public bool ShowTimeline => HasPage && IsTimelineView;

    [RelayCommand]
    private void SetDetailView(EventsDetailView view) => DetailView = view;

    partial void OnDetailViewChanged(EventsDetailView value)
    {
        if (value != EventsDetailView.Primary && !HasPage)
        {
            DetailView = EventsDetailView.Primary;          // nothing selected to map or lay out
            return;
        }

        if (value == EventsDetailView.Timeline)
        {
            LoadTimelineForCurrent();
        }
        else if (value == EventsDetailView.Map)
        {
            LoadMapForCurrent(reselect: null);
        }
    }

    private void LoadTimelineForCurrent()
    {
        if (IsContinuitySelected && _activeContinuityId is int continuityId)
        {
            Timeline.LoadForContinuity(continuityId);
        }
        else if (IsEventSelected && _activeEventId is int eventId)
        {
            Timeline.LoadForEvent(eventId);
        }
    }

    // --- Loading ---

    public void LoadEvent(int storyEventId)
    {
        _activeEventId = storyEventId;
        _activeContinuityId = null;
        Page = ContinuityScreenPage.Event;
        Sidebar.IsSuggestionsActive = false;
        Sidebar.Tab = ContinuitySidebarTab.Events;
        EventPage.Load(storyEventId);
        Sidebar.RefreshEvents();
        Sidebar.RefreshContinuities();
        RaiseSelection();
        FollowSelection();
    }

    public void LoadContinuity(int continuityId)
    {
        _activeContinuityId = continuityId;
        _activeEventId = null;
        Page = ContinuityScreenPage.Continuity;
        Sidebar.IsSuggestionsActive = false;
        Sidebar.Tab = ContinuitySidebarTab.Continuities;
        ContinuityPage.Load(continuityId);
        Sidebar.RefreshContinuities();
        Sidebar.RefreshEvents();
        RaiseSelection();
        FollowSelection();
    }

    private void RaiseSelection()
    {
        OnPropertyChanged(nameof(ActiveEventId));
        OnPropertyChanged(nameof(ActiveContinuityId));
        OnPropertyChanged(nameof(HasNothing));
        OnPropertyChanged(nameof(ShowEmptyPrompt));
    }

    /// <summary>While Map or Timeline shows, it follows the newly opened item (back from the reader, the map re-selects the issue last read).</summary>
    private void FollowSelection()
    {
        if (IsMapView)
        {
            int? reselect = null;
            if (_mapOpenedReader)
            {
                _mapOpenedReader = false;
                reselect = ReaderIssueId?.Invoke();
            }

            LoadMapForCurrent(reselect);
        }
        else if (IsTimelineView)
        {
            LoadTimelineForCurrent();
        }
    }

    /// <summary>Every visit to the screen: reload what's open (so it reflects any change since), or on the first visit open the first
    /// item of the remembered sidebar tab.</summary>
    public void EnsureEventLoaded()
    {
        switch (Page)
        {
            case ContinuityScreenPage.Event when _activeEventId is int eventId:
                LoadEvent(eventId);
                return;
            case ContinuityScreenPage.Continuity when _activeContinuityId is int continuityId:
                LoadContinuity(continuityId);
                return;
            case ContinuityScreenPage.Suggestions:
                return;
        }

        OpenFirst();
    }

    private void OpenFirst()
    {
        Sidebar.RefreshContinuities();
        Sidebar.RefreshEvents();
        bool eventsFirst = Sidebar.Tab == ContinuitySidebarTab.Events;
        var firstContinuity = Sidebar.Continuities.FirstOrDefault();
        var firstEvent = Sidebar.Events.FirstOrDefault();
        if (eventsFirst && firstEvent is not null)
        {
            LoadEvent(firstEvent.Id);
        }
        else if (firstContinuity is not null)
        {
            LoadContinuity(firstContinuity.Id);
        }
        else if (firstEvent is not null)
        {
            LoadEvent(firstEvent.Id);
        }
        else
        {
            ShowNothing();
        }
    }

    private void ShowNothing()
    {
        _activeEventId = null;
        _activeContinuityId = null;
        Page = ContinuityScreenPage.None;
        DetailView = EventsDetailView.Primary;
        RaiseSelection();
    }

    /// <summary>The sidebar's event list (kept under its old name for <see cref="MainViewModel"/>).</summary>
    public void RefreshSidebar()
    {
        Sidebar.RefreshEvents();
        RaiseSelection();
    }

    public void RefreshContinuitiesSidebar()
    {
        Sidebar.RefreshContinuities();
        RaiseSelection();
    }

    public void RefreshStoryEventCandidates() => Suggestions.RefreshStoryEventCandidates();

    public void RefreshPossibleDuplicates() => Suggestions.RefreshPossibleDuplicates();

    [RelayCommand]
    private void SelectEvent(StoryEventSummary? summary)
    {
        if (summary is not null)
        {
            // Map stays on when switching (the map follows the selection); Timeline goes back to Overview.
            if (!IsMapView)
            {
                DetailView = EventsDetailView.Primary;
            }

            LoadEvent(summary.Id);
        }
    }

    [RelayCommand]
    private void SelectContinuity(ContinuitySummary? summary)
    {
        if (summary is not null)
        {
            if (!IsMapView)
            {
                DetailView = EventsDetailView.Primary;
            }

            LoadContinuity(summary.Id);
        }
    }

    [RelayCommand]
    private void OpenSuggestions() => OpenSuggestionsAt(null);

    /// <summary>Opens Suggestions &amp; checks in the main area (the sidebar selection clears), optionally at one event's duplicate pair.</summary>
    private void OpenSuggestionsAt(int? duplicateEventId)
    {
        _activeEventId = null;
        _activeContinuityId = null;
        DetailView = EventsDetailView.Primary;
        Page = ContinuityScreenPage.Suggestions;
        Sidebar.IsSuggestionsActive = true;
        Suggestions.FocusedDuplicateEventId = duplicateEventId;
        Suggestions.RefreshPossibleDuplicates();
        Suggestions.RefreshStoryEventCandidates();
        Sidebar.RefreshEvents();
        Sidebar.RefreshContinuities();
        RaiseSelection();
    }

    // --- Deleting (sidebar rows and the pages' Manage menus) ---

    /// <summary>
    /// Deletes a story event (docs/superpowers/specs/2026-08-22-delete-functionality-design.md): its memberships cascade, the issues stay,
    /// linked reading lists are only unlinked. The refresh is deferred - it rebuilds the list the clicked confirm button lives in.
    /// </summary>
    private void DeleteEvent(int storyEventId)
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            if (context.StoryEvents.Find(storyEventId) is not { } storyEvent)
            {
                return;
            }

            context.StoryEvents.Remove(storyEvent);
            context.SaveChanges();
        }

        Dispatcher.UIThread.Post(() =>
        {
            Sidebar.RefreshEvents();
            if (IsEventSelected && _activeEventId == storyEventId)
            {
                _activeEventId = null;
                if (Sidebar.Events.FirstOrDefault() is { } next)
                {
                    LoadEvent(next.Id);
                }
                else
                {
                    OpenFirst();
                }
            }

            RaiseSelection();
        });
    }

    /// <summary>Deletes a continuity: its series are only unlinked. Deferred like <see cref="DeleteEvent"/>.</summary>
    private void DeleteContinuity(int continuityId)
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            if (context.Continuities.FirstOrDefault(c => c.Id == continuityId) is not { } continuity)
            {
                return;
            }

            context.Continuities.Remove(continuity);
            context.SaveChanges();
        }

        Dispatcher.UIThread.Post(() =>
        {
            Sidebar.RefreshContinuities();
            if (IsContinuitySelected && _activeContinuityId == continuityId)
            {
                _activeContinuityId = null;
                if (Sidebar.Continuities.FirstOrDefault() is { } next)
                {
                    LoadContinuity(next.Id);
                }
                else
                {
                    OpenFirst();
                }
            }

            RaiseSelection();
        });
    }

    /// <summary>After a sweep or merge: if the open event was merged away, open its survivor; otherwise refresh what shows.</summary>
    private void AfterIdentityWork(StoryEventIdentitySweepSummary summary)
    {
        var survivorOf = summary.Merges.ToDictionary(m => m.RemovedId, m => m.SurvivorId);
        if (IsEventSelected && _activeEventId is int active && survivorOf.ContainsKey(active))
        {
            int survivor = active;
            while (survivorOf.TryGetValue(survivor, out int next))
            {
                survivor = next;
            }

            LoadEvent(survivor);
        }
        else if (IsEventSelected && _activeEventId is int open)
        {
            LoadEvent(open);                    // its Related events may have new connections
        }
        else
        {
            Sidebar.RefreshEvents();
            RaiseSelection();
        }
    }

    // --- Event Map (docs/superpowers/specs/2026-09-25-event-map-design.md §4, continuity scope from 2026-09-27-continuity-map-design.md) ---

    private EventMapViewModel? _map;
    private bool _mapOpenedReader;

    /// <summary>The map's own view model, built the first time Map is chosen.</summary>
    public EventMapViewModel Map => _map ??= new EventMapViewModel(
        openReader: (issueId, eventId) =>
        {
            _mapOpenedReader = true;
            if (eventId is int anchor)
            {
                _goToReaderInEvent(issueId, anchor);
            }
            else
            {
                _goToReader(issueId);            // a continuity-map issue that's in no event: plain series order
            }
        },
        openSeriesDetail: seriesId => _goToSeriesDetail(seriesId),
        showAddIssues: ShowAddIssuesFromMap,
        activity: _activity,
        runInBackground: MapBackgroundRunner,
        openEventMap: eventId =>
        {
            LoadEvent(eventId);
            DetailView = EventsDetailView.Map;
        },
        showDuplicates: OpenSuggestions);

    /// <summary>Test seam: how the map runs its load query. Null = a thread-pool task.</summary>
    internal Func<Func<EventMapSource?>, Task<EventMapSource?>>? MapBackgroundRunner { get; set; }

    /// <summary>True once <see cref="Map"/> has been created - Map loads lazily.</summary>
    public bool IsMapCreated => _map is not null;

    /// <summary>The map sits beside, not inside, the page's ScrollViewer - it needs its own two-axis viewport.</summary>
    public bool ShowEventMap => HasPage && IsMapView;

    public bool ShowDetailScroller => !IsMapView || !HasPage;

    /// <summary>The map view's DataContext: null until Map shows, so nothing is built before the user asks for it.</summary>
    public EventMapViewModel? ActiveMap => ShowEventMap ? Map : null;

    /// <summary>Test seam: the most recent map load this screen started.</summary>
    internal Task MapLoadTask { get; private set; } = Task.CompletedTask;

    private void LoadMapForCurrent(int? reselect)
    {
        if (IsContinuitySelected && _activeContinuityId is int continuityId)
        {
            MapLoadTask = Map.LoadContinuityAsync(continuityId, reselect);
        }
        else if (IsEventSelected && _activeEventId is int eventId)
        {
            MapLoadTask = Map.LoadAsync(eventId, reselect);
        }
    }

    /// <summary>The map's empty state: back to Overview with Add issues open.</summary>
    private void ShowAddIssuesFromMap()
    {
        DetailView = EventsDetailView.Primary;
        if (!EventPage.IsAddingIssues)
        {
            EventPage.ToggleAddIssues();
        }
    }
}
