using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Issues/Related/Details/Activity tab strip, ported from DetailTabs.dc.html (Claude Design
/// project 43c40b25). Populated from the real <see cref="Series"/> passed to
/// <see cref="LoadSeries"/> rather than the wireframe's static sample data.
/// </summary>
public partial class DetailTabsViewModel : ViewModelBase
{
    private readonly Action<int> _goToProperties;
    private readonly Action<IReadOnlyList<int>> _goToBulkProperties;
    private readonly Action? _onSelectionChanged;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private int? _seriesId;
    private int? _lastToggledIndex;

    public DetailTabsViewModel(Action<int> goToProperties, Action<IReadOnlyList<int>> goToBulkProperties, Action? onSelectionChanged = null)
        : this(goToProperties, goToBulkProperties, onSelectionChanged, PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal DetailTabsViewModel(Action<int> goToProperties, Action<IReadOnlyList<int>> goToBulkProperties, Action? onSelectionChanged, Func<PaperbunkrDbContext> contextFactory)
    {
        _goToProperties = goToProperties;
        _goToBulkProperties = goToBulkProperties;
        _onSelectionChanged = onSelectionChanged;
        _contextFactory = contextFactory;
        Issues = new ObservableCollection<IssueCardSample>();
        Related = new ObservableCollection<RelatedSeriesSample>();
        SameContinuity = new ObservableCollection<RelatedGroupSeriesSample>();
        ContinuityChips = new ObservableCollection<ContinuityChip>();
        SameEvent = new ObservableCollection<RelatedGroupSeriesSample>();
    }

    /// <summary>Multi-select state for the Issues tab (docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md §1).</summary>
    public HashSet<int> SelectedIssueIds { get; } = new();

    public ObservableCollection<IssueCardSample> Issues { get; }

    /// <summary>
    /// Real data as of docs/superpowers/specs/2026-08-17-metadata-model-phase3-media-relations-
    /// design.md - populated by <see cref="LoadSeries"/> via <see cref="MediaRelationResolver.GetRelatedSeries"/>.
    /// </summary>
    public ObservableCollection<RelatedSeriesSample> Related { get; }

    public bool HasRelated => Related.Count > 0;

    /// <summary>
    /// Real data as of docs/superpowers/specs/2026-08-17-metadata-model-phase4a-continuity-
    /// design.md - other series sharing at least one <see cref="Data.Entities.Continuity"/> with
    /// the loaded series, populated by <see cref="LoadSeries"/> via
    /// <see cref="ContinuityResolver.GetOtherSeriesSharingContinuity"/>. Additive alongside
    /// <see cref="Related"/>, not a replacement for it - Phase 3's flat relation carousel is
    /// unchanged.
    /// </summary>
    public ObservableCollection<RelatedGroupSeriesSample> SameContinuity { get; }

    public bool HasSameContinuity => SameContinuity.Count > 0;

    /// <summary>The loaded series' own <see cref="Data.Entities.Continuity"/> memberships, shown as removable chips.</summary>
    public ObservableCollection<ContinuityChip> ContinuityChips { get; }

    /// <summary>
    /// Real data as of docs/superpowers/specs/2026-08-17-metadata-model-phase4b-story-events-
    /// design.md - other series sharing at least one <see cref="Data.Entities.StoryEvent"/> with
    /// the loaded series, populated by <see cref="LoadSeries"/> via
    /// <see cref="EventMembershipResolver.GetOtherSeriesInSharedEvents"/>. Additive alongside
    /// <see cref="Related"/>/<see cref="SameContinuity"/>.
    /// </summary>
    public ObservableCollection<RelatedGroupSeriesSample> SameEvent { get; }

    public bool HasSameEvent => SameEvent.Count > 0;

    public ObservableCollection<SeriesSearchResult> RelationSearchResults { get; } = new();

    public string Publisher { get; private set; } = "Unknown";
    public string ReadingModeLabel { get; private set; } = "Left to Right";

    public void LoadSeries(Series series)
    {
        var coverBrush = SeriesCardSample.CoverBrushFor(series.Name);

        Issues.Clear();
        SelectedIssueIds.Clear();
        _lastToggledIndex = null;
        foreach (var issue in series.Issues.OrderByNumber())
        {
            Issues.Add(new IssueCardSample
            {
                Id = issue.Id,
                SeriesId = series.Id,
                Title = string.IsNullOrWhiteSpace(issue.EffectiveNumber()) ? "#?" : $"#{issue.EffectiveNumber()}",
                IsUnread = issue.LastPageRead is null or 0,
                CoverBrush = coverBrush,
                CoverImage = CoverImageCache.Get(issue.Id),
                FilePath = issue.FilePath,
            });
        }

        _seriesId = series.Id;
        Publisher = string.IsNullOrWhiteSpace(series.Publisher) ? "Unknown" : series.Publisher;
        SetReadingModeLabel(series.ReadingMode);
        OnPropertyChanged(nameof(Publisher));

        using (var context = _contextFactory())
        {
            RefreshRelated(context, series.Id);
            RefreshContinuity(context, series.Id);
            RefreshSameEvent(context, series.Id);
        }

        ActiveTab = "issues";
        _onSelectionChanged?.Invoke();
    }

    private void RefreshRelated(PaperbunkrDbContext context, int seriesId)
    {
        Related.Clear();
        foreach (var (otherSeries, displayType, mediaRelationId) in MediaRelationResolver.GetRelatedSeries(context, seriesId))
        {
            Related.Add(new RelatedSeriesSample
            {
                Title = otherSeries.Name,
                Name = otherSeries.Name,
                Note = RelationTypeOption.FormatLabel(displayType),
                CoverBrush = SeriesCardSample.CoverBrushFor(otherSeries.Name),
                RelatedSeriesId = otherSeries.Id,
                MediaRelationId = mediaRelationId,
            });
        }

        OnPropertyChanged(nameof(HasRelated));
    }

    // --- Related tab: add/remove a MediaRelation (docs/superpowers/specs/2026-08-17-metadata-
    // model-phase3-media-relations-design.md) ---

    public static IReadOnlyList<RelationTypeOption> RelationTypeOptions => RelationTypeOption.All;

    [ObservableProperty]
    private bool _isAddingRelation;

    [ObservableProperty]
    private string _relationSearchQuery = string.Empty;

    [ObservableProperty]
    private RelationType _selectedRelationType = RelationType.Related;

    /// <summary>
    /// Bound to the ComboBox's <c>SelectedItem</c> instead of <c>SelectedValue</c>/
    /// <c>SelectedValueBinding</c> - the latter resolves its binding path against this
    /// ViewModel's own DataContext, not the <c>ItemsSource</c> element type, so `{Binding Type}`
    /// there was silently unresolvable (a real, permanent XAML bug since Phase 3, not a build-
    /// tooling artifact - see docs/superpowers/specs/2026-08-18-selectedvaluebinding-xaml-fix-
    /// design.md).
    /// </summary>
    [ObservableProperty]
    private RelationTypeOption _selectedRelationTypeOption = RelationTypeOptions.First(o => o.Type == RelationType.Related);

    partial void OnSelectedRelationTypeOptionChanged(RelationTypeOption value) => SelectedRelationType = value.Type;

    [RelayCommand]
    private void ToggleAddRelation()
    {
        IsAddingRelation = !IsAddingRelation;
        RelationSearchQuery = string.Empty;
        RelationSearchResults.Clear();
    }

    partial void OnRelationSearchQueryChanged(string value) => SearchRelationCandidates();

    [RelayCommand]
    private void SearchRelationCandidates()
    {
        RelationSearchResults.Clear();
        if (string.IsNullOrWhiteSpace(RelationSearchQuery) || _seriesId is not int currentSeriesId)
        {
            return;
        }

        using var context = _contextFactory();
        var matches = context.Series
            .Where(s => s.Id != currentSeriesId)
            .AsEnumerable()
            .Where(s => s.Name.Contains(RelationSearchQuery, StringComparison.OrdinalIgnoreCase))
            .Take(20);

        foreach (var series in matches)
        {
            RelationSearchResults.Add(new SeriesSearchResult { SeriesId = series.Id, Name = series.Name });
        }
    }

    [RelayCommand]
    private void AddRelation(SeriesSearchResult? target)
    {
        if (target is null || _seriesId is not int currentSeriesId)
        {
            return;
        }

        using var context = _contextFactory();
        MediaRelationResolver.TryCreate(context, currentSeriesId, target.SeriesId, SelectedRelationType);

        IsAddingRelation = false;
        RelationSearchQuery = string.Empty;
        RelationSearchResults.Clear();
        RefreshRelated(context, currentSeriesId);
    }

    [RelayCommand]
    private void RemoveRelation(RelatedSeriesSample? sample)
    {
        if (sample is null || _seriesId is not int currentSeriesId)
        {
            return;
        }

        using var context = _contextFactory();
        MediaRelationResolver.Remove(context, sample.MediaRelationId);
        RefreshRelated(context, currentSeriesId);
    }

    // --- Related tab: Continuity membership (docs/superpowers/specs/2026-08-17-metadata-model-
    // phase4a-continuity-design.md) ---

    private void RefreshContinuity(PaperbunkrDbContext context, int seriesId)
    {
        ContinuityChips.Clear();
        foreach (var continuity in ContinuityResolver.GetContinuities(context, seriesId))
        {
            ContinuityChips.Add(new ContinuityChip { ContinuityId = continuity.Id, Name = continuity.Name });
        }

        SameContinuity.Clear();
        foreach (var otherSeries in ContinuityResolver.GetOtherSeriesSharingContinuity(context, seriesId))
        {
            SameContinuity.Add(new RelatedGroupSeriesSample
            {
                Title = otherSeries.Name,
                Name = otherSeries.Name,
                Note = "Same continuity",
                CoverBrush = SeriesCardSample.CoverBrushFor(otherSeries.Name),
                SeriesId = otherSeries.Id,
            });
        }

        OnPropertyChanged(nameof(HasSameContinuity));
    }

    [ObservableProperty]
    private bool _isAddingContinuity;

    [ObservableProperty]
    private string _continuitySearchQuery = string.Empty;

    public ObservableCollection<ContinuitySearchResult> ContinuitySearchResults { get; } = new();

    [RelayCommand]
    private void ToggleAddContinuity()
    {
        IsAddingContinuity = !IsAddingContinuity;
        ContinuitySearchQuery = string.Empty;
        ContinuitySearchResults.Clear();
    }

    partial void OnContinuitySearchQueryChanged(string value) => SearchContinuityCandidates();

    [RelayCommand]
    private void SearchContinuityCandidates()
    {
        ContinuitySearchResults.Clear();
        string query = ContinuitySearchQuery.Trim();
        if (query.Length == 0)
        {
            return;
        }

        using var context = _contextFactory();
        var matches = context.Continuities
            .AsEnumerable()
            .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();

        foreach (var continuity in matches)
        {
            ContinuitySearchResults.Add(new ContinuitySearchResult { ContinuityId = continuity.Id, Name = continuity.Name });
        }

        // No exact (case-insensitive) match among the results - offer a "create new" row rather
        // than requiring a separate action, matching ContinuityResolver.GetOrCreate's own
        // case-insensitive matching so this picker can never produce a near-duplicate.
        if (!matches.Any(c => string.Equals(c.Name, query, StringComparison.OrdinalIgnoreCase)))
        {
            ContinuitySearchResults.Add(new ContinuitySearchResult { Name = query, IsNew = true });
        }
    }

    [RelayCommand]
    private void AddContinuity(ContinuitySearchResult? target)
    {
        if (target is null || _seriesId is not int currentSeriesId)
        {
            return;
        }

        using var context = _contextFactory();
        var continuity = target.IsNew ? ContinuityResolver.GetOrCreate(context, target.Name) : context.Continuities.Find(target.ContinuityId);
        if (continuity is not null)
        {
            ContinuityResolver.AddSeriesToContinuity(context, currentSeriesId, continuity.Id);
        }

        IsAddingContinuity = false;
        ContinuitySearchQuery = string.Empty;
        ContinuitySearchResults.Clear();
        RefreshContinuity(context, currentSeriesId);
    }

    [RelayCommand]
    private void RemoveContinuity(ContinuityChip? chip)
    {
        if (chip is null || _seriesId is not int currentSeriesId)
        {
            return;
        }

        using var context = _contextFactory();
        ContinuityResolver.RemoveSeriesFromContinuity(context, currentSeriesId, chip.ContinuityId);
        RefreshContinuity(context, currentSeriesId);
    }

    // --- Related tab: Same Event (docs/superpowers/specs/2026-08-17-metadata-model-phase4b-
    // story-events-design.md) - read-only, populated from EventMembership, no creation UI here
    // (that lives on the Events screen). ---

    private void RefreshSameEvent(PaperbunkrDbContext context, int seriesId)
    {
        SameEvent.Clear();
        foreach (var otherSeries in EventMembershipResolver.GetOtherSeriesInSharedEvents(context, seriesId))
        {
            SameEvent.Add(new RelatedGroupSeriesSample
            {
                Title = otherSeries.Name,
                Name = otherSeries.Name,
                Note = "Same event",
                CoverBrush = SeriesCardSample.CoverBrushFor(otherSeries.Name),
                SeriesId = otherSeries.Id,
            });
        }

        OnPropertyChanged(nameof(HasSameEvent));
    }

    private void SetReadingModeLabel(ReadingMode mode)
    {
        ReadingModeLabel = mode switch
        {
            ReadingMode.RightToLeft => "Right to Left ▾",
            ReadingMode.VerticalContinuous => "Vertical (Continuous) ▾",
            ReadingMode.HorizontalContinuous => "Horizontal (Continuous) ▾",
            _ => "Left to Right ▾",
        };
        OnPropertyChanged(nameof(ReadingModeLabel));
    }

    /// <summary>
    /// Binary flip only (docs/superpowers/specs/2026-08-07-reader-rtl-navigation-design.md §5) -
    /// <see cref="Entities.ReadingMode.VerticalContinuous"/>/<see cref="Entities.ReadingMode.HorizontalContinuous"/>
    /// are unreachable via any UI today (only CE migration data produces them, and there's no
    /// scroll-paging implementation behind them yet), so toggling from either of those collapses
    /// to <see cref="Entities.ReadingMode.RightToLeft"/> rather than growing a full mode picker.
    /// </summary>
    [RelayCommand]
    private void ToggleReadingMode()
    {
        if (_seriesId is not int seriesId)
        {
            return;
        }

        using var context = _contextFactory();
        var series = context.Series.FirstOrDefault(s => s.Id == seriesId);
        if (series is null)
        {
            return;
        }

        series.ReadingMode = series.ReadingMode == ReadingMode.RightToLeft ? ReadingMode.LeftToRight : ReadingMode.RightToLeft;
        context.SaveChanges();

        SetReadingModeLabel(series.ReadingMode);
    }

    [ObservableProperty]
    private string _activeTab = "issues";

    public bool IsIssuesTab => ActiveTab == "issues";
    public bool IsRelatedTab => ActiveTab == "related";
    public bool IsDetailsTab => ActiveTab == "details";
    public bool IsActivityTab => ActiveTab == "activity";

    partial void OnActiveTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsIssuesTab));
        OnPropertyChanged(nameof(IsRelatedTab));
        OnPropertyChanged(nameof(IsDetailsTab));
        OnPropertyChanged(nameof(IsActivityTab));
    }

    [RelayCommand]
    private void GoIssues() => ActiveTab = "issues";

    [RelayCommand]
    private void GoRelated() => ActiveTab = "related";

    [RelayCommand]
    private void GoDetails() => ActiveTab = "details";

    [RelayCommand]
    private void GoActivity() => ActiveTab = "activity";

    /// <summary>
    /// Left-click tile selection (docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md
    /// §1): plain click toggles the clicked tile; shift-click additionally selects the contiguous
    /// range from the last-toggled tile to this one, without clearing the existing selection.
    /// </summary>
    public void ToggleIssueSelection(IssueCardSample issue, bool isShiftHeld)
    {
        int index = Issues.IndexOf(issue);
        if (index < 0)
        {
            return;
        }

        if (isShiftHeld && _lastToggledIndex is int lastIndex)
        {
            int start = Math.Min(lastIndex, index);
            int end = Math.Max(lastIndex, index);
            for (int i = start; i <= end; i++)
            {
                Issues[i].IsSelected = true;
                SelectedIssueIds.Add(Issues[i].Id);
            }
        }
        else
        {
            issue.IsSelected = !issue.IsSelected;
            if (issue.IsSelected)
            {
                SelectedIssueIds.Add(issue.Id);
            }
            else
            {
                SelectedIssueIds.Remove(issue.Id);
            }
        }

        _lastToggledIndex = index;
        _onSelectionChanged?.Invoke();
    }

    /// <summary>
    /// Right-click entry point into the Issue Properties editor(s) (docs/superpowers/specs/
    /// 2026-08-07-issue-properties-editor-design.md §2, extended by docs/superpowers/specs/
    /// 2026-08-07-bulk-issue-editing-design.md §2) - operates on the current selection union the
    /// right-clicked tile, so right-clicking a lone unselected tile with nothing else selected
    /// (today's entire pre-existing behavior) still just edits that one issue.
    /// </summary>
    [RelayCommand]
    private void EditIssueProperties(IssueCardSample issue)
    {
        var ids = (SelectedIssueIds.Count > 0 ? SelectedIssueIds.Append(issue.Id) : new[] { issue.Id })
            .Distinct()
            .ToList();

        if (ids.Count == 1)
        {
            _goToProperties(ids[0]);
        }
        else
        {
            _goToBulkProperties(ids);
        }
    }

    /// <summary>
    /// Right-click "Show in Explorer" (docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-
    /// fileless-entries-design.md §1) - same selection-union shape as <see cref="EditIssueProperties"/>
    /// above, so right-clicking a lone unselected tile still just reveals that one file, but a
    /// multi-selection reveals every uniquely-folder'd file at once. No cross-screen navigation
    /// needed (pure OS side effect), so this stays entirely local - no injected callback.
    /// </summary>
    [RelayCommand]
    private void RevealIssue(IssueCardSample issue)
    {
        var ids = (SelectedIssueIds.Count > 0 ? SelectedIssueIds.Append(issue.Id) : new[] { issue.Id })
            .Distinct()
            .ToList();

        using var context = _contextFactory();
        var issues = context.Issues.Where(i => ids.Contains(i.Id)).ToList();

        if (issues.Count == 1)
        {
            RevealInExplorerHelper.RevealIssue(issues[0]);
        }
        else
        {
            RevealInExplorerHelper.RevealIssues(issues);
        }
    }
}
