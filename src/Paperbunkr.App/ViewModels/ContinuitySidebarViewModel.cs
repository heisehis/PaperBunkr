using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The Continuity screen's sidebar (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md, "Sidebar"): a
/// Continuities | Events switch remembered in <c>AppSettings.ContinuitySidebarTab</c> (Continuities the first time), continuity rows with
/// the publisher logo and issue count, event rows with "start year · issues", and the Suggestions &amp; checks row with its badge.
/// Each list is one grouped query. Selecting and deleting are the shell's; this only holds the rows.
/// </summary>
public partial class ContinuitySidebarViewModel : ViewModelBase
{
    private readonly Func<int?> _activeContinuityId;
    private readonly Func<int?> _activeEventId;
    private readonly Action<int> _deleteContinuity;
    private readonly Action<int> _deleteEvent;
    private bool _loadingTab;

    public ContinuitySidebarViewModel(Func<int?> activeContinuityId, Func<int?> activeEventId, Action<int> deleteContinuity, Action<int> deleteEvent)
    {
        _activeContinuityId = activeContinuityId;
        _activeEventId = activeEventId;
        _deleteContinuity = deleteContinuity;
        _deleteEvent = deleteEvent;
        LoadTab();
    }

    public ObservableCollection<ContinuitySummary> Continuities { get; } = new();

    public ObservableCollection<StoryEventSummary> Events { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContinuitiesTab), nameof(IsEventsTab))]
    private ContinuitySidebarTab _tab = ContinuitySidebarTab.Continuities;

    public bool IsContinuitiesTab => Tab == ContinuitySidebarTab.Continuities;

    public bool IsEventsTab => Tab == ContinuitySidebarTab.Events;

    public bool HasNoContinuities => Continuities.Count == 0;

    public bool HasNoEvents => Events.Count == 0;

    public string ContinuitiesCountLabel => Continuities.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    public string EventsCountLabel => Events.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Everything waiting in Suggestions &amp; checks: story-event suggestions, possible duplicates, shared-universe suggestions.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestions), nameof(SuggestionsCountLabel))]
    private int _suggestionsCount;

    public bool HasSuggestions => SuggestionsCount > 0;

    public string SuggestionsCountLabel => SuggestionsCount.ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>True while the Suggestions &amp; checks panel is what the main area shows.</summary>
    [ObservableProperty]
    private bool _isSuggestionsActive;

    [RelayCommand]
    private void ShowContinuitiesTab() => Tab = ContinuitySidebarTab.Continuities;

    [RelayCommand]
    private void ShowEventsTab() => Tab = ContinuitySidebarTab.Events;

    /// <summary>Every switch is remembered - by hand, or because the user opened an event from a continuity page (or the reverse).</summary>
    partial void OnTabChanged(ContinuitySidebarTab value)
    {
        if (_loadingTab)
        {
            return;
        }

        try
        {
            using var context = PaperbunkrDb.CreateContext();
            context.GetOrCreateAppSettings().ContinuitySidebarTab = value.ToString();
            context.SaveChanges();
        }
        catch (Exception)
        {
            // Remembering the tab is a nicety; the switch itself already happened.
        }
    }

    private void LoadTab()
    {
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            if (Enum.TryParse<ContinuitySidebarTab>(context.GetOrCreateAppSettings().ContinuitySidebarTab, out var remembered)
                && Enum.IsDefined(remembered))
            {
                _loadingTab = true;
                Tab = remembered;
            }
        }
        catch (Exception)
        {
            // No settings row yet (or no database): the first-time default stands.
        }
        finally
        {
            _loadingTab = false;
        }
    }

    public void RefreshContinuities()
    {
        using var context = PaperbunkrDb.CreateContext();
        var rows = context.Continuities
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Publisher,
                SeriesCount = c.Memberships.Count,
                IssueCount = c.Memberships.SelectMany(m => m.Series.Issues).Count(i => !i.IsPlaceholder),
            })
            .AsEnumerable()
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        int? active = _activeContinuityId();
        Continuities.Clear();
        foreach (var row in rows)
        {
            int id = row.Id;
            Continuities.Add(new ContinuitySummary(row.Id, row.Name, row.Publisher, row.SeriesCount, row.Id == active)
            {
                IssueCount = row.IssueCount,
                DeleteConfirm = new TwoStepConfirm(() => _deleteContinuity(id), idleLabel: "Delete", armedLabel: "Confirm delete?"),
            });
        }

        OnPropertyChanged(nameof(HasNoContinuities));
        OnPropertyChanged(nameof(ContinuitiesCountLabel));
    }

    public void RefreshEvents()
    {
        using var context = PaperbunkrDb.CreateContext();
        var rows = context.StoryEvents
            .Select(e => new
            {
                e.Id,
                e.Name,
                Count = e.Members.Count,
                e.StartDate,
                FirstYear = e.Members.Min(m => m.Issue!.Year),
            })
            .AsEnumerable()
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        int? active = _activeEventId();
        Events.Clear();
        foreach (var row in rows)
        {
            int id = row.Id;
            Events.Add(new StoryEventSummary
            {
                Id = row.Id,
                Name = row.Name,
                MemberCount = row.Count,
                StartYear = row.StartDate?.Year ?? row.FirstYear,
                IsActive = row.Id == active,
                DeleteConfirm = new TwoStepConfirm(() => _deleteEvent(id), idleLabel: "Delete", armedLabel: "Confirm delete?"),
            });
        }

        OnPropertyChanged(nameof(HasNoEvents));
        OnPropertyChanged(nameof(EventsCountLabel));
    }
}
