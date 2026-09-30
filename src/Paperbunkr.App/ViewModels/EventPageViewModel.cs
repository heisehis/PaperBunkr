using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.ContinuityScreen;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Gcd;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One story event's page (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md, "Event page"): the hero (stats, source
/// and continuity chips, Continue, Add issues, Manage), Follows ← This event → Followed by, Needs attention, the reading list with its
/// filter chips, Related events and Issue suggestions. The member list and panels load synchronously as on the old screen (ported
/// behaviour unchanged, docs/superpowers/specs/2026-08-17-metadata-model-phase4b-story-events-design.md and the phase 4d/4e specs); the
/// overview is worked out off the UI thread. Edits persist immediately.
/// </summary>
public partial class EventPageViewModel : ViewModelBase
{
    private readonly ContinuityScreenNavigation _nav;
    private int _overviewGeneration;

    public EventPageViewModel(ContinuityScreenNavigation nav) => _nav = nav;

    public int? EventId { get; private set; }

    public static EventMembershipRoleOption[] RoleOptions => EventMembershipRoleOption.All;

    public static string[] RoleNames { get; } = RoleOptions.Select(o => o.Label).ToArray();

    /// <summary>How one event relates to another (docs/superpowers/specs/2026-08-27-metadata-model-phase4d-event-relations-design.md) -
    /// the creation picker's subset of <see cref="RelationType"/>.</summary>
    public static RelationTypeOption[] RelationTypeOptions { get; } = new[]
    {
        RelationType.Prequel, RelationType.Sequel, RelationType.Continuation, RelationType.Crossover,
        RelationType.SameUniverse, RelationType.SharedUniverse, RelationType.Related, RelationType.Other,
    }.Select(t => new RelationTypeOption(t, RelationTypeOption.FormatLabel(t))).ToArray();

    public static string[] RelationTypeNames { get; } = RelationTypeOptions.Select(o => o.Label).ToArray();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    private string _description = string.Empty;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    [ObservableProperty]
    private bool _isDescriptionExpanded;

    [RelayCommand]
    private void ToggleDescription() => IsDescriptionExpanded = !IsDescriptionExpanded;

    // --- Reading list ---

    /// <summary>Every member in reading order. Commands and selection act on this; the view shows <see cref="VisibleMembers"/>.</summary>
    public ObservableCollection<EventMemberRowViewModel> Members { get; } = new();

    /// <summary><see cref="Members"/> through the active filter, still in reading order.</summary>
    public ObservableCollection<EventMemberRowViewModel> VisibleMembers { get; } = new();

    public bool HasNoMembers => EventId is not null && Members.Count == 0;

    public bool HasNoVisibleMembers => Members.Count > 0 && VisibleMembers.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllFilter), nameof(IsCoreFilter), nameof(IsHideOptionalFilter), nameof(IsUnreadFilter), nameof(IsNeedsReviewFilter))]
    private EventMemberFilter _filter = EventMemberFilter.All;

    public bool IsAllFilter => Filter == EventMemberFilter.All;

    public bool IsCoreFilter => Filter == EventMemberFilter.Core;

    public bool IsHideOptionalFilter => Filter == EventMemberFilter.HideOptional;

    public bool IsUnreadFilter => Filter == EventMemberFilter.Unread;

    public bool IsNeedsReviewFilter => Filter == EventMemberFilter.NeedsReview;

    public string AllFilterLabel => $"All {Members.Count}";

    public string CoreFilterLabel => $"Core {Members.Count(m => m.SelectedRole == EventMembershipRole.Core)}";

    public string UnreadFilterLabel => $"Unread {Members.Count(m => !m.IsRead)}";

    public int RoleSuggestionCount => Members.Count(m => m.HasRoleSuggestion);

    public bool HasRoleSuggestions => RoleSuggestionCount > 0;

    public string NeedsReviewFilterLabel => $"Needs review {RoleSuggestionCount}";

    [RelayCommand]
    private void SetFilter(EventMemberFilter filter) => Filter = filter;

    partial void OnFilterChanged(EventMemberFilter value) => ApplyFilter();

    private bool Passes(EventMemberRowViewModel row) => Filter switch
    {
        EventMemberFilter.Core => row.SelectedRole == EventMembershipRole.Core,
        EventMemberFilter.HideOptional => row.SelectedRole != EventMembershipRole.Optional,
        EventMemberFilter.Unread => !row.IsRead,
        EventMemberFilter.NeedsReview => row.HasRoleSuggestion,
        _ => true,
    };

    private void ApplyFilter()
    {
        if (Filter == EventMemberFilter.NeedsReview && !HasRoleSuggestions)
        {
            Filter = EventMemberFilter.All;             // re-enters through OnFilterChanged
            return;
        }

        VisibleMembers.Clear();
        foreach (var row in Members.Where(Passes))
        {
            VisibleMembers.Add(row);
        }

        OnPropertyChanged(nameof(HasNoVisibleMembers));
        OnPropertyChanged(nameof(AllFilterLabel));
        OnPropertyChanged(nameof(CoreFilterLabel));
        OnPropertyChanged(nameof(UnreadFilterLabel));
        OnPropertyChanged(nameof(NeedsReviewFilterLabel));
        OnPropertyChanged(nameof(RoleSuggestionCount));
        OnPropertyChanged(nameof(HasRoleSuggestions));
        RefreshAttention();
    }

    // --- Related events, suggestions, chain ---

    /// <summary>Non-directional links (crossovers and the like). Prequel/Sequel/Continuation show in the Follows / Followed by strip.</summary>
    public ObservableCollection<ConnectedEventCard> ConnectedEvents { get; } = new();

    /// <summary>Every connection, directional ones included - what Remove and the old "connected" count work from.</summary>
    public ObservableCollection<ConnectedEventCard> AllConnections { get; } = new();

    public ObservableCollection<StoryEventSearchResult> EventSearchResults { get; } = new();

    public ObservableCollection<SuggestedIssueRowViewModel> SuggestedIssues { get; } = new();

    public ObservableCollection<DismissedSuggestionCard> DismissedSuggestions { get; } = new();

    public ObservableCollection<EventFamilyNodeCard> EventFamily { get; } = new();

    public ObservableCollection<EventConnectionSuggestionCard> ConnectionSuggestions { get; } = new();

    public bool HasNoConnectedEvents => ConnectedEvents.Count == 0;

    public bool HasNoSuggestions => SuggestedIssues.Count == 0;

    public bool HasNoDismissed => DismissedSuggestions.Count == 0;

    public bool HasNoConnectionSuggestions => ConnectionSuggestions.Count == 0;

    /// <summary>True only when the graph reaches beyond this event and its direct connections (a real chain).</summary>
    public bool HasEventChain => EventFamily.Count > AllConnections.Count + 1;

    [ObservableProperty]
    private bool _relatedEventsExpanded;

    [ObservableProperty]
    private bool _issueSuggestionsExpanded;

    [ObservableProperty]
    private bool _eventFamilyExpanded;

    [ObservableProperty]
    private bool _dismissedExpanded;

    [RelayCommand]
    private void ToggleRelatedEvents() => RelatedEventsExpanded = !RelatedEventsExpanded;

    [RelayCommand]
    private void ToggleIssueSuggestions() => IssueSuggestionsExpanded = !IssueSuggestionsExpanded;

    [RelayCommand]
    private void ToggleEventFamily() => EventFamilyExpanded = !EventFamilyExpanded;

    [RelayCommand]
    private void ToggleDismissed() => DismissedExpanded = !DismissedExpanded;

    public string RelatedEventsSummary => $"{ConnectedEvents.Count} connected · {ConnectionSuggestions.Count} suggested";

    public string IssueSuggestionsSummary => SuggestedIssues.Count == 1 ? "1 to review" : $"{SuggestedIssues.Count} to review";

    /// <summary>Raised when an attention item asks the view to bring a panel into view ("issues", "related").</summary>
    public event Action<string>? ScrollToPanelRequested;

    // --- Load ---

    public void Load(int storyEventId)
    {
        bool switched = EventId != storyEventId;
        EventId = storyEventId;

        using var context = PaperbunkrDb.CreateContext();
        var storyEvent = context.StoryEvents.AsNoTracking().FirstOrDefault(e => e.Id == storyEventId);
        if (storyEvent is null)
        {
            return;
        }

        Name = storyEvent.Name;
        Description = storyEvent.Description ?? string.Empty;

        Members.Clear();
        int position = 1;
        foreach (var member in EventMembershipResolver.GetOrderedMembers(context, storyEventId))
        {
            Members.Add(new EventMemberRowViewModel(member, MoveMemberUp, MoveMemberDown, RemoveMember, PersistRoleChange) { Position = position++ });
        }

        MemberSelection.Clear();
        SearchSelection.Clear();
        RaiseSelectionState();

        IsAddingIssues = false;
        SearchQuery = string.Empty;
        SearchResults.Clear();

        LoadConnections(context, storyEventId);

        IsConnectingEvent = false;
        ConnectEventQuery = string.Empty;
        EventSearchResults.Clear();

        SuggestedIssues.Clear();
        foreach (var suggestion in EventSuggestionResolver.GetSuggestions(context, storyEventId))
        {
            SuggestedIssues.Add(new SuggestedIssueRowViewModel(suggestion, AddSuggestion, DismissSuggestion));
        }

        DismissedSuggestions.Clear();
        foreach (var issue in EventSuggestionResolver.GetDismissed(context, storyEventId))
        {
            DismissedSuggestions.Add(new DismissedSuggestionCard { IssueId = issue.Id, Label = $"{issue.Series?.Name ?? "Unknown"} #{issue.EffectiveNumber()}" });
        }

        EventFamily.Clear();
        foreach (var (evt, depth) in EventRelationResolver.GetEventFamily(context, storyEventId))
        {
            EventFamily.Add(new EventFamilyNodeCard { EventId = evt.Id, Name = evt.Name, Depth = depth });
        }

        LoadConnectionSuggestions(context, storyEventId);

        if (switched)
        {
            Filter = EventMemberFilter.All;
            IsDescriptionExpanded = false;
            _deleteConfirm?.Cancel();          // an armed delete never carries over to another event
            ClearOverview();
        }

        OnPropertyChanged(nameof(HasNoMembers));
        OnPropertyChanged(nameof(HasNoConnectedEvents));
        OnPropertyChanged(nameof(HasNoSuggestions));
        OnPropertyChanged(nameof(HasNoDismissed));
        OnPropertyChanged(nameof(HasEventChain));
        OnPropertyChanged(nameof(RelatedEventsSummary));
        OnPropertyChanged(nameof(IssueSuggestionsSummary));
        ApplyFilter();
        _ = LoadOverviewAsync();
    }

    private void LoadConnections(PaperbunkrDbContext context, int storyEventId)
    {
        var related = EventRelationResolver.GetRelatedEvents(context, storyEventId);
        var relationIds = related.Select(r => r.EventRelationId).ToList();
        var automaticSource = context.EventRelations
            .Where(r => relationIds.Contains(r.Id))
            .Select(r => new { r.Id, Providers = r.Evidence.Select(e => e.Provider).ToList() })
            .AsEnumerable()
            .Where(r => r.Providers.Count > 0 && r.Providers.All(p => p is RelationEvidenceProvider.Inferred or RelationEvidenceProvider.Wikidata))
            .ToDictionary(r => r.Id, r => r.Providers.Contains(RelationEvidenceProvider.Wikidata) ? "Wikidata" : "inferred");

        ConnectedEvents.Clear();
        AllConnections.Clear();
        foreach (var (otherEvent, displayType, relationId) in related)
        {
            var card = new ConnectedEventCard
            {
                EventRelationId = relationId,
                OtherEventId = otherEvent.Id,
                Name = otherEvent.Name,
                RelationLabel = RelationTypeOption.FormatLabel(displayType),
                SourceLabel = automaticSource.GetValueOrDefault(relationId),
            };
            AllConnections.Add(card);
            if (!EventChronology.IsDirectional(displayType))
            {
                ConnectedEvents.Add(card);
            }
        }
    }

    private void LoadConnectionSuggestions(PaperbunkrDbContext context, int storyEventId)
    {
        ConnectionSuggestions.Clear();

        // The smart connector's typed suggestions first (weak inferences only - strong ones are saved as inferred relations).
        var names = context.StoryEvents.Select(e => new { e.Id, e.Name }).ToDictionary(e => e.Id, e => e.Name);
        var typedPartners = new HashSet<int>();
        using var gcd = _nav.OpenGcd();
        foreach (var inference in EventChronologyInference.Infer(context, storyEventId, gcd).Where(i => !i.IsStrong))
        {
            int other = inference.SourceEventId == storyEventId ? inference.TargetEventId : inference.SourceEventId;
            typedPartners.Add(other);
            ConnectionSuggestions.Add(new EventConnectionSuggestionCard
            {
                CandidateEventId = other,
                Name = names.GetValueOrDefault(other, "Event"),
                Reason = inference.Reason,
                SuggestedType = inference.Type,
                SourceEventId = inference.SourceEventId,
                TargetEventId = inference.TargetEventId,
                Headline = TypedHeadline(inference, storyEventId, names),
            });
        }

        foreach (var suggestion in EventRelationSuggestionResolver.GetSuggestions(context, storyEventId))
        {
            if (!typedPartners.Contains(suggestion.Candidate.Id))
            {
                ConnectionSuggestions.Add(new EventConnectionSuggestionCard
                {
                    CandidateEventId = suggestion.Candidate.Id,
                    Name = suggestion.Candidate.Name,
                    Reason = suggestion.Reason,
                    Headline = suggestion.Candidate.Name,
                });
            }
        }

        OnPropertyChanged(nameof(HasNoConnectionSuggestions));
    }

    /// <summary>"Looks like the sequel of Planet Hulk", from the viewed event's side.</summary>
    private static string TypedHeadline(InferredEventRelation inference, int viewedId, IReadOnlyDictionary<int, string> names)
    {
        bool viewedIsSource = inference.SourceEventId == viewedId;
        int other = viewedIsSource ? inference.TargetEventId : inference.SourceEventId;
        string otherName = names.GetValueOrDefault(other, "another event");
        string relation = inference.Type switch
        {
            RelationType.Crossover => "Looks like a crossover with",
            RelationType.Sequel => viewedIsSource ? "Looks like the sequel of" : "Looks like it's followed by",
            RelationType.Prequel => viewedIsSource ? "Looks like the prequel of" : "Looks like it follows",
            RelationType.Continuation => viewedIsSource ? "Looks like it continues" : "Looks like it's continued by",
            _ => "Looks related to",
        };
        return $"{relation} {otherName}";
    }

    /// <summary>Reload after an edit here, then the sidebar's counts.</summary>
    private void Reload()
    {
        if (EventId is int id)
        {
            Load(id);
            _nav.SidebarChanged();
        }
    }

    /// <summary>Reload once the click that asked for it has finished routing: the clicked control lives in a row (or a row's menu) that
    /// the reload replaces (CLAUDE.md, routed-event detach rule).</summary>
    private void ReloadAfterClick() => Dispatcher.UIThread.Post(Reload);
    // --- Overview (off the UI thread) ---

    [ObservableProperty]
    private string _statsLine = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSourceLabel))]
    private string? _sourceLabel;

    public bool HasSourceLabel => SourceLabel is not null;

    public ObservableCollection<EventContinuityRef> Continuities { get; } = new();

    public ObservableCollection<EventContinuityRef> MoreContinuities { get; } = new();

    public EventContinuityRef? FirstContinuity => Continuities.FirstOrDefault();

    public bool HasContinuity => Continuities.Count > 0;

    public bool HasMoreContinuities => MoreContinuities.Count > 0;

    public string ContinuityChipLabel => FirstContinuity is { } c ? $"in {c.Name}" : string.Empty;

    public string MoreContinuitiesLabel => $"+{MoreContinuities.Count}";

    public ObservableCollection<EventNeighbour> Follows { get; } = new();

    public ObservableCollection<EventNeighbour> FollowedBy { get; } = new();

    public bool HasNeighbours => Follows.Count > 0 || FollowedBy.Count > 0;

    public bool HasFollows => Follows.Count > 0;

    public bool HasFollowedBy => FollowedBy.Count > 0;

    public ObservableCollection<string> CollageKeys { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContinue), nameof(ContinueLabel))]
    private ContinueTarget? _continue;

    public bool HasContinue => Continue is not null;

    public string ContinueLabel => Continue is { } c ? $"Continue · {c.Label}" : string.Empty;

    [ObservableProperty]
    private bool _isOverviewLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverviewError))]
    private string? _overviewError;

    public bool HasOverviewError => OverviewError is not null;

    private EventMapEventRef? _possibleDuplicate;

    public ObservableCollection<AttentionItem> Attention { get; } = new();

    public bool HasAttention => Attention.Count > 0;

    /// <summary>The most recent overview load - tests await it.</summary>
    internal Task OverviewTask { get; private set; } = Task.CompletedTask;

    private Task LoadOverviewAsync()
    {
        if (EventId is not int id)
        {
            return Task.CompletedTask;
        }

        int generation = ++_overviewGeneration;
        IsOverviewLoading = true;
        OverviewError = null;
        return OverviewTask = LoadOverviewCoreAsync(id, generation);
    }

    private async Task LoadOverviewCoreAsync(int id, int generation)
    {
        EventOverview? overview;
        try
        {
            overview = (EventOverview?)await _nav.Runner(() =>
            {
                using var context = PaperbunkrDb.CreateContext();
                using var gcd = _nav.OpenGcd();
                return EventOverviewBuilder.Load(context, id, gcd);
            });
        }
        catch (Exception ex)
        {
            if (generation == _overviewGeneration)
            {
                IsOverviewLoading = false;
                OverviewError = $"Couldn't load this event's overview: {ex.Message}";
                _nav.Activity.RaiseAlert(new ActivityAlert
                {
                    Severity = ActivityAlertSeverity.Warning,
                    Title = "Event overview failed to load",
                    Detail = ex.Message,
                });
            }

            return;
        }

        if (generation != _overviewGeneration || EventId != id)
        {
            return;
        }

        IsOverviewLoading = false;
        ApplyOverview(overview);
    }

    [RelayCommand]
    private Task RetryOverview() => LoadOverviewAsync();

    private void ClearOverview()
    {
        _overviewGeneration++;
        ApplyOverview(null);
    }

    private void ApplyOverview(EventOverview? overview)
    {
        StatsLine = overview?.StatsLine ?? string.Empty;
        SourceLabel = overview?.SourceLabel;
        Continue = overview?.Continue;
        _possibleDuplicate = overview?.PossibleDuplicate;

        Continuities.Clear();
        MoreContinuities.Clear();
        foreach (var (c, i) in (overview?.Continuities ?? Array.Empty<EventContinuityRef>()).Select((c, i) => (c, i)))
        {
            Continuities.Add(c);
            if (i > 0)
            {
                MoreContinuities.Add(c);
            }
        }

        Follows.Clear();
        foreach (var n in overview?.Follows ?? Array.Empty<EventNeighbour>())
        {
            Follows.Add(n);
        }

        FollowedBy.Clear();
        foreach (var n in overview?.FollowedBy ?? Array.Empty<EventNeighbour>())
        {
            FollowedBy.Add(n);
        }

        CollageKeys.Clear();
        foreach (var key in overview?.CollageKeys ?? Array.Empty<string>())
        {
            CollageKeys.Add(key);
        }

        OnPropertyChanged(nameof(FirstContinuity));
        OnPropertyChanged(nameof(HasContinuity));
        OnPropertyChanged(nameof(HasMoreContinuities));
        OnPropertyChanged(nameof(ContinuityChipLabel));
        OnPropertyChanged(nameof(MoreContinuitiesLabel));
        OnPropertyChanged(nameof(HasNeighbours));
        OnPropertyChanged(nameof(HasFollows));
        OnPropertyChanged(nameof(HasFollowedBy));
        RefreshAttention();
    }

    private void RefreshAttention()
    {
        Attention.Clear();
        foreach (var item in EventOverviewBuilder.Attention(RoleSuggestionCount, SuggestedIssues.Count, ConnectionSuggestions.Count, _possibleDuplicate?.Name))
        {
            Attention.Add(item);
        }

        OnPropertyChanged(nameof(HasAttention));
    }

    /// <summary>An attention item: the Needs review filter, the Issue suggestions or Related events panel, or the duplicate pair.</summary>
    [RelayCommand]
    private void OpenAttention(AttentionItem? item)
    {
        switch (item?.Kind)
        {
            case AttentionKind.RoleSuggestions:
                Filter = EventMemberFilter.NeedsReview;
                break;
            case AttentionKind.IssueSuggestions:
                IssueSuggestionsExpanded = true;
                ScrollToPanelRequested?.Invoke("issues");
                break;
            case AttentionKind.Connections:
                RelatedEventsExpanded = true;
                ScrollToPanelRequested?.Invoke("related");
                break;
            case AttentionKind.Duplicate:
                _nav.OpenSuggestions(EventId);
                break;
        }
    }

    // --- Hero actions ---

    [RelayCommand]
    private void ContinueReading()
    {
        if (Continue is { } target && EventId is int eventId)
        {
            _nav.GoToReaderInEvent(target.IssueId, target.EventId ?? eventId);
        }
    }

    /// <summary>A neighbour card or a continuity chip swaps the page it lives on - deferred one tick.</summary>
    [RelayCommand]
    private void OpenNeighbour(EventNeighbour? neighbour)
    {
        if (neighbour is not null)
        {
            int id = neighbour.EventId;
            Dispatcher.UIThread.Post(() => _nav.OpenEvent(id));
        }
    }

    /// <summary>Removes the before/after link(s) to a neighbour - the strip's Unlink, since directional links no longer show in Related
    /// events. A link the smart connector made is also dismissed so it isn't re-created.</summary>
    [RelayCommand]
    private void UnlinkNeighbour(EventNeighbour? neighbour)
    {
        if (neighbour is null)
        {
            return;
        }

        var relationIds = AllConnections.Where(c => c.OtherEventId == neighbour.EventId && !ConnectedEvents.Contains(c)).Select(c => c.EventRelationId).ToList();
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (int relationId in relationIds)
            {
                EventConnectorSweep.RemoveRelation(context, relationId);
            }
        }

        ReloadAfterClick();
    }

    [RelayCommand]
    private void OpenContinuity(EventContinuityRef? continuity)
    {
        if (continuity is not null)
        {
            int id = continuity.ContinuityId;
            Dispatcher.UIThread.Post(() => _nav.OpenContinuity(id));
        }
    }

    [RelayCommand]
    private void DeleteEvent()
    {
        if (EventId is int id)
        {
            _nav.DeleteEvent(id);
        }
    }

    /// <summary>Manage → Delete event: the same two-click confirm as the sidebar row.</summary>
    public TwoStepConfirm DeleteConfirm => _deleteConfirm ??= new TwoStepConfirm(() => DeleteEventCommand.Execute(null),
        idleLabel: "Delete event", armedLabel: "Confirm delete event?");

    private TwoStepConfirm? _deleteConfirm;

    /// <summary>The "Detect roles" action: roles it is sure of are applied (where the role is itself automatic); the rest are offered
    /// as suggestions. Reported through the Activity Center.</summary>
    [RelayCommand]
    private void DetectRoles()
    {
        if (EventId is not int eventId)
        {
            return;
        }

        using var job = _nav.Activity.StartJob(ActivityJobKind.Other, "Detecting roles in this event");
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            var summary = MemberRoleDetection.DetectForEvent(context, eventId);
            job.Succeed(summary.ToString(), itemsProcessed: summary.Applied + summary.Suggested);
            _nav.Notify("Roles detected", summary.ToString());
        }
        catch (Exception ex)
        {
            job.Fail($"Role detection failed: {ex.Message}", ex: ex);
            _nav.Notify("Role detection failed", ex.Message);
            return;
        }

        // Reload after the click has finished routing (the rows being rebuilt include the control that raised it).
        Dispatcher.UIThread.Post(Reload);
    }

    // --- Member rows ---

    private void MoveMemberUp(EventMemberRowViewModel row) => Reorder(row, offset: -1);

    private void MoveMemberDown(EventMemberRowViewModel row) => Reorder(row, offset: 1);

    private void Reorder(EventMemberRowViewModel row, int offset)
    {
        if (EventId is null)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            EventMembershipResolver.Reorder(context, row.Member.Id, offset);
        }

        ReloadAfterClick();
    }

    private void RemoveMember(EventMemberRowViewModel row)
    {
        if (EventId is null)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            EventMembershipResolver.RemoveMember(context, row.Member.Id);
        }

        ReloadAfterClick();
    }

    private void PersistRoleChange(EventMemberRowViewModel row)
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            var member = context.EventMemberships.Find(row.Member.Id);
            if (member is not null)
            {
                member.Role = row.SelectedRole;
                MemberRoleApplier.CopyState(row.Member, member);      // role source / reason / pending suggestion, as the row now shows them
                context.SaveChanges();
            }
        }

        // The filter counts and Needs review follow the change; the row set itself only changes when the filter hides the row, which
        // must wait until the menu/button that raised it has finished routing.
        Dispatcher.UIThread.Post(ApplyFilter);
    }

    // --- Bulk selection ---

    public TileSelectionController<IssueSearchResult> SearchSelection { get; } = new();

    public TileSelectionController<EventMemberRowViewModel> MemberSelection { get; } = new();

    public bool AnySearchSelected => SearchSelection.Count > 0;

    public bool AnyMembersSelected => MemberSelection.Count > 0;

    public string SearchSelectionSummary => $"{SearchSelection.Count} selected";

    public string MemberSelectionSummary => $"{MemberSelection.Count} selected";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BulkRoleText))]
    private EventMembershipRoleOption? _bulkRole;

    public string BulkRoleText
    {
        get => BulkRole?.Label ?? string.Empty;
        set => BulkRole = RoleOptions.FirstOrDefault(o => o.Label == value);
    }

    private void RaiseSelectionState()
    {
        OnPropertyChanged(nameof(AnySearchSelected));
        OnPropertyChanged(nameof(AnyMembersSelected));
        OnPropertyChanged(nameof(SearchSelectionSummary));
        OnPropertyChanged(nameof(MemberSelectionSummary));
    }

    [RelayCommand]
    private void ToggleSearchSelection(IssueSearchResult? r)
    {
        if (r is null) return;
        SearchSelection.Toggle(SearchResults, r, isShiftHeld: false);
        RaiseSelectionState();
    }

    [RelayCommand]
    private void ToggleMemberSelection(EventMemberRowViewModel? row)
    {
        if (row is null) return;
        MemberSelection.Toggle(Members, row, isShiftHeld: false);
        RaiseSelectionState();
    }

    [RelayCommand]
    private void ClearSearchSelection()
    {
        SearchSelection.Clear(SearchResults);
        RaiseSelectionState();
    }

    [RelayCommand]
    private void ClearMemberSelection()
    {
        MemberSelection.Clear(Members);
        RaiseSelectionState();
    }

    [RelayCommand]
    private void AddSelectedMembers()
    {
        if (EventId is not int eventId || SearchSelection.Count == 0)
        {
            return;
        }

        var role = BulkRole?.Role ?? SelectedRole;
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (var r in SearchResults.Where(x => SearchSelection.SelectedIds.Contains(x.Id)))
            {
                EventMembershipResolver.AddMember(context, eventId, r.IssueId, role);
            }
        }

        SearchSelection.Clear();
        SearchQuery = string.Empty;
        RaiseSelectionState();
        Reload();
    }

    [RelayCommand]
    private void AddAllOfSeries(IssueSearchResult? r)
    {
        if (r is null || EventId is not int eventId)
        {
            return;
        }

        var role = BulkRole?.Role ?? SelectedRole;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var issues = context.Issues.Where(i => i.SeriesId == r.SeriesId && !i.IsPlaceholder).AsEnumerable().OrderByNumber();
            foreach (var issue in issues)
            {
                EventMembershipResolver.AddMember(context, eventId, issue.Id, role);
            }
        }

        ReloadAfterClick();
    }

    [RelayCommand]
    private void RemoveSelectedMembers()
    {
        if (EventId is null || MemberSelection.Count == 0)
        {
            return;
        }

        var ids = MemberSelection.SelectedIds.ToList();
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (int membershipId in ids)
            {
                EventMembershipResolver.RemoveMember(context, membershipId);
            }
        }

        MemberSelection.Clear();
        RaiseSelectionState();
        Reload();
    }

    [RelayCommand]
    private void SetRoleForSelectedMembers()
    {
        if (EventId is null || MemberSelection.Count == 0 || BulkRole is null)
        {
            return;
        }

        var ids = MemberSelection.SelectedIds.ToList();
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (var m in context.EventMemberships.Where(x => ids.Contains(x.Id)))
            {
                m.Role = BulkRole.Role;
                MemberRoleApplier.MarkUserSet(m);
            }

            context.SaveChanges();
        }

        MemberSelection.Clear();
        RaiseSelectionState();
        Reload();
    }

    // --- Add issues (the search panel under the hero) ---

    public ObservableCollection<IssueSearchResult> SearchResults { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isAddingIssues;

    [ObservableProperty]
    private EventMembershipRole _selectedRole = EventMembershipRole.Core;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedRoleOptionText))]
    private EventMembershipRoleOption _selectedRoleOption = RoleOptions.First(o => o.Role == EventMembershipRole.Core);

    partial void OnSelectedRoleOptionChanged(EventMembershipRoleOption value) => SelectedRole = value.Role;

    public string SelectedRoleOptionText
    {
        get => SelectedRoleOption.Label;
        set
        {
            if (RoleOptions.FirstOrDefault(o => o.Label == value) is { } match)
            {
                SelectedRoleOption = match;
            }
        }
    }

    /// <summary>Live search - typing alone fills the results; <see cref="SearchCommand"/> stays on Enter as a manual re-trigger.</summary>
    partial void OnSearchQueryChanged(string value)
    {
        SearchSelection.Clear(SearchResults);
        Search();
        RaiseSelectionState();
    }

    /// <summary>Opens/closes the Add issues panel; clears the box and results either way.</summary>
    [RelayCommand]
    public void ToggleAddIssues()
    {
        IsAddingIssues = !IsAddingIssues;
        SearchQuery = string.Empty;
        SearchResults.Clear();
        SearchSelection.Clear();
        RaiseSelectionState();
    }

    [RelayCommand]
    private void Search()
    {
        SearchSelection.Clear();
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
                SeriesId = issue.SeriesId,
                DisplayLabel = $"{issue.Series?.Name ?? "Unknown"} #{issue.EffectiveNumber()}",
            });
        }
    }

    [RelayCommand]
    private void AddIssue(IssueSearchResult? result)
    {
        if (result is null || EventId is not int eventId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            EventMembershipResolver.AddMember(context, eventId, result.IssueId, SelectedRole);
        }

        SearchResults.Clear();
        SearchQuery = string.Empty;
        ReloadAfterClick();
    }

    // --- Connect an event ---

    [ObservableProperty]
    private bool _isConnectingEvent;

    [ObservableProperty]
    private string _connectEventQuery = string.Empty;

    [ObservableProperty]
    private RelationType _selectedRelationType = RelationType.Related;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedRelationTypeOptionText))]
    private RelationTypeOption _selectedRelationTypeOption = RelationTypeOptions.First(o => o.Type == RelationType.Related);

    partial void OnSelectedRelationTypeOptionChanged(RelationTypeOption value) => SelectedRelationType = value.Type;

    public string SelectedRelationTypeOptionText
    {
        get => SelectedRelationTypeOption.Label;
        set
        {
            if (RelationTypeOptions.FirstOrDefault(o => o.Label == value) is { } match)
            {
                SelectedRelationTypeOption = match;
            }
        }
    }

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
        if (query.Length == 0 || EventId is not int activeId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var matches = context.StoryEvents.AsNoTracking()
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
        if (result is null || EventId is not int activeId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            EventRelationResolver.TryCreate(context, activeId, result.StoryEventId, SelectedRelationType);
        }

        IsConnectingEvent = false;
        ConnectEventQuery = string.Empty;
        EventSearchResults.Clear();
        Reload();
    }

    [RelayCommand]
    private void RemoveConnectedEvent(ConnectedEventCard? card)
    {
        if (card is null || EventId is null)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            // A relation the smart connector made is also dismissed, so it isn't re-created (continuity-map design §2).
            EventConnectorSweep.RemoveRelation(context, card.EventRelationId);
        }

        ReloadAfterClick();
    }

    /// <summary>A connected event's card opens it - a natural way to walk an event chain.</summary>
    [RelayCommand]
    private void OpenConnectedEvent(ConnectedEventCard? card)
    {
        if (card is not null)
        {
            _nav.OpenEvent(card.OtherEventId);
        }
    }

    [RelayCommand]
    private void OpenFamilyEvent(EventFamilyNodeCard? card)
    {
        if (card is not null)
        {
            _nav.OpenEvent(card.EventId);
        }
    }

    /// <summary>Connect a suggested candidate: a typed suggestion creates exactly what it says, else the picker's relation type.</summary>
    [RelayCommand]
    private void ConnectSuggestedEvent(EventConnectionSuggestionCard? card)
    {
        if (card is null || EventId is not int activeId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            if (card.SuggestedType is RelationType type)
            {
                EventRelationResolver.TryCreate(context, card.SourceEventId, card.TargetEventId, type);
            }
            else
            {
                EventRelationResolver.TryCreate(context, activeId, card.CandidateEventId, SelectedRelationType);
            }
        }

        ReloadAfterClick();
    }

    /// <summary>"Not connected": never suggested or created again. Deferred - the row is in the list this rebuilds.</summary>
    [RelayCommand]
    private void DismissSuggestedConnection(EventConnectionSuggestionCard? card)
    {
        if (card is null || EventId is not int activeId)
        {
            return;
        }

        int other = card.CandidateEventId;
        Dispatcher.UIThread.Post(() =>
        {
            using (var context = PaperbunkrDb.CreateContext())
            {
                EventConnectorSweep.Dismiss(context, activeId, other);
            }

            Reload();
        });
    }

    // --- Issue suggestions (docs/superpowers/specs/2026-08-27-metadata-model-phase4e-format-signal-suggestions-design.md) ---

    private void AddSuggestion(SuggestedIssueRowViewModel row)
    {
        if (EventId is not int eventId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            EventMembershipResolver.AddMember(context, eventId, row.IssueId, row.SelectedRole);
        }

        ReloadAfterClick();
    }

    /// <summary>"Never suggest this issue for this event again" - reversible from the Dismissed list.</summary>
    private void DismissSuggestion(SuggestedIssueRowViewModel row)
    {
        if (EventId is not int eventId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            EventSuggestionResolver.Dismiss(context, eventId, row.IssueId);
        }

        ReloadAfterClick();
    }

    [RelayCommand]
    private void RestoreDismissed(DismissedSuggestionCard? card)
    {
        if (card is null || EventId is not int activeId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            EventSuggestionResolver.Restore(context, activeId, card.IssueId);
        }

        ReloadAfterClick();
    }
}
