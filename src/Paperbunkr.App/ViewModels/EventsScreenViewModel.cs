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

    public EventsScreenViewModel()
    {
        Events = new ObservableCollection<StoryEventSummary>();
        Members = new ObservableCollection<EventMemberRowViewModel>();
        SearchResults = new ObservableCollection<IssueSearchResult>();
        RefreshSidebar();
    }

    public ObservableCollection<StoryEventSummary> Events { get; }
    public ObservableCollection<EventMemberRowViewModel> Members { get; }
    public ObservableCollection<IssueSearchResult> SearchResults { get; }

    public static EventMembershipRoleOption[] RoleOptions => EventMembershipRoleOption.All;

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

    public bool HasNoEvents => Events.Count == 0;

    public bool HasNoMembers => !HasNoEvents && Members.Count == 0;

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

        OnPropertyChanged(nameof(HasNoMembers));
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
}
