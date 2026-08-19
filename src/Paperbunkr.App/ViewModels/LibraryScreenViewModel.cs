using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using cYo.Common.Collections;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Library grid + toolbar, ported from LibraryScreen.dc.html (Claude Design project 43c40b25),
/// "pills" toolbar variant (the default selected in the parent "Paperbunkr App" wireframe).
/// Loads real Series records from <see cref="PaperbunkrDb"/> (docs/onboarding.md §5-6) rather
/// than the hardcoded sample data this originally shipped with.
/// </summary>
public partial class LibraryScreenViewModel : ViewModelBase
{
    private readonly Action<int> _goDetail;
    private readonly Action<int> _goReaderForIssue;
    private readonly Action<int, int, bool> _goToNewIssueProperties;

    private ContentType? _activeContentType;
    private int? _activeCategoryId;

    /// <summary>Back/forward history (docs/superpowers/specs/2026-08-19-library-browse-history-
    /// design.md) - reuses CE's own already-ported <see cref="CursorList{T}"/> as-is, no changes
    /// needed to it.</summary>
    private readonly CursorList<LibraryBrowseState> _browseHistory = new();
    private bool _isNavigatingHistory;

    /// <summary>Debounce for search-query history pushes only - the search-as-you-type behavior
    /// itself (<see cref="OnSearchQueryChanged"/>'s existing reload) is unchanged and stays
    /// instant; only recording a *history step* waits for a pause in typing, so Back/Forward moves
    /// through meaningful searches instead of one keystroke at a time.</summary>
    private static readonly TimeSpan SearchHistoryDebounce = TimeSpan.FromMilliseconds(800);
    private DispatcherTimer? _searchHistoryDebounceTimer;

    public LibraryScreenViewModel(Action<int> goDetail, Action<int> goReaderForIssue, Action<int, int, bool> goToNewIssueProperties)
    {
        _goDetail = goDetail;
        _goReaderForIssue = goReaderForIssue;
        _goToNewIssueProperties = goToNewIssueProperties;
        Covers = new ObservableCollection<SeriesCardSample>();
        Groups = new ObservableCollection<SeriesCardGroup>();
        ContentTypes = new ObservableCollection<ContentTypeSummary>();
        Collections = new ObservableCollection<CategorySummary>();
        ExistingSeriesNames = new ObservableCollection<string>();
        IssueList = new IssueListScreenViewModel(goReaderForIssue);
        // Two independent axes now (docs/superpowers/specs/2026-08-18-library-book-centric-
        // redesign-design.md Slice 3, then the same-session follow-up that brought series-cards
        // back as a real option instead of a full replacement): LibraryViewMode is the layout
        // *shape* (grid/list/tiles/etc.), LibraryContentGranularity is *what's in each tile*
        // (one card per series, aggregated, vs one tile per issue). IssueList owns the Issue-
        // granularity's sort/group/rows; Covers/Groups below own the Series-granularity's. Both
        // computed on every LoadFromDatabase regardless of which is currently displayed, matching
        // this ViewModel's existing "library sizes here are small enough" cost tradeoff elsewhere -
        // simpler and safer against stale-data bugs than only computing the active one.
        IssueList.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IssueListScreenViewModel.SortField) or nameof(IssueListScreenViewModel.IsGrouped))
            {
                OnPropertyChanged(nameof(ShowAlphabetIndex));
            }

            if (e.PropertyName is nameof(IssueListScreenViewModel.HasAnyResults))
            {
                OnPropertyChanged(nameof(HasAnyResults));
            }

            if (e.PropertyName is nameof(IssueListScreenViewModel.SortField) or nameof(IssueListScreenViewModel.SortDirection))
            {
                OnPropertyChanged(nameof(ActiveSortLabel));
            }

            if (e.PropertyName is nameof(IssueListScreenViewModel.GroupField))
            {
                OnPropertyChanged(nameof(ActiveGroupLabel));
            }

            // IssueList's own sort/group persists on every change, same as every other Library
            // field's own On*Changed hook already does - see AppSettings.LibraryIssueListSortField.
            if (e.PropertyName is nameof(IssueListScreenViewModel.SortField)
                or nameof(IssueListScreenViewModel.SortDirection)
                or nameof(IssueListScreenViewModel.GroupField))
            {
                SaveLibrarySettings();
            }
        };
        LoadLibrarySettings();

        // Seeds history entry #1 with the just-loaded state, matching CE's own behavior of the
        // very first BookList assignment already counting as the first history entry - so
        // CanBrowsePrevious is meaningfully false until a second, different state gets pushed,
        // not true from a phantom pre-history state.
        _browseHistory.AddAtCursor(CurrentBrowseState());

        LoadFromDatabase();
    }

    private LibraryBrowseState CurrentBrowseState() => new(_activeContentType, _activeCategoryId, SearchQuery);

    public bool CanBrowsePrevious => _browseHistory.CanMoveCursorPrevious;
    public bool CanBrowseNext => _browseHistory.CanMoveCursorNext;

    private void RaiseBrowseHistoryChanged()
    {
        OnPropertyChanged(nameof(CanBrowsePrevious));
        OnPropertyChanged(nameof(CanBrowseNext));
    }

    /// <summary>Pushes the current state as a new history step - a no-op if it equals the state the
    /// cursor's already on (<see cref="CursorList{T}.AddAtCursor(T)"/>'s own dedup), otherwise
    /// truncates any abandoned "forward" branch before appending. Never called while
    /// <see cref="_isNavigatingHistory"/> - applying a Back/Forward state doesn't re-push itself.</summary>
    private void PushBrowseHistory()
    {
        if (_isNavigatingHistory)
        {
            return;
        }

        _browseHistory.AddAtCursor(CurrentBrowseState());
        RaiseBrowseHistoryChanged();
    }

    private void ApplyBrowseState(LibraryBrowseState state)
    {
        _isNavigatingHistory = true;
        try
        {
            _activeContentType = state.ActiveContentType;
            _activeCategoryId = state.ActiveCategoryId;
            SearchQuery = state.SearchQuery; // Goes through the normal setter - same reload/save path as any other search-query change.
            SaveLibrarySettings();
            LoadFromDatabase();
        }
        finally
        {
            _isNavigatingHistory = false;
        }

        RaiseBrowseHistoryChanged();
    }

    [RelayCommand]
    private void BrowsePrevious()
    {
        var node = _browseHistory.MoveCursorPrevious();
        if (node is not null)
        {
            ApplyBrowseState(node.Value);
        }
    }

    [RelayCommand]
    private void BrowseNext()
    {
        var node = _browseHistory.MoveCursorNext();
        if (node is not null)
        {
            ApplyBrowseState(node.Value);
        }
    }

    /// <summary>Stops the search-history debounce timer (if pending) and immediately runs its push -
    /// exists purely for testability (an 800ms real-time wait in a unit test is slow and flaky);
    /// production code never calls this, only the timer's own <c>Tick</c> does the equivalent inline.</summary>
    internal void FlushSearchHistoryDebounce()
    {
        if (_searchHistoryDebounceTimer is null)
        {
            return;
        }

        _searchHistoryDebounceTimer.Stop();
        PushBrowseHistory();
    }

    /// <summary>Issue-granularity's sort/group/rows owner - see the constructor's doc comment.</summary>
    public IssueListScreenViewModel IssueList { get; }

    /// <summary>
    /// Seeds sort/group/display/filter state from <see cref="Paperbunkr.Data.Entities.AppSettings"/>
    /// (docs/superpowers/specs/2026-08-17-library-saved-list-layouts-design.md) before the first
    /// <see cref="LoadFromDatabase"/> call, via direct field assignment rather than the public
    /// property setters - those setters' <c>On*Changed</c> partials call <see cref="LoadFromDatabase"/>
    /// and <see cref="SaveLibrarySettings"/> themselves, which during construction would mean one
    /// redundant DB round-trip per field instead of the single one this method plus the constructor's
    /// own <see cref="LoadFromDatabase"/> call already need.
    /// </summary>
    private void LoadLibrarySettings()
    {
        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();

        // Deliberate direct field writes, not the generated properties (which would each re-run
        // On*Changed's own LoadFromDatabase/SaveLibrarySettings) - see this method's doc comment.
#pragma warning disable MVVMTK0034
        _granularity = settings.LibraryGranularity;
        _sortField = settings.LibrarySortField;
        _sortDirection = settings.LibrarySortDirection;
        _groupField = settings.LibraryGroupField;
        _viewMode = settings.LibraryViewMode;
        _gridDensity = settings.LibraryGridDensity;
        _showUnreadBadge = settings.LibraryShowUnreadBadge;
        _showPublisherBadge = settings.LibraryShowPublisherBadge;
        _showLanguageBadge = settings.LibraryShowLanguageBadge;
        _useLanguageIcon = settings.LibraryUseLanguageIcon;
        _showContinueReadingButton = settings.LibraryShowContinueReadingButton;
        _searchQuery = settings.LibrarySearchQuery ?? string.Empty;
        _searchMode = settings.LibrarySearchMode;
        _filterUnreadOnly = settings.LibraryFilterUnreadOnly;
        _filterMissingIssues = settings.LibraryFilterMissingIssues;
        _filterTrackedOnly = settings.LibraryFilterTrackedOnly;
#pragma warning restore MVVMTK0034

        // Stale-reference fallback: a category deleted since last session falls back to "All
        // Series" rather than silently rendering an empty grid with no visible reason why.
        if (settings.LibraryActiveCategoryId is int categoryId &&
            context.Categories.Any(c => c.Id == categoryId))
        {
            _activeCategoryId = categoryId;
        }
        else
        {
            _activeContentType = settings.LibraryActiveContentType;
        }

        // Seeded last, deliberately: IssueList has no direct-field seeding backdoor (private
        // fields on a different class), so these go through its normal property setters, which
        // raise PropertyChanged and trigger this constructor's relay straight back into
        // SaveLibrarySettings() below - harmless only because every other field above is already
        // seeded to its correct just-loaded value by this point, so that redundant write-back is a
        // no-op rather than corrupting anything with defaults.
        IssueList.SortField = settings.LibraryIssueListSortField;
        IssueList.SortDirection = settings.LibraryIssueListSortDirection;
        IssueList.GroupField = settings.LibraryIssueListGroupField;
    }

    /// <summary>Immediate write-back for every field <see cref="LoadLibrarySettings"/> seeds, called from each field's own change hook - no debounce, matching this ViewModel's existing no-debounce philosophy (see <see cref="SearchQuery"/>'s doc comment) and <see cref="Paperbunkr.App.ViewModels.ReaderScreenViewModel"/>'s equivalent immediate-write precedent for <c>AppSettings</c>.</summary>
    private void SaveLibrarySettings()
    {
        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();

        settings.LibraryGranularity = Granularity;
        settings.LibrarySortField = SortField;
        settings.LibrarySortDirection = SortDirection;
        settings.LibraryGroupField = GroupField;
        settings.LibraryIssueListSortField = IssueList.SortField;
        settings.LibraryIssueListSortDirection = IssueList.SortDirection;
        settings.LibraryIssueListGroupField = IssueList.GroupField;
        settings.LibraryViewMode = ViewMode;
        settings.LibraryGridDensity = GridDensity;
        settings.LibraryShowUnreadBadge = ShowUnreadBadge;
        settings.LibraryShowPublisherBadge = ShowPublisherBadge;
        settings.LibraryShowLanguageBadge = ShowLanguageBadge;
        settings.LibraryUseLanguageIcon = UseLanguageIcon;
        settings.LibraryShowContinueReadingButton = ShowContinueReadingButton;
        settings.LibrarySearchQuery = string.IsNullOrEmpty(SearchQuery) ? null : SearchQuery;
        settings.LibrarySearchMode = SearchMode;
        settings.LibraryActiveContentType = _activeContentType;
        settings.LibraryActiveCategoryId = _activeCategoryId;
        settings.LibraryFilterUnreadOnly = FilterUnreadOnly;
        settings.LibraryFilterMissingIssues = FilterMissingIssues;
        settings.LibraryFilterTrackedOnly = FilterTrackedOnly;

        context.SaveChanges();
    }

    /// <summary>Typeahead source for the "Add a physical book" flyout (docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md §2).</summary>
    public ObservableCollection<string> ExistingSeriesNames { get; }

    [ObservableProperty]
    private string _newIssueSeriesName = string.Empty;

    [ObservableProperty]
    private string _newIssueNumber = string.Empty;

    /// <summary>
    /// "+ Add" flow's Content Type picker (docs/superpowers/specs/2026-08-16-manga-content-type-
    /// classification-design.md §3). Applied only when <see cref="CreatePlaceholderIssue"/> actually
    /// creates a new row - never overwrites an existing matched series' classification (same
    /// wasCreated-gated safety already established for that command's deleteIfUnedited behavior).
    /// </summary>
    [ObservableProperty]
    private ContentType _newIssueContentType = ContentType.Comic;

    [ObservableProperty]
    private ReadingMode _newIssueReadingMode = ReadingMode.RightToLeft;

    public bool ShowAddReadingModePicker => NewIssueContentType is ContentType.Manga or ContentType.Manhua or ContentType.Manhwa;

    partial void OnNewIssueContentTypeChanged(ContentType value) => OnPropertyChanged(nameof(ShowAddReadingModePicker));

    [RelayCommand]
    private void SetNewIssueContentType(ContentType type) => NewIssueContentType = type;

    [RelayCommand]
    private void SetNewIssueReadingMode(ReadingMode mode) => NewIssueReadingMode = mode;

    /// <summary>A-Z jump indexer's letters (docs/superpowers/specs/2026-08-09-library-toolbar-design.md Phase B) - "#" covers names that don't start with a letter.</summary>
    public static IReadOnlyList<string> AlphabetIndexLetters { get; } =
        Enumerable.Range('A', 26).Select(c => ((char)c).ToString()).Append("#").ToList();

    /// <summary>Every <see cref="ContentType"/> with at least one series, real counts, sidebar filter (docs/superpowers/specs/2026-08-09-library-sidebar-categorization-design.md).</summary>
    public ObservableCollection<ContentTypeSummary> ContentTypes { get; }

    /// <summary>Real <c>Category</c> rows ("Collections") - empty today since nothing creates them yet; that's Beta-scoped. See spec above.</summary>
    public ObservableCollection<CategorySummary> Collections { get; }

    [ObservableProperty]
    private int _allSeriesCount;

    public bool IsAllSeriesActive => _activeContentType is null && _activeCategoryId is null;

    public bool HasCollections => Collections.Count > 0;

    /// <summary>One aggregated card per series - populated whenever <see cref="Granularity"/> is
    /// <see cref="LibraryContentGranularity.Series"/>. See <see cref="IssueList"/> for the
    /// per-issue equivalent.</summary>
    public ObservableCollection<SeriesCardSample> Covers { get; }

    /// <summary>Populated instead of <see cref="Covers"/> when <see cref="IsGrouped"/>.</summary>
    public ObservableCollection<SeriesCardGroup> Groups { get; }

    /// <summary>Series-card Sort field - only meaningful when <see cref="IsSeriesGranularity"/>; see
    /// <see cref="IssueList"/>'s own <c>SortField</c> for the per-issue equivalent.</summary>
    [ObservableProperty]
    private LibrarySortField _sortField = LibrarySortField.DateAdded;

    partial void OnSortFieldChanged(LibrarySortField value)
    {
        OnPropertyChanged(nameof(ShowAlphabetIndex));
        OnPropertyChanged(nameof(SortLabel));
        OnPropertyChanged(nameof(ActiveSortLabel));
        SaveLibrarySettings();
        LoadFromDatabase();
    }

    [ObservableProperty]
    private SortDirection _sortDirection = SortDirection.Descending;

    partial void OnSortDirectionChanged(SortDirection value)
    {
        OnPropertyChanged(nameof(SortLabel));
        OnPropertyChanged(nameof(ActiveSortLabel));
        SaveLibrarySettings();
        LoadFromDatabase();
    }

    [RelayCommand]
    private void ToggleSortDirection() =>
        SortDirection = SortDirection == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;

    public string SortLabel =>
        (LibraryFieldCatalog.SortFields.TryGetValue(SortField, out var sortDescriptor) ? sortDescriptor.DisplayName : "Sort")
        + (SortDirection == SortDirection.Ascending ? " ↑" : " ↓");

    [ObservableProperty]
    private LibraryGroupField _groupField = LibraryGroupField.None;

    public bool IsGrouped => GroupField != LibraryGroupField.None;

    public string GroupLabel => GroupField == LibraryGroupField.None
        ? "None"
        : LibraryFieldCatalog.GroupFields[GroupField].DisplayName;

    partial void OnGroupFieldChanged(LibraryGroupField value)
    {
        OnPropertyChanged(nameof(IsGrouped));
        OnPropertyChanged(nameof(GroupLabel));
        OnPropertyChanged(nameof(ActiveGroupLabel));
        OnPropertyChanged(nameof(ShowAlphabetIndex));
        SaveLibrarySettings();
        LoadFromDatabase();
    }

    [RelayCommand]
    private void SetSortField(LibrarySortField field) => SortField = field;

    [RelayCommand]
    private void SetGroupField(LibraryGroupField field) => GroupField = field;

    [ObservableProperty]
    private LibraryContentGranularity _granularity = LibraryContentGranularity.Issue;

    public bool IsSeriesGranularity => Granularity == LibraryContentGranularity.Series;
    public bool IsIssueGranularity => Granularity == LibraryContentGranularity.Issue;

    partial void OnGranularityChanged(LibraryContentGranularity value)
    {
        OnPropertyChanged(nameof(IsSeriesGranularity));
        OnPropertyChanged(nameof(IsIssueGranularity));
        OnPropertyChanged(nameof(SortLabel));
        OnPropertyChanged(nameof(GroupLabel));
        OnPropertyChanged(nameof(ActiveSortLabel));
        OnPropertyChanged(nameof(ActiveGroupLabel));
        OnPropertyChanged(nameof(HasAnyResults));
        OnPropertyChanged(nameof(ShowAlphabetIndex));
        SaveLibrarySettings();
    }

    [RelayCommand]
    private void SetGranularity(LibraryContentGranularity granularity) => Granularity = granularity;

    /// <summary>P6 fix (docs/alpha-todo.md) - none of the display modes had a "no series match"
    /// empty state; delegates to whichever granularity is active.</summary>
    public bool HasAnyResults => IsSeriesGranularity ? (Covers.Count > 0 || Groups.Count > 0) : IssueList.HasAnyResults;

    /// <summary>Sort/Group toolbar pills show whichever granularity's own label is currently active -
    /// lets the XAML toolbar bind one pair of pills instead of branching per granularity itself.</summary>
    public string ActiveSortLabel => IsSeriesGranularity ? SortLabel : IssueList.SortLabelWithDirection;

    public string ActiveGroupLabel => IsSeriesGranularity ? GroupLabel : IssueList.GroupFieldLabel;

    /// <summary>
    /// Reloads everything from the database: the sidebar's <see cref="ContentTypes"/>/
    /// <see cref="Collections"/> summaries (always full, unfiltered counts), <see cref="IssueList"/>'s
    /// rows, AND <see cref="Covers"/>/<see cref="Groups"/> - both granularities are always computed
    /// regardless of which is currently displayed, see the constructor's doc comment for why.
    /// Filtered by whichever of <see cref="_activeContentType"/>/<see cref="_activeCategoryId"/> is
    /// set, mutually exclusive - both null means "All Series"). Re-queries on every sidebar click
    /// rather than caching the last series list, matching this codebase's existing convention of
    /// hitting the DB fresh per user action.
    /// </summary>
    public void LoadFromDatabase()
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series
            .Include(s => s.Issues)
            .Include(s => s.Categories)
            .Include(s => s.TrackingLinks)
            .OrderBy(s => s.SortName ?? s.Name)
            .ToList();

        AllSeriesCount = series.Count;

        ExistingSeriesNames.Clear();
        foreach (string name in series.Select(s => s.Name).Distinct().OrderBy(n => n))
        {
            ExistingSeriesNames.Add(name);
        }

        ContentTypes.Clear();
        foreach (var group in series.GroupBy(s => s.ContentType).OrderBy(g => g.Key))
        {
            ContentTypes.Add(new ContentTypeSummary
            {
                ContentType = group.Key,
                Name = group.Key.ToString(),
                Count = group.Count(),
                IsActive = _activeContentType == group.Key,
            });
        }

        var categories = context.Categories.Include(c => c.Series).OrderBy(c => c.SortOrder).ToList();
        Collections.Clear();
        foreach (var category in categories)
        {
            Collections.Add(new CategorySummary
            {
                Id = category.Id,
                Name = category.Name,
                Count = category.Series.Count,
                IsActive = _activeCategoryId == category.Id,
            });
        }

        IEnumerable<Series> filtered = series;
        if (_activeContentType is ContentType contentType)
        {
            filtered = filtered.Where(s => s.ContentType == contentType);
        }
        else if (_activeCategoryId is int categoryId)
        {
            filtered = filtered.Where(s => s.Categories.Any(c => c.Id == categoryId));
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string query = SearchQuery.Trim();
            filtered = filtered.Where(s => MatchesSearch(s, query));
        }

        if (FilterTrackedOnly)
        {
            filtered = filtered.Where(s => s.TrackingLinks.Count > 0);
        }

        // Series granularity: Unread/Missing apply at the series level ("series containing at
        // least one such issue") - a card represents the whole series, so that's the only filter
        // semantics that makes sense for it. This is CE's original per-series filter behavior,
        // predating Slice 3.
        IEnumerable<Series> seriesForCards = filtered;
        if (FilterUnreadOnly)
        {
            seriesForCards = seriesForCards.Where(s => s.Issues.Any(i => i.LastPageRead is null or 0));
        }

        if (FilterMissingIssues)
        {
            seriesForCards = seriesForCards.Where(s => s.Issues.Any(i => i.FileIsMissing));
        }

        var cards = SortCards(seriesForCards.Select(SeriesCardSample.FromSeries).ToList());
        Covers.Clear();
        Groups.Clear();
        if (IsGrouped)
        {
            foreach (var group in GroupCards(cards))
            {
                Groups.Add(group);
            }
        }
        else
        {
            foreach (var card in cards)
            {
                Covers.Add(card);
            }
        }

        // Issue granularity (docs/superpowers/specs/2026-08-18-library-book-centric-redesign-
        // design.md Slice 3): Library's other data pipeline, feeding IssueList's own sort/group/
        // rows. Unread/Missing apply to individual issues here, not "series containing at least
        // one", matching CE's real per-book filter semantics - deliberately different from the
        // series-card filtering just above.
        IEnumerable<Issue> issues = filtered.SelectMany(s => s.Issues);
        if (FilterUnreadOnly)
        {
            issues = issues.Where(i => i.LastPageRead is null or 0);
        }

        if (FilterMissingIssues)
        {
            issues = issues.Where(i => i.FileIsMissing);
        }

        IssueList.SetRows(issues);

        OnPropertyChanged(nameof(IsAllSeriesActive));
        OnPropertyChanged(nameof(HasCollections));
        OnPropertyChanged(nameof(HasAnyResults));
    }

    /// <summary>
    /// Sorts by <see cref="SortField"/>/<see cref="SortDirection"/> - Series-level aggregates
    /// already computed on each <see cref="SeriesCardSample"/>, not re-derived from the Issue
    /// entities here. Comparison logic itself lives in <see cref="LibraryFieldCatalog"/> (docs/
    /// superpowers/specs/2026-08-17-metadata-model-phase2c-library-field-descriptors-design.md).
    /// </summary>
    private List<SeriesCardSample> SortCards(List<SeriesCardSample> cards)
    {
        var descriptor = LibraryFieldCatalog.SortFields.TryGetValue(SortField, out var found)
            ? found
            : LibraryFieldCatalog.SortFields[LibrarySortField.Name];

        var result = cards.ToList();
        result.Sort(descriptor.Compare);
        if (SortDirection == SortDirection.Descending)
        {
            result.Reverse();
        }

        return result;
    }

    /// <summary>
    /// Partitions already-sorted <paramref name="cards"/> into <see cref="SeriesCardGroup"/>s -
    /// LINQ's GroupBy preserves each group's internal element order, so Sort's order survives
    /// intact within every group without any extra work here. Bucketing/ordering logic itself lives
    /// in <see cref="LibraryFieldCatalog"/>; <see cref="LibraryGroupField.None"/> (and any future
    /// gap) falls through to the same empty result the old switch's default branch returned.
    /// </summary>
    private IEnumerable<SeriesCardGroup> GroupCards(List<SeriesCardSample> cards)
    {
        if (!LibraryFieldCatalog.GroupFields.TryGetValue(GroupField, out var descriptor))
        {
            return Enumerable.Empty<SeriesCardGroup>();
        }

        return cards
            .GroupBy(descriptor.GroupKey)
            .OrderBy(g => g.Key, Comparer<string>.Create(descriptor.GroupOrder))
            .Select(g => new SeriesCardGroup { Header = g.Key, Items = new ObservableCollection<SeriesCardSample>(g) });
    }

    /// <summary>
    /// Mirrors CE's real QuickSearch mode dropdown (<c>ComicBookAllPropertiesMatcher</c> in
    /// <c>_reference/ComicRackCE/ComicRack.Engine/Metadata/ComicBook/Matcher/
    /// ComicBookAllPropertiesMatcher.cs:138-230</c>) - each <see cref="SearchMode"/> value searches
    /// the same fixed field set CE does, all real <see cref="Issue"/> properties already in this
    /// schema. Runs against the already-materialized in-memory <paramref name="s"/>.Issues (this
    /// method is called from <see cref="LoadFromDatabase"/>'s LINQ-to-Objects stage, after
    /// <c>.ToList()</c> - not translated to SQL), so full C# string matching is fine, no EF
    /// provider-translation constraints to work around.
    /// </summary>
    private bool MatchesSearch(Series s, string query) => SearchMode switch
    {
        SearchMode.Series => Contains(s.Name, query) || s.Issues.Any(i =>
            Contains(i.AlternateSeries, query) || Contains(i.Format, query) ||
            Contains(i.SeriesGroup, query) || Contains(i.StoryArc, query)),
        SearchMode.Writer => s.Issues.Any(i => Contains(i.Writer, query)),
        SearchMode.Artists => s.Issues.Any(i =>
            Contains(i.Writer, query) || Contains(i.Penciller, query) || Contains(i.Inker, query) ||
            Contains(i.Colorist, query) || Contains(i.Editor, query) || Contains(i.Translator, query) ||
            Contains(i.Letterer, query) || Contains(i.CoverArtist, query)),
        SearchMode.Descriptive => s.Issues.Any(i =>
            Contains(i.Notes, query) || Contains(i.Summary, query) || Contains(i.Review, query) ||
            Contains(i.Tags, query) || Contains(i.MainCharacterOrTeam, query) || Contains(i.Teams, query) ||
            Contains(i.Locations, query) || Contains(i.ScanInformation, query)),
        SearchMode.File => s.Issues.Any(i => Contains(i.FilePath, query)),
        SearchMode.Catalog => s.Issues.Any(i =>
            Contains(i.BookAge, query) || Contains(i.BookCollectionStatus, query) || Contains(i.BookNotes, query) ||
            Contains(i.BookOwner, query) || Contains(i.BookStore, query) || Contains(i.BookLocation, query) ||
            Contains(i.ISBN, query)),
        _ => Contains(s.Name, query) || Contains(s.Publisher, query) || Contains(s.Genre, query) || s.Issues.Any(i =>
            Contains(i.AlternateSeries, query) || Contains(i.EffectiveTitle(), query) ||
            Contains(i.SeriesGroup, query) || Contains(i.StoryArc, query) ||
            Contains(i.Writer, query) || Contains(i.Penciller, query) || Contains(i.Inker, query) ||
            Contains(i.Colorist, query) || Contains(i.Letterer, query) || Contains(i.Editor, query) ||
            Contains(i.Translator, query) || Contains(i.CoverArtist, query) ||
            Contains(i.Summary, query) || Contains(i.Notes, query) || Contains(i.Review, query) ||
            Contains(i.FilePath, query) || Contains(i.Genre, query) || Contains(i.Publisher, query) ||
            Contains(i.Imprint, query) || Contains(i.Volume, query) || Contains(i.Number, query) ||
            Contains(i.AlternateNumber, query) || Contains(i.Format, query) || Contains(i.AgeRating, query) ||
            Contains(i.Tags, query) || Contains(i.MainCharacterOrTeam, query) || Contains(i.Teams, query) ||
            Contains(i.Locations, query) || Contains(i.BookAge, query) || Contains(i.BookCollectionStatus, query) ||
            Contains(i.BookNotes, query) || Contains(i.BookOwner, query) || Contains(i.BookStore, query) ||
            Contains(i.BookLocation, query) || Contains(i.ISBN, query) || Contains(i.ScanInformation, query)),
    };

    private static bool Contains(string? value, string query) =>
        !string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void SelectAllSeries()
    {
        _activeContentType = null;
        _activeCategoryId = null;
        SaveLibrarySettings();
        LoadFromDatabase();
        PushBrowseHistory();
    }

    [RelayCommand]
    private void SelectContentType(ContentTypeSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        _activeContentType = summary.ContentType;
        _activeCategoryId = null;
        SaveLibrarySettings();
        LoadFromDatabase();
        PushBrowseHistory();
    }

    [RelayCommand]
    private void SelectCollection(CategorySummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        _activeCategoryId = summary.Id;
        _activeContentType = null;
        SaveLibrarySettings();
        LoadFromDatabase();
        PushBrowseHistory();
    }

    /// <summary>
    /// Free-text search, scoped to <see cref="SearchMode"/>'s field set (docs/superpowers/specs/
    /// 2026-08-09-library-toolbar-design.md Phase B, extended per CE's real QuickSearch mode
    /// dropdown - see <see cref="SearchMode"/>'s doc comment). No debounce: requerying on every
    /// keystroke matches how every other filter in this ViewModel already behaves (sidebar clicks,
    /// the 3 checkboxes below), and library sizes here are small enough that a plain SQLite query
    /// is fast regardless.
    /// </summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    partial void OnSearchQueryChanged(string value)
    {
        SaveLibrarySettings();
        LoadFromDatabase();

        // History-push is debounced (~800ms pause in typing) - the reload above is not, and stays
        // exactly as instant as it always was. Guarded against ApplyBrowseState's own SearchQuery
        // write, which shouldn't re-arm a debounce for the state it just navigated to.
        if (_isNavigatingHistory)
        {
            return;
        }

        if (_searchHistoryDebounceTimer is null)
        {
            _searchHistoryDebounceTimer = new DispatcherTimer { Interval = SearchHistoryDebounce };
            _searchHistoryDebounceTimer.Tick += OnSearchHistoryDebounceElapsed;
        }

        _searchHistoryDebounceTimer.Stop(); // Restarts the countdown - each keystroke pushes the deadline out.
        _searchHistoryDebounceTimer.Start();
    }

    private void OnSearchHistoryDebounceElapsed(object? sender, EventArgs e)
    {
        _searchHistoryDebounceTimer!.Stop();
        PushBrowseHistory();
    }

    /// <summary>Field scope for <see cref="SearchQuery"/> - see <see cref="Paperbunkr.Data.Entities.SearchMode"/>'s doc comment for the CE source this mirrors.</summary>
    [ObservableProperty]
    private SearchMode _searchMode = SearchMode.All;

    public string SearchModeLabel => SearchMode.ToString();

    partial void OnSearchModeChanged(SearchMode value)
    {
        OnPropertyChanged(nameof(SearchModeLabel));
        SaveLibrarySettings();
        LoadFromDatabase();
    }

    [RelayCommand]
    private void SetSearchMode(SearchMode mode) => SearchMode = mode;

    [ObservableProperty]
    private bool _filterUnreadOnly;

    partial void OnFilterUnreadOnlyChanged(bool value)
    {
        SaveLibrarySettings();
        LoadFromDatabase();
    }

    [ObservableProperty]
    private bool _filterMissingIssues;

    partial void OnFilterMissingIssuesChanged(bool value)
    {
        SaveLibrarySettings();
        LoadFromDatabase();
    }

    [ObservableProperty]
    private bool _filterTrackedOnly;

    partial void OnFilterTrackedOnlyChanged(bool value)
    {
        SaveLibrarySettings();
        LoadFromDatabase();
    }

    /// <summary>A-Z indexer only means something against an alphabetically-ordered, ungrouped flat
    /// list (docs/superpowers/specs/2026-08-09-library-toolbar-design.md Phase C) - now reads
    /// <see cref="IssueList"/>'s sort/group state directly since that's the only one left (see the
    /// constructor's relay for how this stays live).</summary>
    public bool ShowAlphabetIndex => IsSeriesGranularity
        ? SortField == LibrarySortField.Name && !IsGrouped
        : IssueList.SortField == IssueListSortField.Series && !IssueList.IsGrouped;

    [ObservableProperty]
    private string? _activeDropdown;

    public bool IsFilterOpen => ActiveDropdown == "filter";
    public bool IsSortOpen => ActiveDropdown == "sort";
    public bool IsGroupOpen => ActiveDropdown == "group";
    public bool IsDisplayOpen => ActiveDropdown == "display";
    public bool IsAddOpen => ActiveDropdown == "add";
    public bool IsSearchModeOpen => ActiveDropdown == "searchMode";

    partial void OnActiveDropdownChanged(string? value)
    {
        OnPropertyChanged(nameof(IsFilterOpen));
        OnPropertyChanged(nameof(IsSortOpen));
        OnPropertyChanged(nameof(IsGroupOpen));
        OnPropertyChanged(nameof(IsDisplayOpen));
        OnPropertyChanged(nameof(IsAddOpen));
        OnPropertyChanged(nameof(IsSearchModeOpen));
    }

    [RelayCommand]
    private void ToggleSearchMode() => ActiveDropdown = ActiveDropdown == "searchMode" ? null : "searchMode";

    [RelayCommand]
    private void ToggleFilter() => ActiveDropdown = ActiveDropdown == "filter" ? null : "filter";

    [RelayCommand]
    private void ToggleSort() => ActiveDropdown = ActiveDropdown == "sort" ? null : "sort";

    [RelayCommand]
    private void ToggleGroup() => ActiveDropdown = ActiveDropdown == "group" ? null : "group";

    [RelayCommand]
    private void ToggleDisplay() => ActiveDropdown = ActiveDropdown == "display" ? null : "display";

    [RelayCommand]
    private void ToggleAdd()
    {
        ActiveDropdown = ActiveDropdown == "add" ? null : "add";
        NewIssueSeriesName = string.Empty;
        NewIssueNumber = string.Empty;
        NewIssueContentType = ContentType.Comic;
        NewIssueReadingMode = ReadingMode.RightToLeft;
    }

    /// <summary>Reveals THIS tile's own issue file directly (docs/superpowers/specs/
    /// 2026-08-18-library-book-centric-redesign-design.md Slice 3) - simpler and more correct than
    /// the old series-card version's "first issue" heuristic, which only existed because a series
    /// card had no single file of its own to point at.</summary>
    [RelayCommand]
    private void RevealIssue(int issueId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var issue = context.Issues.Find(issueId);
        if (issue is not null)
        {
            RevealInExplorerHelper.RevealIssue(issue);
        }
    }

    /// <summary>Opens the clicked tile's series in Detail (docs/superpowers/specs/
    /// 2026-08-18-library-book-centric-redesign-design.md Slice 3 open question #1) - tiles
    /// themselves now open the Reader directly (<see cref="IssueListScreenViewModel.OpenIssueCommand"/>),
    /// so this is the tile context menu's own entry point to series-level actions/metadata.</summary>
    [RelayCommand]
    private void GoToSeries(int seriesId) => _goDetail(seriesId);

    /// <summary>Series-card equivalent of <see cref="RevealIssueCommand"/> - a series card has no
    /// single file of its own, so this reveals its first issue's folder instead (docs/superpowers/
    /// specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md §1). Only reachable when
    /// <see cref="IsSeriesGranularity"/>.</summary>
    [RelayCommand]
    private void RevealSeries(SeriesCardSample card)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Include(s => s.Issues).FirstOrDefault(s => s.Id == card.SeriesId);
        if (series is not null)
        {
            RevealInExplorerHelper.RevealSeries(series);
        }
    }

    /// <summary>Series card click, opens Detail - the per-issue tile equivalent is
    /// <see cref="IssueListScreenViewModel.OpenIssueCommand"/>. Only reachable when
    /// <see cref="IsSeriesGranularity"/>.</summary>
    [RelayCommand]
    private void SelectCard(SeriesCardSample? card)
    {
        if (card is not null)
        {
            _goDetail(card.SeriesId);
        }
    }

    /// <summary>
    /// Tile context menu's "Set Content Type" picker (docs/superpowers/specs/2026-08-16-manga-
    /// content-type-classification-design.md §2) - one small command per fixed value (Avalonia's
    /// CommandParameter only carries a single value, and this needs both "which series" and "which
    /// value"; matches this codebase's existing per-value-command shape) each taking the
    /// right-clicked tile's <c>SeriesId</c> as CommandParameter, same as <see cref="RevealIssueCommand"/>.
    /// </summary>
    private void SetSeriesContentType(int seriesId, ContentType type)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Find(seriesId);
        if (series is not null)
        {
            series.ContentType = type;
            context.SaveChanges();
            LoadFromDatabase();
        }
    }

    [RelayCommand] private void SetSeriesContentTypeComic(int seriesId) => SetSeriesContentType(seriesId, ContentType.Comic);
    [RelayCommand] private void SetSeriesContentTypeManga(int seriesId) => SetSeriesContentType(seriesId, ContentType.Manga);
    [RelayCommand] private void SetSeriesContentTypeManhua(int seriesId) => SetSeriesContentType(seriesId, ContentType.Manhua);
    [RelayCommand] private void SetSeriesContentTypeManhwa(int seriesId) => SetSeriesContentType(seriesId, ContentType.Manhwa);

    /// <summary>
    /// Tile context menu's "Set Status" picker (docs/superpowers/specs/2026-08-18-metadata-model-ui-
    /// gaps-status-and-bookmarks-design.md) - same one-command-per-value shape as
    /// <see cref="SetSeriesContentType"/> above. No <see cref="LoadFromDatabase"/> reload: unlike
    /// Content Type, Library doesn't sort/group/filter by Status (yet), so nothing downstream needs
    /// a refresh from this write alone.
    /// </summary>
    private void SetSeriesStatus(int seriesId, SeriesStatus status)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Find(seriesId);
        if (series is not null)
        {
            series.Status = status;
            context.SaveChanges();
        }
    }

    [RelayCommand] private void SetSeriesStatusUnknown(int seriesId) => SetSeriesStatus(seriesId, SeriesStatus.Unknown);
    [RelayCommand] private void SetSeriesStatusOngoing(int seriesId) => SetSeriesStatus(seriesId, SeriesStatus.Ongoing);
    [RelayCommand] private void SetSeriesStatusCompleted(int seriesId) => SetSeriesStatus(seriesId, SeriesStatus.Completed);
    [RelayCommand] private void SetSeriesStatusCancelled(int seriesId) => SetSeriesStatus(seriesId, SeriesStatus.Cancelled);
    [RelayCommand] private void SetSeriesStatusHiatus(int seriesId) => SetSeriesStatus(seriesId, SeriesStatus.Hiatus);

    private void SetSeriesReadingMode(int seriesId, ReadingMode mode)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Find(seriesId);
        if (series is not null)
        {
            series.ReadingMode = mode;
            context.SaveChanges();
        }
    }

    [RelayCommand] private void SetSeriesReadingModeLeftToRight(int seriesId) => SetSeriesReadingMode(seriesId, ReadingMode.LeftToRight);
    [RelayCommand] private void SetSeriesReadingModeRightToLeft(int seriesId) => SetSeriesReadingMode(seriesId, ReadingMode.RightToLeft);

    /// <summary>
    /// Manual "add a physical book" entry point (docs/superpowers/specs/2026-08-16-reveal-in-
    /// explorer-and-fileless-entries-design.md §2/§3) - creates (or resolves to an existing match
    /// for) a fileless placeholder Issue, then hands off to the Issue Properties editor to fill in
    /// the rest. deleteIfUnedited only applies when a new row was actually created - see
    /// ReadingListMatcher's wasCreated out param.
    /// </summary>
    [RelayCommand]
    private void CreatePlaceholderIssue()
    {
        if (string.IsNullOrWhiteSpace(NewIssueSeriesName) || string.IsNullOrWhiteSpace(NewIssueNumber))
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        string trimmedSeriesName = NewIssueSeriesName.Trim();

        // Real bug, found via manual testing: wasCreated (below) reflects whether the *Issue* was
        // newly created, not the Series - ResolveOrCreatePlaceholder attaches a new placeholder Issue
        // to an EXISTING series (by name) whenever that series just doesn't have this exact issue
        // number yet, which still reports wasCreated=true. Applying the picker's Content Type in that
        // case would silently overwrite a real, already-classified series just because it didn't
        // happen to have issue NewIssueNumber on file - checking series existence *before* the call
        // is the only way to tell "series is genuinely new" apart from "issue is new but its series
        // isn't" (docs/superpowers/specs/2026-08-16-manga-content-type-classification-design.md §3).
        bool seriesIsNew = !context.Series.Any(s => s.Name.ToLower() == trimmedSeriesName.ToLower());

        var issue = ReadingListMatcher.ResolveOrCreatePlaceholder(
            context, trimmedSeriesName, NewIssueNumber.Trim(), null, null, null, out bool wasCreated);

        if (seriesIsNew)
        {
            issue.Series.ContentType = NewIssueContentType;
            if (ShowAddReadingModePicker)
            {
                issue.Series.ReadingMode = NewIssueReadingMode;
            }

            context.SaveChanges();
        }

        ActiveDropdown = null;
        NewIssueSeriesName = string.Empty;
        NewIssueNumber = string.Empty;
        NewIssueContentType = ContentType.Comic;
        NewIssueReadingMode = ReadingMode.RightToLeft;
        _goToNewIssueProperties(issue.Id, issue.SeriesId, wasCreated);
    }

    [ObservableProperty]
    private LibraryViewMode _viewMode = LibraryViewMode.ComfortableGrid;

    public bool IsCompactGrid => ViewMode == LibraryViewMode.CompactGrid;
    public bool IsComfortableGrid => ViewMode == LibraryViewMode.ComfortableGrid;
    public bool IsCoverOnlyGrid => ViewMode == LibraryViewMode.CoverOnlyGrid;
    public bool IsPanoramaGrid => ViewMode == LibraryViewMode.PanoramaGrid;
    public bool IsListView => ViewMode == LibraryViewMode.List;
    public bool IsDetailsView => ViewMode == LibraryViewMode.Details;
    public bool IsTilesView => ViewMode == LibraryViewMode.Tiles;
    public bool IsIssueListView => ViewMode == LibraryViewMode.IssueList;

    partial void OnViewModeChanged(LibraryViewMode value)
    {
        OnPropertyChanged(nameof(IsCompactGrid));
        OnPropertyChanged(nameof(IsComfortableGrid));
        OnPropertyChanged(nameof(IsCoverOnlyGrid));
        OnPropertyChanged(nameof(IsPanoramaGrid));
        OnPropertyChanged(nameof(IsListView));
        OnPropertyChanged(nameof(IsDetailsView));
        OnPropertyChanged(nameof(IsTilesView));
        OnPropertyChanged(nameof(IsIssueListView));
        OnPropertyChanged(nameof(DisplayModeLabel));
        SaveLibrarySettings();
    }

    /// <summary>Every Display mode now renders the exact same <see cref="IssueList"/> rows/groups
    /// (docs/superpowers/specs/2026-08-18-library-book-centric-redesign-design.md Slice 3) - a mode
    /// switch is a pure XAML visibility flip, no reload needed.</summary>
    [RelayCommand]
    private void SetViewMode(LibraryViewMode mode) => ViewMode = mode;

    public string DisplayModeLabel => ViewMode switch
    {
        LibraryViewMode.CompactGrid => "Compact grid",
        LibraryViewMode.ComfortableGrid => "Comfortable grid",
        LibraryViewMode.CoverOnlyGrid => "Cover-only grid",
        LibraryViewMode.PanoramaGrid => "Panorama grid",
        LibraryViewMode.List => "List",
        LibraryViewMode.Details => "Details",
        LibraryViewMode.Tiles => "Tiles",
        LibraryViewMode.IssueList => "Comic List",
        _ => "Display",
    };

    private double _gridDensity = 1.0;

    /// <summary>
    /// Width/height multiplier for the fixed-box grid-family modes (Compact/Comfortable/Cover-only/
    /// Tiles), replacing the toolbar's previously-fake "Grid density" slider (docs/superpowers/specs/
    /// 2026-08-09-library-toolbar-design.md Phase A). Panorama grid is deliberately exempt - its
    /// width is already computed per-cover from the real aspect ratio (see
    /// <see cref="SeriesCardSample.PanoramaWidth"/>); layering a second independent multiplier on
    /// top would need re-deriving that value against a changing height, which isn't worth the
    /// complexity for what's otherwise a density knob for the fixed-box modes.
    /// </summary>
    public double GridDensity
    {
        get => _gridDensity;
        set
        {
            double clamped = Math.Clamp(value, 0.6, 1.6);
            if (SetProperty(ref _gridDensity, clamped))
            {
                OnPropertyChanged(nameof(CompactCardWidth));
                OnPropertyChanged(nameof(CompactCardHeight));
                OnPropertyChanged(nameof(ComfortableCardWidth));
                OnPropertyChanged(nameof(ComfortableCardHeight));
                OnPropertyChanged(nameof(CoverOnlyCardWidth));
                OnPropertyChanged(nameof(CoverOnlyCardHeight));
                OnPropertyChanged(nameof(TilesThumbWidth));
                OnPropertyChanged(nameof(TilesThumbHeight));
                OnPropertyChanged(nameof(TilesCardWidth));
                SaveLibrarySettings();
            }
        }
    }

    public double CompactCardWidth => 110 * GridDensity;
    public double CompactCardHeight => 160 * GridDensity;
    public double ComfortableCardWidth => 150 * GridDensity;
    public double ComfortableCardHeight => 216 * GridDensity;
    public double CoverOnlyCardWidth => 150 * GridDensity;
    public double CoverOnlyCardHeight => 216 * GridDensity;
    public double TilesThumbWidth => 48 * GridDensity;
    public double TilesThumbHeight => 68 * GridDensity;
    public double TilesCardWidth => 260 * GridDensity;

    /// <summary>Panorama grid's fixed tile height - XAML binds here rather than a hardcoded literal, so this and <see cref="SeriesCardSample.PanoramaWidth"/>'s own height math can't drift apart.</summary>
    public double PanoramaCardHeight => SeriesCardSample.PanoramaHeight;

    /// <summary>Overlay toggles (docs/superpowers/specs/2026-08-09-library-toolbar-design.md Phase D), persisted per docs/superpowers/specs/2026-08-17-library-saved-list-layouts-design.md.
    /// See <see cref="ShowContinueReadingButton"/> below for why that one toggle is scoped to
    /// series granularity only, unlike these badge toggles which apply everywhere.</summary>
    [ObservableProperty]
    private bool _showUnreadBadge = true;

    partial void OnShowUnreadBadgeChanged(bool value) => SaveLibrarySettings();

    [ObservableProperty]
    private bool _showPublisherBadge;

    partial void OnShowPublisherBadgeChanged(bool value) => SaveLibrarySettings();

    [ObservableProperty]
    private bool _showLanguageBadge;

    partial void OnShowLanguageBadgeChanged(bool value) => SaveLibrarySettings();

    [ObservableProperty]
    private bool _useLanguageIcon;

    partial void OnUseLanguageIconChanged(bool value) => SaveLibrarySettings();

    /// <summary>Series-card-only overlay button: once <see cref="IsIssueGranularity"/>, clicking a
    /// tile itself IS "continue reading it" (<see cref="IssueListScreenViewModel.OpenIssueCommand"/>),
    /// so a separate button that jumps to a *different*, unread issue only makes sense when a card
    /// aggregates a whole series (<see cref="IsSeriesGranularity"/>).</summary>
    [ObservableProperty]
    private bool _showContinueReadingButton;

    partial void OnShowContinueReadingButtonChanged(bool value) => SaveLibrarySettings();

    [RelayCommand]
    private void ContinueReading(SeriesCardSample? card)
    {
        if (card?.ContinueReadingIssueId is int issueId)
        {
            _goReaderForIssue(issueId);
        }
    }
}
