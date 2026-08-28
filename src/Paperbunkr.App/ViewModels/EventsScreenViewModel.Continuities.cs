using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Continuities mode of the Story Events screen (docs/superpowers/specs/2026-08-27-metadata-model-
/// phase4f-continuity-browse-design.md) - a browse/edit shelf over <c>Continuity</c> data Phase 4a
/// already modeled. Writes through the same <see cref="ContinuityResolver"/> calls the Related-tab
/// continuity UI uses, not a parallel path. No new <c>Continuity</c> schema this phase.
/// </summary>
public partial class EventsScreenViewModel
{
    private int? _activeContinuityId;

    public ObservableCollection<ContinuitySummary> Continuities { get; }

    /// <summary>The active continuity's member series, as a poster grid. Clicking a card navigates to that series' Detail screen.</summary>
    public ObservableCollection<SeriesCardSample> ContinuityMembers { get; }

    public ObservableCollection<SeriesSearchResult> ContinuitySeriesSearchResults { get; }

    /// <summary>Other continuities sharing at least one series with the active one - the cross-continuity compare picker (docs/superpowers/specs/2026-08-27-metadata-model-phase4f-continuity-browse-design.md).</summary>
    public ObservableCollection<ContinuityOverlapCard> OverlappingContinuities { get; }

    /// <summary>Series in both the active continuity and <see cref="_compareContinuityId"/>.</summary>
    public ObservableCollection<SeriesCardSample> SharedContinuitySeries { get; }

    private int? _compareContinuityId;

    [ObservableProperty]
    private bool _isComparingContinuities;

    [ObservableProperty]
    private string _compareContinuityName = string.Empty;

    public bool HasNoOverlappingContinuities => OverlappingContinuities.Count == 0;

    public bool HasComparison => _compareContinuityId is not null;

    [ObservableProperty]
    private string _continuityName = string.Empty;

    [ObservableProperty]
    private string _continuityDescription = string.Empty;

    [ObservableProperty]
    private string _continuityPublisher = string.Empty;

    [ObservableProperty]
    private string _newContinuityName = string.Empty;

    [ObservableProperty]
    private bool _isAddingContinuitySeries;

    [ObservableProperty]
    private string _continuitySeriesSearchQuery = string.Empty;

    public bool HasNoContinuities => Continuities.Count == 0;

    public bool HasActiveContinuity => _activeContinuityId is not null;

    public bool HasNoContinuityMembers => HasActiveContinuity && ContinuityMembers.Count == 0;

    public void RefreshContinuitiesSidebar()
    {
        using var context = PaperbunkrDb.CreateContext();
        var all = context.Continuities.Include(c => c.Memberships).OrderBy(c => c.Name).ToList();

        Continuities.Clear();
        foreach (var continuity in all)
        {
            int cid = continuity.Id;
            Continuities.Add(new ContinuitySummary(continuity.Id, continuity.Name, continuity.Publisher, continuity.Memberships.Count, continuity.Id == _activeContinuityId)
            {
                DeleteConfirm = new TwoStepConfirm(() => DeleteContinuity(cid), idleLabel: "Delete", armedLabel: "Confirm delete?"),
            });
        }

        OnPropertyChanged(nameof(HasNoContinuities));

        // On first switch into this mode with nothing selected yet, open the first continuity.
        if (_activeContinuityId is null && all.Count > 0)
        {
            LoadContinuity(all[0].Id);
        }
    }

    public void LoadContinuity(int continuityId)
    {
        _activeContinuityId = continuityId;
        _activeEventId = null;
        NotifySelectionChanged();

        using var context = PaperbunkrDb.CreateContext();
        var continuity = context.Continuities.FirstOrDefault(c => c.Id == continuityId);
        if (continuity is null)
        {
            return;
        }

        ContinuityName = continuity.Name;
        ContinuityDescription = continuity.Description ?? string.Empty;
        ContinuityPublisher = continuity.Publisher ?? string.Empty;

        RefreshContinuityMembers(context, continuityId);
        ContinuityMemberSelection.Clear();
        ContinuitySeriesSelection.Clear();
        RaiseContinuitySelectionState();

        IsAddingContinuitySeries = false;
        ContinuitySeriesSearchQuery = string.Empty;
        ContinuitySeriesSearchResults.Clear();

        // Cross-continuity compare resets when the active continuity changes.
        _compareContinuityId = null;
        IsComparingContinuities = false;
        CompareContinuityName = string.Empty;
        SharedContinuitySeries.Clear();
        OverlappingContinuities.Clear();
        foreach (var (other, shared) in ContinuityResolver.GetOverlappingContinuities(context, continuityId))
        {
            int otherId = other.Id;
            string otherName = other.Name;
            OverlappingContinuities.Add(new ContinuityOverlapCard
            {
                ContinuityId = other.Id,
                Name = other.Name,
                SharedSeriesCount = shared,
                MergeConfirm = new TwoStepConfirm(() => MergeActiveContinuityInto(otherId, otherName), idleLabel: "Merge into this", armedLabel: "Confirm merge?"),
            });
        }

        OnPropertyChanged(nameof(HasActiveContinuity));
        OnPropertyChanged(nameof(HasNoOverlappingContinuities));
        OnPropertyChanged(nameof(HasComparison));
        RefreshContinuitiesSidebarKeepingSelection();
    }

    private void RefreshContinuityMembers(PaperbunkrDbContext context, int continuityId)
    {
        // Membership comes from the resolver (the point being exercised: this mode writes through
        // the same ContinuityResolver calls the Related tab does), in SortOrder order, then the
        // series are re-queried with their issues so the poster cards show real counts/covers. Each
        // card also carries its membership Note (docs/superpowers/specs/2026-08-28-continuity-
        // editing-design.md, Part D).
        var memberships = ContinuityResolver.GetMemberships(context, continuityId);
        var seriesIds = memberships.Select(m => m.SeriesId).ToList();
        var withIssues = context.Series.Include(s => s.Issues).Where(s => seriesIds.Contains(s.Id)).ToDictionary(s => s.Id);

        ContinuityMembers.Clear();
        foreach (var membership in memberships)
        {
            if (!withIssues.TryGetValue(membership.SeriesId, out var series))
            {
                continue;
            }

            var card = SeriesCardSample.FromSeries(series);
            card.MembershipNote = membership.Note;
            ContinuityMembers.Add(card);
        }

        OnPropertyChanged(nameof(HasNoContinuityMembers));
    }

    private void RefreshContinuitiesSidebarKeepingSelection()
    {
        using var context = PaperbunkrDb.CreateContext();
        var all = context.Continuities.Include(c => c.Memberships).OrderBy(c => c.Name).ToList();

        Continuities.Clear();
        foreach (var continuity in all)
        {
            int cid = continuity.Id;
            Continuities.Add(new ContinuitySummary(continuity.Id, continuity.Name, continuity.Publisher, continuity.Memberships.Count, continuity.Id == _activeContinuityId)
            {
                DeleteConfirm = new TwoStepConfirm(() => DeleteContinuity(cid), idleLabel: "Delete", armedLabel: "Confirm delete?"),
            });
        }

        OnPropertyChanged(nameof(HasNoContinuities));
    }

    [RelayCommand]
    private void SelectContinuity(ContinuitySummary? summary)
    {
        if (summary is not null)
        {
            DetailView = EventsDetailView.Primary;
            LoadContinuity(summary.Id);
        }
    }

    /// <summary>
    /// A second entry point into the same creation path Phase 4a's combo-box-with-create exposes on
    /// the Related tab - <see cref="ContinuityResolver.GetOrCreate"/>, with its case-insensitive
    /// dedup guardrail. Not a new creation mechanism.
    /// </summary>
    [RelayCommand]
    private void CreateNewContinuity()
    {
        string name = string.IsNullOrWhiteSpace(NewContinuityName) ? "New Continuity" : NewContinuityName.Trim();

        using var context = PaperbunkrDb.CreateContext();
        var continuity = ContinuityResolver.GetOrCreate(context, name);

        NewContinuityName = string.Empty;
        RefreshContinuitiesSidebar();
        LoadContinuity(continuity.Id);
    }

    [RelayCommand]
    private void ToggleAddContinuitySeries()
    {
        IsAddingContinuitySeries = !IsAddingContinuitySeries;
        ContinuitySeriesSearchQuery = string.Empty;
        ContinuitySeriesSearchResults.Clear();
    }

    partial void OnContinuitySeriesSearchQueryChanged(string value) => SearchContinuitySeries();

    [RelayCommand]
    private void SearchContinuitySeries()
    {
        ContinuitySeriesSearchResults.Clear();
        string query = ContinuitySeriesSearchQuery.Trim();
        if (query.Length == 0)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var matches = context.Series
            .AsEnumerable()
            .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(20);

        foreach (var series in matches)
        {
            ContinuitySeriesSearchResults.Add(new SeriesSearchResult { SeriesId = series.Id, Name = series.Name });
        }
    }

    [RelayCommand]
    private void AddSeriesToActiveContinuity(SeriesSearchResult? result)
    {
        if (result is null || _activeContinuityId is not int continuityId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        ContinuityResolver.AddSeriesToContinuity(context, result.SeriesId, continuityId);

        IsAddingContinuitySeries = false;
        ContinuitySeriesSearchQuery = string.Empty;
        ContinuitySeriesSearchResults.Clear();
        LoadContinuity(continuityId);
    }

    [RelayCommand]
    private void RemoveSeriesFromActiveContinuity(SeriesCardSample? card)
    {
        if (card is null || _activeContinuityId is not int continuityId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        ContinuityResolver.RemoveSeriesFromContinuity(context, card.SeriesId, continuityId);
        LoadContinuity(continuityId);
    }

    // --- Per-series note + reorder (docs/superpowers/specs/2026-08-28-continuity-editing-design.md, Part D) ---

    /// <summary>Opens the inline note editor on one member card.</summary>
    [RelayCommand]
    private void BeginEditContinuitySeriesNote(SeriesCardSample? card)
    {
        if (card is null)
        {
            return;
        }

        foreach (var other in ContinuityMembers)
        {
            other.IsEditingNote = other == card;
        }
    }

    /// <summary>Persists the edited note (blank clears it) and closes the editor.</summary>
    [RelayCommand]
    private void SetContinuitySeriesNote(SeriesCardSample? card)
    {
        if (card is null || _activeContinuityId is not int continuityId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            ContinuityResolver.SetMembershipNote(context, continuityId, card.SeriesId, card.MembershipNote);
        }

        card.MembershipNote = string.IsNullOrWhiteSpace(card.MembershipNote) ? null : card.MembershipNote.Trim();
        card.IsEditingNote = false;
    }

    /// <summary>Moves a member one slot earlier in the continuity's order.</summary>
    [RelayCommand]
    private void MoveContinuitySeriesEarlier(SeriesCardSample? card) => MoveContinuitySeries(card, -1);

    /// <summary>Moves a member one slot later in the continuity's order.</summary>
    [RelayCommand]
    private void MoveContinuitySeriesLater(SeriesCardSample? card) => MoveContinuitySeries(card, 1);

    private void MoveContinuitySeries(SeriesCardSample? card, int offset)
    {
        if (card is null || _activeContinuityId is not int continuityId)
        {
            return;
        }

        int index = ContinuityMembers.IndexOf(card);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= ContinuityMembers.Count)
        {
            return;
        }

        var ordered = ContinuityMembers.Select(c => c.SeriesId).ToList();
        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);

        using (var context = PaperbunkrDb.CreateContext())
        {
            ContinuityResolver.SetMembershipOrder(context, continuityId, ordered);
        }

        LoadContinuity(continuityId);
    }

    // --- Continuity editing: delete + merge (docs/superpowers/specs/2026-08-28-continuity-editing-design.md) ---

    /// <summary>
    /// Deletes a whole continuity. Its series are only <em>unlinked</em> - the M:M join rows go,
    /// the <see cref="Paperbunkr.Data.Entities.Series"/> themselves stay. Mirrors
    /// <see cref="DeleteEvent"/>.
    /// </summary>
    private void DeleteContinuity(int continuityId)
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            var continuity = context.Continuities.FirstOrDefault(c => c.Id == continuityId);
            if (continuity is null)
            {
                return;
            }

            // Cascade delete on ContinuityMembership.ContinuityId drops the join rows; the member
            // Series themselves are untouched.
            context.Continuities.Remove(continuity);
            context.SaveChanges();
        }

        if (_activeContinuityId == continuityId)
        {
            _activeContinuityId = null;
            NotifySelectionChanged();
            ContinuityName = string.Empty;
            ContinuityDescription = string.Empty;
            ContinuityPublisher = string.Empty;
            ContinuityMembers.Clear();
            OverlappingContinuities.Clear();
            SharedContinuitySeries.Clear();
            OnPropertyChanged(nameof(HasActiveContinuity));
            OnPropertyChanged(nameof(HasNoContinuityMembers));
        }

        RefreshContinuitiesSidebar();

        // Fall back to the first remaining continuity, matching the sidebar's first-load behaviour.
        if (_activeContinuityId is null && Continuities.Count > 0)
        {
            LoadContinuity(Continuities[0].Id);
        }
    }

    /// <summary>"Delete continuity" from the ⋯ Manage menu - arms the active row's own two-step confirm.</summary>
    [RelayCommand]
    private void DeleteActiveContinuity()
    {
        if (_activeContinuityId is int id)
        {
            DeleteContinuity(id);
        }
    }

    /// <summary>"Merge into this" on an overlap card - folds the active continuity's series into the
    /// target continuity, then deletes the active one and opens the target.</summary>
    private void MergeActiveContinuityInto(int targetId, string targetName)
    {
        if (_activeContinuityId is not int sourceId || targetId == sourceId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            ContinuityResolver.Merge(context, sourceId, targetId);
        }

        _activeContinuityId = null;
        RefreshContinuitiesSidebar();
        LoadContinuity(targetId);
        _notify("Continuities merged", $"Series folded into \"{targetName}\".");
    }

    // --- Bulk selection: continuity surfaces (docs/superpowers/specs/2026-08-28-bulk-selection-lists-continuities-events-design.md) ---

    public TileSelectionController<SeriesSearchResult> ContinuitySeriesSelection { get; } = new();
    public TileSelectionController<SeriesCardSample> ContinuityMemberSelection { get; } = new();

    public bool AnyContinuitySeriesSelected => ContinuitySeriesSelection.Count > 0;
    public bool AnyContinuityMembersSelected => ContinuityMemberSelection.Count > 0;
    public string ContinuitySeriesSelectionSummary => $"{ContinuitySeriesSelection.Count} selected";
    public string ContinuityMemberSelectionSummary => $"{ContinuityMemberSelection.Count} selected";

    private void RaiseContinuitySelectionState()
    {
        OnPropertyChanged(nameof(AnyContinuitySeriesSelected));
        OnPropertyChanged(nameof(AnyContinuityMembersSelected));
        OnPropertyChanged(nameof(ContinuitySeriesSelectionSummary));
        OnPropertyChanged(nameof(ContinuityMemberSelectionSummary));
    }

    [RelayCommand]
    private void ToggleContinuitySeriesSelection(SeriesSearchResult? r)
    {
        if (r is null) return;
        ContinuitySeriesSelection.Toggle(ContinuitySeriesSearchResults, r, isShiftHeld: false);
        RaiseContinuitySelectionState();
    }

    [RelayCommand]
    private void ToggleContinuityMemberSelection(SeriesCardSample? card)
    {
        if (card is null) return;
        ContinuityMemberSelection.Toggle(ContinuityMembers, card, isShiftHeld: false);
        RaiseContinuitySelectionState();
    }

    [RelayCommand]
    private void ClearContinuitySeriesSelection()
    {
        ContinuitySeriesSelection.Clear(ContinuitySeriesSearchResults);
        RaiseContinuitySelectionState();
    }

    [RelayCommand]
    private void ClearContinuityMemberSelection()
    {
        ContinuityMemberSelection.Clear(ContinuityMembers);
        RaiseContinuitySelectionState();
    }

    [RelayCommand]
    private void AddSelectedSeries()
    {
        if (_activeContinuityId is not int continuityId || ContinuitySeriesSelection.Count == 0)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (var r in ContinuitySeriesSearchResults.Where(x => ContinuitySeriesSelection.SelectedIds.Contains(x.Id)))
            {
                ContinuityResolver.AddSeriesToContinuity(context, r.SeriesId, continuityId);
            }
        }

        ContinuitySeriesSelection.Clear();
        RaiseContinuitySelectionState();
        LoadContinuity(continuityId);
    }

    [RelayCommand]
    private void RemoveSelectedSeries()
    {
        if (_activeContinuityId is not int continuityId || ContinuityMemberSelection.Count == 0)
        {
            return;
        }

        var ids = ContinuityMemberSelection.SelectedIds.ToList();
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (int seriesId in ids)
            {
                ContinuityResolver.RemoveSeriesFromContinuity(context, seriesId, continuityId);
            }
        }

        ContinuityMemberSelection.Clear();
        RaiseContinuitySelectionState();
        LoadContinuity(continuityId);
    }

    /// <summary>Clicking a member series card navigates to that series' Detail screen.</summary>
    [RelayCommand]
    private void OpenContinuitySeries(SeriesCardSample? card)
    {
        if (card is not null)
        {
            _goToSeriesDetail(card.SeriesId);
        }
    }

    // --- Cross-continuity comparison (docs/superpowers/specs/2026-08-27-metadata-model-phase4f-continuity-browse-design.md) ---

    [RelayCommand]
    private void ToggleCompareContinuities()
    {
        IsComparingContinuities = !IsComparingContinuities;
        if (!IsComparingContinuities)
        {
            _compareContinuityId = null;
            CompareContinuityName = string.Empty;
            SharedContinuitySeries.Clear();
            OnPropertyChanged(nameof(HasComparison));
        }
    }

    [RelayCommand]
    private void CompareWithContinuity(ContinuityOverlapCard? card)
    {
        if (card is null || _activeContinuityId is not int activeId)
        {
            return;
        }

        _compareContinuityId = card.ContinuityId;
        CompareContinuityName = card.Name;

        using var context = PaperbunkrDb.CreateContext();
        SharedContinuitySeries.Clear();
        foreach (var series in ContinuityResolver.GetSeriesInBothContinuities(context, activeId, card.ContinuityId))
        {
            SharedContinuitySeries.Add(SeriesCardSample.FromSeries(series));
        }

        OnPropertyChanged(nameof(HasComparison));
    }

    // --- Create a reading list from this continuity (docs/superpowers/specs/2026-08-27-metadata-model-phase4f-continuity-browse-design.md) ---

    [RelayCommand]
    private void CreateReadingListFromContinuity()
    {
        if (_activeContinuityId is not int continuityId)
        {
            return;
        }

        int listId;
        string name;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var list = ContinuityReadingListBuilder.CreateFromContinuity(context, continuityId);
            listId = list.Id;
            name = list.Name;
        }

        _notify("Reading list created", $"\"{name}\" — {ContinuityMembers.Count} series in publication order.");
        _goToReadingList(listId);
    }
}
