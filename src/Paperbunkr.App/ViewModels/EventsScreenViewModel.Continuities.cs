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
        var all = context.Continuities.Include(c => c.Series).OrderBy(c => c.Name).ToList();

        Continuities.Clear();
        foreach (var continuity in all)
        {
            Continuities.Add(new ContinuitySummary(continuity.Id, continuity.Name, continuity.Publisher, continuity.Series.Count, continuity.Id == _activeContinuityId));
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
            OverlappingContinuities.Add(new ContinuityOverlapCard { ContinuityId = other.Id, Name = other.Name, SharedSeriesCount = shared });
        }

        OnPropertyChanged(nameof(HasActiveContinuity));
        OnPropertyChanged(nameof(HasNoOverlappingContinuities));
        OnPropertyChanged(nameof(HasComparison));
        RefreshContinuitiesSidebarKeepingSelection();
    }

    private void RefreshContinuityMembers(PaperbunkrDbContext context, int continuityId)
    {
        // Membership comes from the resolver (the point being exercised: this mode writes through
        // the same ContinuityResolver calls the Related tab does), then the series are re-queried
        // with their issues so the poster cards show real counts/covers.
        var memberIds = ContinuityResolver.GetSeriesInContinuity(context, continuityId).Select(s => s.Id).ToList();
        var withIssues = context.Series.Include(s => s.Issues).Where(s => memberIds.Contains(s.Id)).ToList();

        ContinuityMembers.Clear();
        foreach (var series in withIssues.OrderBy(s => s.Name))
        {
            ContinuityMembers.Add(SeriesCardSample.FromSeries(series));
        }

        OnPropertyChanged(nameof(HasNoContinuityMembers));
    }

    private void RefreshContinuitiesSidebarKeepingSelection()
    {
        using var context = PaperbunkrDb.CreateContext();
        var all = context.Continuities.Include(c => c.Series).OrderBy(c => c.Name).ToList();

        Continuities.Clear();
        foreach (var continuity in all)
        {
            Continuities.Add(new ContinuitySummary(continuity.Id, continuity.Name, continuity.Publisher, continuity.Series.Count, continuity.Id == _activeContinuityId));
        }

        OnPropertyChanged(nameof(HasNoContinuities));
    }

    [RelayCommand]
    private void SelectContinuity(ContinuitySummary? summary)
    {
        if (summary is not null)
        {
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
