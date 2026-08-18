using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
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

    public LibraryScreenViewModel(Action<int> goDetail, Action<int> goReaderForIssue, Action<int, int, bool> goToNewIssueProperties)
    {
        _goDetail = goDetail;
        _goReaderForIssue = goReaderForIssue;
        _goToNewIssueProperties = goToNewIssueProperties;
        Covers = new ObservableCollection<SeriesCardSample>();
        ContentTypes = new ObservableCollection<ContentTypeSummary>();
        Collections = new ObservableCollection<CategorySummary>();
        Groups = new ObservableCollection<SeriesCardGroup>();
        ExistingSeriesNames = new ObservableCollection<string>();
        LoadLibrarySettings();
        LoadFromDatabase();
    }

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
    }

    /// <summary>Immediate write-back for every field <see cref="LoadLibrarySettings"/> seeds, called from each field's own change hook - no debounce, matching this ViewModel's existing no-debounce philosophy (see <see cref="SearchQuery"/>'s doc comment) and <see cref="Paperbunkr.App.ViewModels.ReaderScreenViewModel"/>'s equivalent immediate-write precedent for <c>AppSettings</c>.</summary>
    private void SaveLibrarySettings()
    {
        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();

        settings.LibrarySortField = SortField;
        settings.LibrarySortDirection = SortDirection;
        settings.LibraryGroupField = GroupField;
        settings.LibraryViewMode = ViewMode;
        settings.LibraryGridDensity = GridDensity;
        settings.LibraryShowUnreadBadge = ShowUnreadBadge;
        settings.LibraryShowPublisherBadge = ShowPublisherBadge;
        settings.LibraryShowLanguageBadge = ShowLanguageBadge;
        settings.LibraryUseLanguageIcon = UseLanguageIcon;
        settings.LibraryShowContinueReadingButton = ShowContinueReadingButton;
        settings.LibrarySearchQuery = string.IsNullOrEmpty(SearchQuery) ? null : SearchQuery;
        settings.LibraryActiveContentType = _activeContentType;
        settings.LibraryActiveCategoryId = _activeCategoryId;
        settings.LibraryFilterUnreadOnly = FilterUnreadOnly;
        settings.LibraryFilterMissingIssues = FilterMissingIssues;
        settings.LibraryFilterTrackedOnly = FilterTrackedOnly;

        context.SaveChanges();
    }

    public ObservableCollection<SeriesCardSample> Covers { get; }

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

    /// <summary>Populated instead of <see cref="Covers"/> when <see cref="IsGrouped"/> - see <see cref="LoadFromDatabase"/>.</summary>
    public ObservableCollection<SeriesCardGroup> Groups { get; }

    [ObservableProperty]
    private int _allSeriesCount;

    public bool IsAllSeriesActive => _activeContentType is null && _activeCategoryId is null;

    public bool HasCollections => Collections.Count > 0;

    /// <summary>P6 fix (docs/alpha-todo.md) - none of the 7 display modes had a "no series match" empty state; a zero-result search/filter combination just rendered a blank area.</summary>
    public bool HasAnyResults => Covers.Count > 0 || Groups.Count > 0;

    /// <summary>
    /// Reloads everything from the database: the sidebar's <see cref="ContentTypes"/>/
    /// <see cref="Collections"/> summaries (always full, unfiltered counts) and <see cref="Covers"/>
    /// (filtered by whichever of <see cref="_activeContentType"/>/<see cref="_activeCategoryId"/> is
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
            filtered = filtered.Where(s =>
                s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (s.Publisher?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.Genre?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (FilterUnreadOnly)
        {
            filtered = filtered.Where(s => s.Issues.Any(i => i.LastPageRead is null or 0));
        }

        if (FilterMissingIssues)
        {
            filtered = filtered.Where(s => s.Issues.Any(i => i.FileIsMissing));
        }

        if (FilterTrackedOnly)
        {
            filtered = filtered.Where(s => s.TrackingLinks.Count > 0);
        }

        var cards = SortCards(filtered.Select(SeriesCardSample.FromSeries).ToList());

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

    [RelayCommand]
    private void SelectAllSeries()
    {
        _activeContentType = null;
        _activeCategoryId = null;
        SaveLibrarySettings();
        LoadFromDatabase();
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
    }

    /// <summary>
    /// Free-text search across Name/Publisher/Genre (docs/superpowers/specs/
    /// 2026-08-09-library-toolbar-design.md Phase B) - not CE's full all-properties matcher sweep,
    /// scoped to the real text fields worth searching. No debounce: requerying on every keystroke
    /// matches how every other filter in this ViewModel already behaves (sidebar clicks, the 3
    /// checkboxes below), and library sizes here are small enough that a plain SQLite query is
    /// fast regardless.
    /// </summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    partial void OnSearchQueryChanged(string value)
    {
        SaveLibrarySettings();
        LoadFromDatabase();
    }

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

    [ObservableProperty]
    private LibrarySortField _sortField = LibrarySortField.DateAdded;

    partial void OnSortFieldChanged(LibrarySortField value)
    {
        OnPropertyChanged(nameof(ShowAlphabetIndex));
        OnPropertyChanged(nameof(SortLabel));
        SaveLibrarySettings();
        LoadFromDatabase();
    }

    [ObservableProperty]
    private SortDirection _sortDirection = SortDirection.Descending;

    partial void OnSortDirectionChanged(SortDirection value)
    {
        OnPropertyChanged(nameof(SortLabel));
        SaveLibrarySettings();
        LoadFromDatabase();
    }

    [RelayCommand]
    private void ToggleSortDirection() =>
        SortDirection = SortDirection == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;

    public string SortLabel =>
        (LibraryFieldCatalog.SortFields.TryGetValue(SortField, out var descriptor) ? descriptor.DisplayName : "Sort")
        + (SortDirection == SortDirection.Ascending ? " ↑" : " ↓");

    [ObservableProperty]
    private LibraryGroupField _groupField = LibraryGroupField.None;

    public bool IsGrouped => GroupField != LibraryGroupField.None;

    partial void OnGroupFieldChanged(LibraryGroupField value)
    {
        OnPropertyChanged(nameof(IsGrouped));
        OnPropertyChanged(nameof(ShowAlphabetIndex));
        SaveLibrarySettings();
        LoadFromDatabase();
    }

    /// <summary>A-Z indexer only means something against an alphabetically-ordered, ungrouped flat list (docs/superpowers/specs/2026-08-09-library-toolbar-design.md Phase C).</summary>
    public bool ShowAlphabetIndex => SortField == LibrarySortField.Name && !IsGrouped;

    [ObservableProperty]
    private string? _activeDropdown;

    public bool IsFilterOpen => ActiveDropdown == "filter";
    public bool IsSortOpen => ActiveDropdown == "sort";
    public bool IsGroupOpen => ActiveDropdown == "group";
    public bool IsDisplayOpen => ActiveDropdown == "display";
    public bool IsAddOpen => ActiveDropdown == "add";

    partial void OnActiveDropdownChanged(string? value)
    {
        OnPropertyChanged(nameof(IsFilterOpen));
        OnPropertyChanged(nameof(IsSortOpen));
        OnPropertyChanged(nameof(IsGroupOpen));
        OnPropertyChanged(nameof(IsDisplayOpen));
        OnPropertyChanged(nameof(IsAddOpen));
    }

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

    /// <summary>docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md §1 - a series has no single file, so this opens its first issue's folder rather than selecting one.</summary>
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

    /// <summary>
    /// Series-card "Set Content Type" picker (docs/superpowers/specs/2026-08-16-manga-content-type-
    /// classification-design.md §2) - one small command per fixed value (Avalonia's CommandParameter
    /// only carries a single value, and this needs both "which card" and "which value"; matches
    /// this codebase's existing per-value-command shape, e.g. SetSortField/SetGroupField, rather
    /// than a tuple/multi-binding CommandParameter, which has no clean XAML story here) each taking
    /// the right-clicked card as CommandParameter, same as the existing RevealSeriesCommand.
    /// </summary>
    private void SetSeriesContentType(SeriesCardSample card, ContentType type)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Find(card.SeriesId);
        if (series is not null)
        {
            series.ContentType = type;
            context.SaveChanges();
            LoadFromDatabase();
        }
    }

    [RelayCommand] private void SetSeriesContentTypeComic(SeriesCardSample card) => SetSeriesContentType(card, ContentType.Comic);
    [RelayCommand] private void SetSeriesContentTypeManga(SeriesCardSample card) => SetSeriesContentType(card, ContentType.Manga);
    [RelayCommand] private void SetSeriesContentTypeManhua(SeriesCardSample card) => SetSeriesContentType(card, ContentType.Manhua);
    [RelayCommand] private void SetSeriesContentTypeManhwa(SeriesCardSample card) => SetSeriesContentType(card, ContentType.Manhwa);

    private void SetSeriesReadingMode(SeriesCardSample card, ReadingMode mode)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Find(card.SeriesId);
        if (series is not null)
        {
            series.ReadingMode = mode;
            context.SaveChanges();
        }
    }

    [RelayCommand] private void SetSeriesReadingModeLeftToRight(SeriesCardSample card) => SetSeriesReadingMode(card, ReadingMode.LeftToRight);
    [RelayCommand] private void SetSeriesReadingModeRightToLeft(SeriesCardSample card) => SetSeriesReadingMode(card, ReadingMode.RightToLeft);

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

    [RelayCommand]
    private void SetSortField(LibrarySortField field) => SortField = field;

    [RelayCommand]
    private void SetGroupField(LibraryGroupField field) => GroupField = field;

    [ObservableProperty]
    private LibraryViewMode _viewMode = LibraryViewMode.ComfortableGrid;

    public bool IsCompactGrid => ViewMode == LibraryViewMode.CompactGrid;
    public bool IsComfortableGrid => ViewMode == LibraryViewMode.ComfortableGrid;
    public bool IsCoverOnlyGrid => ViewMode == LibraryViewMode.CoverOnlyGrid;
    public bool IsPanoramaGrid => ViewMode == LibraryViewMode.PanoramaGrid;
    public bool IsListView => ViewMode == LibraryViewMode.List;
    public bool IsDetailsView => ViewMode == LibraryViewMode.Details;
    public bool IsTilesView => ViewMode == LibraryViewMode.Tiles;

    partial void OnViewModeChanged(LibraryViewMode value)
    {
        OnPropertyChanged(nameof(IsCompactGrid));
        OnPropertyChanged(nameof(IsComfortableGrid));
        OnPropertyChanged(nameof(IsCoverOnlyGrid));
        OnPropertyChanged(nameof(IsPanoramaGrid));
        OnPropertyChanged(nameof(IsListView));
        OnPropertyChanged(nameof(IsDetailsView));
        OnPropertyChanged(nameof(IsTilesView));
        OnPropertyChanged(nameof(DisplayModeLabel));
        SaveLibrarySettings();
    }

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

    [RelayCommand]
    private void SelectCard(SeriesCardSample? card)
    {
        if (card is not null)
        {
            _goDetail(card.SeriesId);
        }
    }

    /// <summary>Overlay toggles (docs/superpowers/specs/2026-08-09-library-toolbar-design.md Phase D), persisted per docs/superpowers/specs/2026-08-17-library-saved-list-layouts-design.md.</summary>
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
