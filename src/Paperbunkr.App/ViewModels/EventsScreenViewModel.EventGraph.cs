using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Events-mode extras: the transitive event-relation graph (docs/superpowers/specs/2026-08-27-
/// metadata-model-phase4d-event-relations-design.md), auto-suggested event connections
/// (extends [[phase4e]]'s issue-suggestion idea to event-to-event relations), and the persisted
/// dismissed-suggestion list ([[phase4e]]).
/// </summary>
public partial class EventsScreenViewModel
{
    /// <summary>The transitive connected component of events reachable from the active event, depth-ordered.</summary>
    public ObservableCollection<EventFamilyNodeCard> EventFamily { get; }

    public ObservableCollection<EventConnectionSuggestionCard> EventConnectionSuggestions { get; }

    public ObservableCollection<DismissedSuggestionCard> DismissedSuggestions { get; }

    [ObservableProperty]
    private bool _eventFamilyExpanded;

    [ObservableProperty]
    private bool _dismissedExpanded;

    /// <summary>True only when the graph has more than the active event + its direct connections (i.e. a real chain).</summary>
    public bool HasEventChain => EventFamily.Count > ConnectedEvents.Count + 1;

    public bool HasNoConnectionSuggestions => EventConnectionSuggestions.Count == 0;

    public bool HasNoDismissed => DismissedSuggestions.Count == 0;

    private void RefreshEventFamily(PaperbunkrDbContext context, int storyEventId)
    {
        EventFamily.Clear();
        foreach (var (evt, depth) in EventRelationResolver.GetEventFamily(context, storyEventId))
        {
            EventFamily.Add(new EventFamilyNodeCard { EventId = evt.Id, Name = evt.Name, Depth = depth });
        }

        OnPropertyChanged(nameof(HasEventChain));
    }

    private void RefreshEventConnectionSuggestions(PaperbunkrDbContext context, int storyEventId)
    {
        EventConnectionSuggestions.Clear();
        foreach (var suggestion in EventRelationSuggestionResolver.GetSuggestions(context, storyEventId))
        {
            EventConnectionSuggestions.Add(new EventConnectionSuggestionCard
            {
                CandidateEventId = suggestion.Candidate.Id,
                Name = suggestion.Candidate.Name,
                Reason = suggestion.Reason,
            });
        }

        OnPropertyChanged(nameof(HasNoConnectionSuggestions));
    }

    private void RefreshDismissedSuggestions(PaperbunkrDbContext context, int storyEventId)
    {
        DismissedSuggestions.Clear();
        foreach (var issue in EventSuggestionResolver.GetDismissed(context, storyEventId))
        {
            DismissedSuggestions.Add(new DismissedSuggestionCard
            {
                IssueId = issue.Id,
                Label = $"{issue.Series?.Name ?? "Unknown"} #{issue.EffectiveNumber()}",
            });
        }

        OnPropertyChanged(nameof(HasNoDismissed));
    }

    [RelayCommand]
    private void ToggleEventFamily() => EventFamilyExpanded = !EventFamilyExpanded;

    [RelayCommand]
    private void ToggleDismissed() => DismissedExpanded = !DismissedExpanded;

    /// <summary>Jump to any event in the transitive graph.</summary>
    [RelayCommand]
    private void OpenFamilyEvent(EventFamilyNodeCard? card)
    {
        if (card is not null)
        {
            LoadEvent(card.EventId);
        }
    }

    /// <summary>Connect the active event to a suggested candidate using the currently-selected relation type.</summary>
    [RelayCommand]
    private void ConnectSuggestedEvent(EventConnectionSuggestionCard? card)
    {
        if (card is null || _activeEventId is not int activeId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        EventRelationResolver.TryCreate(context, activeId, card.CandidateEventId, SelectedEventRelationType);
        LoadEvent(activeId);
    }

    /// <summary>Un-dismiss an issue so it can be suggested for this event again.</summary>
    [RelayCommand]
    private void RestoreDismissed(DismissedSuggestionCard? card)
    {
        if (card is null || _activeEventId is not int activeId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        EventSuggestionResolver.Restore(context, activeId, card.IssueId);
        LoadEvent(activeId);
    }
}
