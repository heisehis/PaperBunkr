using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Story Events screen (docs/superpowers/specs/2026-08-17-metadata-model-phase4b-story-events-
/// design.md) - structurally mirrors <see cref="ReadingScreenViewModel"/> (sidebar of named
/// collections, detail pane of an ordered, addable/removable/reorderable issue list), with a
/// <see cref="EventMembershipRole"/> picker per row instead of <c>GroupLabel</c> grouping. No CBL/
/// CSV import-export - that's a Reading-List-specific format with no equivalent for a concept CE
/// never had. Edits persist immediately, same as Reading Lists (no Save/Cancel draft).
/// </summary>
public partial class EventsScreenViewModel : ViewModelBase
{
    private int? _activeEventId;
    private readonly Action<int> _goToSeriesDetail;
    private readonly Action<int> _goToReader;
    private readonly Action<int> _goToReadingList;
    private readonly Action<string, string> _notify;

    public EventsScreenViewModel(
        Action<int>? goToSeriesDetail = null,
        Action<int>? goToReader = null,
        Action<int>? goToReadingList = null,
        Action<string, string>? notify = null)
    {
        _goToSeriesDetail = goToSeriesDetail ?? (_ => { });
        _goToReader = goToReader ?? (_ => { });
        _goToReadingList = goToReadingList ?? (_ => { });
        _notify = notify ?? ((_, _) => { });
        Events = new ObservableCollection<StoryEventSummary>();
        Members = new ObservableCollection<EventMemberRowViewModel>();
        SearchResults = new ObservableCollection<IssueSearchResult>();
        ConnectedEvents = new ObservableCollection<ConnectedEventCard>();
        EventSearchResults = new ObservableCollection<StoryEventSearchResult>();
        SuggestedIssues = new ObservableCollection<SuggestedIssueRowViewModel>();
        Continuities = new ObservableCollection<ContinuitySummary>();
        ContinuityMembers = new ObservableCollection<SeriesCardSample>();
        ContinuitySeriesSearchResults = new ObservableCollection<SeriesSearchResult>();
        OverlappingContinuities = new ObservableCollection<ContinuityOverlapCard>();
        SharedContinuitySeries = new ObservableCollection<SeriesCardSample>();
        TimelineSections = new ObservableCollection<TimelineSectionViewModel>();
        TimelineSeriesSearchResults = new ObservableCollection<SeriesSearchResult>();
        InferredAges = new ObservableCollection<InferredAgeRowViewModel>();
        EventFamily = new ObservableCollection<EventFamilyNodeCard>();
        EventConnectionSuggestions = new ObservableCollection<EventConnectionSuggestionCard>();
        DismissedSuggestions = new ObservableCollection<DismissedSuggestionCard>();
        RefreshSidebar();
    }

    // --- Mode switcher (docs/superpowers/specs/2026-08-27-metadata-model-phase4f-continuity-browse-
    // design.md introduces Events|Continuities; phase4g adds Timeline). Sidebar + detail contents
    // swap per mode; both share the same screen chrome. Non-active modes are lazy-loaded on switch. ---

    [ObservableProperty]
    private EventsScreenMode _screenMode = EventsScreenMode.Events;

    public bool IsEventsMode => ScreenMode == EventsScreenMode.Events;
    public bool IsContinuitiesMode => ScreenMode == EventsScreenMode.Continuities;
    public bool IsTimelineMode => ScreenMode == EventsScreenMode.Timeline;

    partial void OnScreenModeChanged(EventsScreenMode value)
    {
        OnPropertyChanged(nameof(IsEventsMode));
        OnPropertyChanged(nameof(IsContinuitiesMode));
        OnPropertyChanged(nameof(IsTimelineMode));

        switch (value)
        {
            case EventsScreenMode.Continuities:
                RefreshContinuitiesSidebar();
                break;
            case EventsScreenMode.Timeline:
                RefreshTimelineSeriesSidebar();
                break;
        }
    }

    [RelayCommand]
    private void SetMode(EventsScreenMode mode) => ScreenMode = mode;

    public ObservableCollection<StoryEventSummary> Events { get; }
    public ObservableCollection<EventMemberRowViewModel> Members { get; }
    public ObservableCollection<IssueSearchResult> SearchResults { get; }

    /// <summary>Events connected to the active event (docs/superpowers/specs/2026-08-27-metadata-model-phase4d-event-relations-design.md).</summary>
    public ObservableCollection<ConnectedEventCard> ConnectedEvents { get; }

    public ObservableCollection<StoryEventSearchResult> EventSearchResults { get; }

    /// <summary>Reviewable "this issue looks like it belongs to this event" queue (docs/superpowers/specs/2026-08-27-metadata-model-phase4e-format-signal-suggestions-design.md). Dismissals persist (see <see cref="DismissedSuggestions"/>) and can be restored.</summary>
    public ObservableCollection<SuggestedIssueRowViewModel> SuggestedIssues { get; }

    public static EventMembershipRoleOption[] RoleOptions => EventMembershipRoleOption.All;

    /// <summary>
    /// The subset of <see cref="RelationType"/> that describes how one publishing event relates to
    /// another (docs/superpowers/specs/2026-08-27-metadata-model-phase4d-event-relations-design.md)
    /// - the full ~20-value enum stays a single source of truth; only this creation-UI picker is
    /// scoped down. <c>Adaptation</c>/<c>Variant</c>/<c>SecondPrinting</c>/etc. describe a single
    /// work's print history, not two separate cross-series storylines relating to each other.
    /// </summary>
    public static RelationTypeOption[] EventRelationTypeOptions { get; } = new[]
    {
        RelationType.Prequel, RelationType.Sequel, RelationType.Continuation, RelationType.Crossover,
        RelationType.SameUniverse, RelationType.SharedUniverse, RelationType.Related, RelationType.Other,
    }.Select(t => new RelationTypeOption(t, RelationTypeOption.FormatLabel(t))).ToArray();

    [ObservableProperty]
    private string _eventName = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _totalMembers = "0";

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private EventMembershipRole _selectedRole = EventMembershipRole.Core;

    /// <summary>
    /// Bound to the ComboBox's <c>SelectedItem</c> instead of <c>SelectedValue</c>/
    /// <c>SelectedValueBinding</c> - the latter resolves its binding path against this
    /// ViewModel's own DataContext, not the <c>ItemsSource</c> element type, so `{Binding Role}`
    /// there was silently unresolvable (a real, permanent XAML bug, not a build-tooling artifact -
    /// see docs/superpowers/specs/2026-08-18-selectedvaluebinding-xaml-fix-design.md).
    /// </summary>
    [ObservableProperty]
    private EventMembershipRoleOption _selectedRoleOption = RoleOptions.First(o => o.Role == EventMembershipRole.Core);

    partial void OnSelectedRoleOptionChanged(EventMembershipRoleOption value) => SelectedRole = value.Role;

    // --- Connected Events (docs/superpowers/specs/2026-08-27-metadata-model-phase4d-event-relations-design.md) ---

    [ObservableProperty]
    private bool _isConnectingEvent;

    [ObservableProperty]
    private string _connectEventQuery = string.Empty;

    [ObservableProperty]
    private RelationType _selectedEventRelationType = RelationType.Related;

    /// <summary>Bound to the ComboBox's <c>SelectedItem</c> (not <c>SelectedValue</c>) - same permanent XAML-binding-scope bug as <see cref="SelectedRoleOption"/>.</summary>
    [ObservableProperty]
    private RelationTypeOption _selectedEventRelationTypeOption = EventRelationTypeOptions.First(o => o.Type == RelationType.Related);

    partial void OnSelectedEventRelationTypeOptionChanged(RelationTypeOption value) => SelectedEventRelationType = value.Type;

    public bool HasNoEvents => Events.Count == 0;

    public bool HasNoMembers => !HasNoEvents && Members.Count == 0;

    public bool HasNoConnectedEvents => ConnectedEvents.Count == 0;

    [ObservableProperty]
    private bool _suggestionsExpanded;

    public bool HasNoSuggestions => SuggestedIssues.Count == 0;

    public void LoadEvent(int storyEventId)
    {
        _activeEventId = storyEventId;

        using var context = PaperbunkrDb.CreateContext();
        var storyEvent = context.StoryEvents.FirstOrDefault(e => e.Id == storyEventId);
        if (storyEvent is null)
        {
            return;
        }

        EventName = storyEvent.Name;
        Description = storyEvent.Description ?? string.Empty;

        var members = EventMembershipResolver.GetOrderedMembers(context, storyEventId);
        TotalMembers = members.Count.ToString();

        Members.Clear();
        foreach (var member in members)
        {
            Members.Add(new EventMemberRowViewModel(member, MoveMemberUp, MoveMemberDown, RemoveMember, PersistRoleChange));
        }

        ConnectedEvents.Clear();
        foreach (var (otherEvent, displayType, relationId) in EventRelationResolver.GetRelatedEvents(context, storyEventId))
        {
            ConnectedEvents.Add(new ConnectedEventCard
            {
                EventRelationId = relationId,
                OtherEventId = otherEvent.Id,
                Name = otherEvent.Name,
                RelationLabel = RelationTypeOption.FormatLabel(displayType),
            });
        }

        IsConnectingEvent = false;
        ConnectEventQuery = string.Empty;
        EventSearchResults.Clear();

        SuggestedIssues.Clear();
        foreach (var suggestion in EventSuggestionResolver.GetSuggestions(context, storyEventId))
        {
            SuggestedIssues.Add(new SuggestedIssueRowViewModel(suggestion, AddSuggestion, DismissSuggestion));
        }

        RefreshDismissedSuggestions(context, storyEventId);
        RefreshEventFamily(context, storyEventId);
        RefreshEventConnectionSuggestions(context, storyEventId);

        OnPropertyChanged(nameof(HasNoMembers));
        OnPropertyChanged(nameof(HasNoConnectedEvents));
        OnPropertyChanged(nameof(HasNoSuggestions));
        RefreshSidebar();
    }

    /// <summary>Called on every navigation to the Events screen - re-loads the active event (so it reflects any change since the last visit) or, on first visit, opens the first event.</summary>
    public void EnsureEventLoaded()
    {
        if (_activeEventId is int activeId)
        {
            LoadEvent(activeId);
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var firstId = context.StoryEvents.OrderBy(e => e.Name).Select(e => (int?)e.Id).FirstOrDefault();
        if (firstId is int id)
        {
            LoadEvent(id);
        }
    }

    public void RefreshSidebar()
    {
        using var context = PaperbunkrDb.CreateContext();
        var all = context.StoryEvents.Include(e => e.Members).OrderBy(e => e.Name).ToList();

        Events.Clear();
        foreach (var storyEvent in all)
        {
            int eventId = storyEvent.Id;
            Events.Add(new StoryEventSummary
            {
                Id = storyEvent.Id,
                Name = storyEvent.Name,
                MemberCount = storyEvent.Members.Count,
                IsActive = storyEvent.Id == _activeEventId,
                DeleteConfirm = new TwoStepConfirm(() => DeleteEvent(eventId), idleLabel: "Delete", armedLabel: "Confirm delete?"),
            });
        }

        OnPropertyChanged(nameof(HasNoEvents));
        OnPropertyChanged(nameof(HasNoMembers));
    }

    /// <summary>
    /// Deletes a whole story event (docs/superpowers/specs/2026-08-22-delete-functionality-design.md) -
    /// cascade-deletes its <see cref="EventMembership"/> rows (confirmed <c>DeleteBehavior.Cascade</c>
    /// in <c>PaperbunkrDbContext.OnModelCreating</c>, not the member <see cref="Issue"/>s
    /// themselves). Any <see cref="ReadingList"/> linking to this event via
    /// <c>ReadingList.StoryEventId</c> is left intact, just unlinked (that FK is
    /// <c>DeleteBehavior.SetNull</c>) - deleting an event's own reading-order tracking never takes
    /// the reading list itself down with it.
    /// </summary>
    private void DeleteEvent(int storyEventId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var storyEvent = context.StoryEvents.Find(storyEventId);
        if (storyEvent is null)
        {
            return;
        }

        context.StoryEvents.Remove(storyEvent);
        context.SaveChanges();

        if (_activeEventId == storyEventId)
        {
            _activeEventId = null;
            var nextId = context.StoryEvents.OrderBy(e => e.Name).Select(e => (int?)e.Id).FirstOrDefault();
            if (nextId is int id)
            {
                LoadEvent(id);
                return;
            }

            EventName = string.Empty;
            Description = string.Empty;
            TotalMembers = "0";
            Members.Clear();
            OnPropertyChanged(nameof(HasNoMembers));
        }

        RefreshSidebar();
    }

    private void MoveMemberUp(EventMemberRowViewModel row) => Reorder(row, offset: -1);

    private void MoveMemberDown(EventMemberRowViewModel row) => Reorder(row, offset: 1);

    private void Reorder(EventMemberRowViewModel row, int offset)
    {
        if (_activeEventId is not int eventId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        EventMembershipResolver.Reorder(context, row.Member.Id, offset);
        LoadEvent(eventId);
    }

    private void RemoveMember(EventMemberRowViewModel row)
    {
        if (_activeEventId is not int eventId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        EventMembershipResolver.RemoveMember(context, row.Member.Id);
        LoadEvent(eventId);
    }

    private void PersistRoleChange(EventMemberRowViewModel row)
    {
        using var context = PaperbunkrDb.CreateContext();
        var member = context.EventMemberships.Find(row.Member.Id);
        if (member is not null)
        {
            member.Role = row.SelectedRole;
            context.SaveChanges();
        }
    }

    [RelayCommand]
    private void Search()
    {
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var matches = context.Issues
            .Include(i => i.Series)
            .AsEnumerable()
            .Where(i => (i.Series?.Name ?? string.Empty).Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
                || (i.EffectiveNumber() ?? string.Empty).Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
            .Take(20);

        foreach (var issue in matches)
        {
            SearchResults.Add(new IssueSearchResult
            {
                IssueId = issue.Id,
                DisplayLabel = $"{issue.Series?.Name ?? "Unknown"} #{issue.EffectiveNumber()}",
            });
        }
    }

    [RelayCommand]
    private void AddIssue(IssueSearchResult? result)
    {
        if (result is null || _activeEventId is not int eventId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        EventMembershipResolver.AddMember(context, eventId, result.IssueId, SelectedRole);

        SearchResults.Clear();
        SearchQuery = string.Empty;
        LoadEvent(eventId);
    }

    [RelayCommand]
    private void CreateNew()
    {
        using var context = PaperbunkrDb.CreateContext();
        var storyEvent = new StoryEvent { Name = "New Story Event", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.StoryEvents.Add(storyEvent);
        context.SaveChanges();
        LoadEvent(storyEvent.Id);
    }

    [RelayCommand]
    private void SelectEvent(StoryEventSummary? summary)
    {
        if (summary is not null)
        {
            LoadEvent(summary.Id);
        }
    }

    // --- Connected Events commands (docs/superpowers/specs/2026-08-27-metadata-model-phase4d-event-relations-design.md) ---

    [RelayCommand]
    private void ToggleConnectEvent()
    {
        IsConnectingEvent = !IsConnectingEvent;
        ConnectEventQuery = string.Empty;
        EventSearchResults.Clear();
    }

    partial void OnConnectEventQueryChanged(string value) => SearchEvents();

    [RelayCommand]
    private void SearchEvents()
    {
        EventSearchResults.Clear();
        string query = ConnectEventQuery.Trim();
        if (query.Length == 0 || _activeEventId is not int activeId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var matches = context.StoryEvents
            .Where(e => e.Id != activeId)
            .AsEnumerable()
            .Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(20);

        foreach (var storyEvent in matches)
        {
            EventSearchResults.Add(new StoryEventSearchResult { StoryEventId = storyEvent.Id, Name = storyEvent.Name });
        }
    }

    [RelayCommand]
    private void ConnectEvent(StoryEventSearchResult? result)
    {
        if (result is null || _activeEventId is not int activeId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        EventRelationResolver.TryCreate(context, activeId, result.StoryEventId, SelectedEventRelationType);

        IsConnectingEvent = false;
        ConnectEventQuery = string.Empty;
        EventSearchResults.Clear();
        LoadEvent(activeId);
    }

    [RelayCommand]
    private void RemoveConnectedEvent(ConnectedEventCard? card)
    {
        if (card is null || _activeEventId is not int activeId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        EventRelationResolver.Remove(context, card.EventRelationId);
        LoadEvent(activeId);
    }

    /// <summary>Clicking a connected event's card switches the screen's active event to it - a natural way to walk an event chain without leaving the screen.</summary>
    [RelayCommand]
    private void OpenConnectedEvent(ConnectedEventCard? card)
    {
        if (card is not null)
        {
            LoadEvent(card.OtherEventId);
        }
    }

    // --- Suggested Issues (docs/superpowers/specs/2026-08-27-metadata-model-phase4e-format-signal-suggestions-design.md) ---

    private void AddSuggestion(SuggestedIssueRowViewModel row)
    {
        if (_activeEventId is not int eventId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        EventMembershipResolver.AddMember(context, eventId, row.IssueId, row.SelectedRole);
        LoadEvent(eventId);
    }

    /// <summary>Persists a "never suggest this issue for this event again" marker (docs/superpowers/specs/2026-08-27-metadata-model-phase4e-format-signal-suggestions-design.md) - reversible from the Dismissed list.</summary>
    private void DismissSuggestion(SuggestedIssueRowViewModel row)
    {
        if (_activeEventId is not int eventId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        EventSuggestionResolver.Dismiss(context, eventId, row.IssueId);
        LoadEvent(eventId);
    }

    [RelayCommand]
    private void ToggleSuggestions() => SuggestionsExpanded = !SuggestionsExpanded;
}
