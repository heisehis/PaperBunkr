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
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.ViewModels;

/// <summary>Which tab Library's "View &amp; Sort" popup shows (docs/superpowers/specs/2026-08-27-
/// library-browsing-4b-toolbar-rework-design.md §4). Top-level so XAML <c>x:Static</c> can name it.</summary>
public enum ViewSortTab { View, Sort, Group }

/// <summary>
/// Library grid + toolbar, ported from LibraryScreen.dc.html (Claude Design project 43c40b25),
/// "pills" toolbar variant (the default selected in the parent "Paperbunkr App" wireframe).
/// Loads real Series records from <see cref="PaperbunkrDb"/> (docs/onboarding.md §5-6) rather
/// than the hardcoded sample data this originally shipped with.
/// </summary>
public partial class LibraryScreenViewModel : ViewModelBase, IContextMenuProvider
{
    /// <summary>Right-click menus for the Library grids (docs/superpowers/specs/2026-08-29-context-
    /// menu-rebuild-design.md). Delegated to keep the menu tree out of this already-large file;
    /// consumed by <see cref="Controls.ContextMenuHost"/> attached to the screen root.</summary>
    IReadOnlyList<ContextMenuEntry>? IContextMenuProvider.BuildContextMenu(object? target) =>
        new LibraryContextMenuBuilder(this).Build(target);

    private readonly Action<int> _goDetail;
    private readonly Action<int> _goReaderForIssue;
    private readonly Action<int, int, bool> _goToNewIssueProperties;
    private readonly Action<int> _onQuickRate;
    private readonly Action<int> _goIssueProperties;
    private readonly Action<IReadOnlyList<int>> _goBulkIssueProperties;
    private readonly Action<IReadOnlyList<int>> _goBulkSeriesProperties;
    private readonly Action<string, string> _showToast;
    private readonly Action _goLibraryFolders;

    /// <summary>
    /// Multi-selection (docs/superpowers/specs/2026-08-24-library-multiselect-slice1-design.md) for
    /// Library's issue-granularity grids - shared with <c>DetailTabsViewModel</c>'s own selection via
    /// <see cref="TileSelectionController{TCard}"/>, since both screens want the identical toggle/
    /// shift-range shape. Series-granularity selection is explicitly out of scope for this slice.
    /// </summary>
    public TileSelectionController<IssueListRow> Selection { get; } = new();

    /// <summary>
    /// Series-granularity counterpart to <see cref="Selection"/> (docs/superpowers/specs/2026-08-24-
    /// library-multiselect-slice3-design.md) - a separate controller/id-space rather than reusing
    /// <see cref="Selection"/>, since <see cref="IssueListRow.Id"/> and
    /// <see cref="SeriesCardSample.SeriesId"/> are different id spaces and the two granularities'
    /// card templates never render at the same time (<see cref="IsSeriesGranularity"/> gates which
    /// one is visible).
    /// </summary>
    public TileSelectionController<SeriesCardSample> SeriesSelection { get; } = new();

    private PluginHostService? _pluginHost;

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

    /// <summary>
    /// In-memory library snapshot, refreshed only by <see cref="LoadFromDatabase"/> (nav into
    /// Library, a mutation command, a folder scan). Search / sort / group / filter changes rebuild
    /// the view from these via <see cref="RebuildView"/> without a database round-trip - at 2000+
    /// series, re-materializing the whole library on every search keystroke froze the UI.
    /// </summary>
    private List<Series> _allSeries = new();
    private List<Category> _allCategories = new();

    public LibraryScreenViewModel(
        Action<int> goDetail,
        Action<int> goReaderForIssue,
        Action<int, int, bool> goToNewIssueProperties,
        Action<int>? onQuickRate = null,
        Action<int>? goIssueProperties = null,
        Action<IReadOnlyList<int>>? goBulkIssueProperties = null,
        Action<string, string>? showToast = null,
        Action<IReadOnlyList<int>>? goBulkSeriesProperties = null,
        Action? goLibraryFolders = null)
    {
        _goDetail = goDetail;
        _goReaderForIssue = goReaderForIssue;
        _goToNewIssueProperties = goToNewIssueProperties;
        _onQuickRate = onQuickRate ?? (_ => { });
        _goIssueProperties = goIssueProperties ?? (_ => { });
        _goBulkIssueProperties = goBulkIssueProperties ?? (_ => { });
        _showToast = showToast ?? ((_, _) => { });
        _goBulkSeriesProperties = goBulkSeriesProperties ?? (_ => { });
        _goLibraryFolders = goLibraryFolders ?? (() => { });
        Covers = new ObservableCollection<SeriesCardSample>();
        Groups = new ObservableCollection<SeriesCardGroup>();
        ContentTypes = new ObservableCollection<ContentTypeSummary>();
        Collections = new ObservableCollection<CategorySummary>();
        ExistingSeriesNames = new ObservableCollection<string>();
        ReadingLists = new ObservableCollection<ReadingListOption>();
        DetailsColumns = new ObservableCollection<DetailsColumn>();
        IssueList = new IssueListScreenViewModel(goReaderForIssue, isSelected: Selection.IsSelected);
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

            if (e.PropertyName is nameof(IssueListScreenViewModel.SortField)
                or nameof(IssueListScreenViewModel.SortDirection)
                or nameof(IssueListScreenViewModel.GroupField)
                or nameof(IssueListScreenViewModel.IsGrouped)
                or nameof(IssueListScreenViewModel.HasAnyResults))
            {
                RaiseChipAndEmptyState();
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
        InitializeDetailsColumns();

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
            RebuildView();
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
        _showTileTitles = settings.LibraryShowTileTitles;
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
        _detailsColumnsSetting = settings.LibraryDetailsColumns;
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
        settings.LibraryShowTileTitles = ShowTileTitles;
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
        // DetailsColumns is populated just after LoadLibrarySettings(); until then (the IssueList
        // seeding relay can fire SaveLibrarySettings mid-construction) keep the stored value as-is
        // rather than clobbering a real customization with an empty string.
        settings.LibraryDetailsColumns = DetailsColumns.Count == 0 ? _detailsColumnsSetting : SerializeDetailsColumns();

        context.SaveChanges();
    }

    // --- Configurable Details table columns (docs/superpowers/specs/2026-08-27-library-browsing-4b-
    // toolbar-rework-design.md §8) - Issue granularity only; Series keeps a fixed template. ---

    private string? _detailsColumnsSetting;

    /// <summary>
    /// The Details table's columns, in display order. Every <see cref="IssueListFieldCatalog.ColumnFields"/>
    /// descriptor is present; <see cref="DetailsColumn.IsVisible"/> marks which ones render. The
    /// stored setting only records the visible set (in order); any remaining fields are appended
    /// hidden so the right-click header menu can offer them.
    /// </summary>
    public ObservableCollection<DetailsColumn> DetailsColumns { get; }

    /// <summary>Raised when a column's visibility changes - the view rebuilds its generated column
    /// grid in response (a hand-rolled table, no <c>DataGrid</c>).</summary>
    public event EventHandler? DetailsColumnsChanged;

    private void InitializeDetailsColumns()
    {
        var visibleFields = ParseDetailsColumnsSetting(_detailsColumnsSetting);
        var visibleSet = visibleFields.ToHashSet();

        // Visible columns first, in stored order; then every other column-eligible field, hidden.
        foreach (var field in visibleFields)
        {
            AddDetailsColumn(field, isVisible: true);
        }

        foreach (var descriptor in IssueListFieldCatalog.ColumnFields)
        {
            if (!visibleSet.Contains(descriptor.Field))
            {
                AddDetailsColumn(descriptor.Field, isVisible: false);
            }
        }
    }

    private static readonly HashSet<IssueListSortField> WideDetailsColumns = new()
    {
        IssueListSortField.Title, IssueListSortField.Series, IssueListSortField.FilePath,
        IssueListSortField.FileDirectory, IssueListSortField.FileName, IssueListSortField.Characters,
        IssueListSortField.Teams, IssueListSortField.Locations,
    };

    private void AddDetailsColumn(IssueListSortField field, bool isVisible)
    {
        if (!IssueListFieldCatalog.SortFields.TryGetValue(field, out var descriptor) || descriptor.Display is null)
        {
            return;
        }

        var column = new DetailsColumn
        {
            Field = field,
            DisplayName = descriptor.DisplayName,
            IsVisible = isVisible,
            Width = WideDetailsColumns.Contains(field) ? 220 : 150,
        };
        column.PropertyChanged += OnDetailsColumnChanged;
        DetailsColumns.Add(column);
    }

    private void OnDetailsColumnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DetailsColumn.IsVisible))
        {
            SaveLibrarySettings();
            DetailsColumnsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Parses the comma-joined enum-name list; unknown / non-column-eligible names are
    /// skipped, and a null/empty/all-invalid setting falls back to the curated default set.</summary>
    private static List<IssueListSortField> ParseDetailsColumnsSetting(string? setting)
    {
        var result = new List<IssueListSortField>();
        if (!string.IsNullOrWhiteSpace(setting))
        {
            var eligible = IssueListFieldCatalog.ColumnFields.Select(d => d.Field).ToHashSet();
            foreach (var token in setting.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Enum.TryParse<IssueListSortField>(token, out var field) && eligible.Contains(field) && !result.Contains(field))
                {
                    result.Add(field);
                }
            }
        }

        return result.Count > 0 ? result : IssueListFieldCatalog.DefaultDetailsColumns.ToList();
    }

    private string SerializeDetailsColumns() =>
        string.Join(",", DetailsColumns.Where(c => c.IsVisible).Select(c => c.Field.ToString()));

    /// <summary>
    /// A Details-table header click: sort by that field, or - if it's already the active sort field -
    /// flip the direction. Writes through <see cref="IssueList"/>'s existing sort pipeline, so the
    /// toolbar's <c>Sorted:</c> chip and the shared row rendering both reflect it.
    /// </summary>
    [RelayCommand]
    private void SetDetailsSort(IssueListSortField field)
    {
        if (IssueList.SortField == field)
        {
            IssueList.ToggleSortDirectionCommand.Execute(null);
        }
        else
        {
            IssueList.SetSortFieldCommand.Execute(field);
        }
    }

    /// <summary>Typeahead source for the "Add a physical book" flyout (docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md §2).</summary>
    public ObservableCollection<string> ExistingSeriesNames { get; }

    /// <summary>Backs the action bar's "Add to Reading List" flyout (docs/superpowers/specs/
    /// 2026-08-24-library-multiselect-slice2-design.md §2) - refreshed on every <see cref="LoadFromDatabase"/>.</summary>
    public ObservableCollection<ReadingListOption> ReadingLists { get; }

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
        RaiseChipAndEmptyState();
        SaveLibrarySettings();
        RebuildView();
    }

    [ObservableProperty]
    private SortDirection _sortDirection = SortDirection.Descending;

    partial void OnSortDirectionChanged(SortDirection value)
    {
        OnPropertyChanged(nameof(SortLabel));
        OnPropertyChanged(nameof(ActiveSortLabel));
        RaiseChipAndEmptyState();
        SaveLibrarySettings();
        RebuildView();
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
        RaiseChipAndEmptyState();
        SaveLibrarySettings();
        RebuildView();
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
        RaiseChipAndEmptyState();
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
    /// Re-reads the library into the <see cref="_allSeries"/>/<see cref="_allCategories"/> snapshot
    /// (plus <see cref="ReadingLists"/>), then hands off to <see cref="RebuildView"/> for all the
    /// derived collections. Call this only when the underlying data can have changed - nav into
    /// Library, a mutation command, a folder scan. A view-only change (search / sort / group /
    /// filter / sidebar selection) calls <see cref="RebuildView"/> directly instead, off the cached
    /// snapshot, so it never round-trips the database.
    /// </summary>
    public void LoadFromDatabase()
    {
        using var context = PaperbunkrDb.CreateContext();
        // Include(Tags) - MatchesSearch and IssueListRow both read Issue.JoinedGenre()/JoinedTags()
        // (docs/superpowers/specs/2026-08-23-weighted-categorized-tags-design.md); without it every
        // issue would look like it has no Genre/Tags at all, breaking Library search and the Comic
        // List's Genre/Tags columns for everyone.
        // AsNoTracking + AsSplitQuery: this is a pure read (the context is disposed at method end,
        // nothing here mutates a loaded entity), and at 2000+ series a single query carrying four
        // collection includes multiplies out to millions of rows for EF to de-duplicate and track
        // on the UI thread. Split query = one linear query per include level; no-tracking drops the
        // identity map and change-snapshot cost.
        _allSeries = context.Series
            .Include(s => s.Issues).ThenInclude(i => i.Tags)
            .Include(s => s.Categories)
            .Include(s => s.TrackingLinks)
            .Include(s => s.Titles)
            .AsNoTracking()
            .AsSplitQuery()
            .OrderBy(s => s.SortName ?? s.Name)
            .ToList();

        _allCategories = context.Categories.Include(c => c.Series).AsNoTracking().AsSplitQuery().OrderBy(c => c.SortOrder).ToList();

        // "Add to Reading List" flyout (docs/superpowers/specs/2026-08-24-library-multiselect-
        // slice2-design.md §2) - same ordering ReadingScreenViewModel's own sidebar uses. Its own
        // table, so it only refreshes with a real reload, not on every RebuildView.
        ReadingLists.Clear();
        foreach (var list in context.ReadingLists.OrderBy(l => l.SortOrder))
        {
            ReadingLists.Add(new ReadingListOption { Id = list.Id, Name = list.Name });
        }

        RebuildView();
    }

    /// <summary>
    /// Rebuilds every derived collection - sidebar <see cref="ContentTypes"/>/<see cref="Collections"/>
    /// summaries, the filtered/sorted/grouped <see cref="Covers"/>/<see cref="Groups"/>, and
    /// <see cref="IssueList"/>'s rows - from the in-memory <see cref="_allSeries"/>/
    /// <see cref="_allCategories"/> snapshot, with no database round-trip. Search / sort / group /
    /// filter / sidebar-selection changes call this; only an actual data change (nav into Library, a
    /// mutation command, a folder scan) re-runs <see cref="LoadFromDatabase"/>. Both granularities
    /// are still always computed regardless of which is displayed (see the constructor's doc comment).
    /// </summary>
    private void RebuildView()
    {
        var series = _allSeries;

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

        Collections.Clear();
        foreach (var category in _allCategories)
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
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyStateMessage));
        OnPropertyChanged(nameof(EmptyStateActionLabel));
        OnPropertyChanged(nameof(EmptyStateActionCommand));
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
        // Series-level checks (s.Name/Titles/Publisher/Genre) stay hand-written - they have no
        // per-Issue equivalent. Contains(s.Titles, query) has no CE equivalent (CE never modeled
        // alternate titles at all) - a deliberate new-Paperbunkr addition to both Series and All
        // modes, same "deliberate new feature, not parity" footing as ReadingStatus.
        //
        // Every per-Issue field list is delegated to SearchFieldBundleCatalog (docs/superpowers/
        // specs/2026-08-28-smartlist-engine-v2-design.md §4), the single shared definition the Smart
        // Lists AllProperties field also uses. Behaviour-identical to the pre-extraction inline
        // switch - guarded by SearchFieldBundleCatalogParityTests.
        SearchMode.Series => Contains(s.Name, query) || ContainsAnyTitle(s, query) || IssuesMatch(s, SearchMode.Series, query),
        SearchMode.Writer => IssuesMatch(s, SearchMode.Writer, query),
        SearchMode.Artists => IssuesMatch(s, SearchMode.Artists, query),
        SearchMode.Descriptive => IssuesMatch(s, SearchMode.Descriptive, query),
        SearchMode.File => IssuesMatch(s, SearchMode.File, query),
        SearchMode.Catalog => IssuesMatch(s, SearchMode.Catalog, query),
        _ => Contains(s.Name, query) || ContainsAnyTitle(s, query) || Contains(s.Publisher, query)
            || Contains(s.Genre, query) || IssuesMatch(s, SearchMode.All, query),
    };

    private static bool IssuesMatch(Series s, SearchMode mode, string query) =>
        s.Issues.Any(i => SearchFieldBundleCatalog.IssueFieldSelectors[mode](i).Any(v => Contains(v, query)));

    private static bool Contains(string? value, string query) =>
        !string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>Matches native/romanized/localized alternate titles (docs/superpowers/specs/2026-08-19-metadata-model-multi-value-titles-design.md) - lets e.g. a native-script search find a series whose primary <see cref="Series.Name"/> is the localized title, or vice versa.</summary>
    private static bool ContainsAnyTitle(Series s, string query) => s.Titles.Any(t => Contains(t.Value, query));

    [RelayCommand]
    private void SelectAllSeries()
    {
        _activeContentType = null;
        _activeCategoryId = null;
        SaveLibrarySettings();
        RebuildView();
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
        RebuildView();
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
        RebuildView();
        PushBrowseHistory();
    }

    /// <summary>
    /// Free-text search, scoped to <see cref="SearchMode"/>'s field set (docs/superpowers/specs/
    /// 2026-08-09-library-toolbar-design.md Phase B, extended per CE's real QuickSearch mode
    /// dropdown - see <see cref="SearchMode"/>'s doc comment). No debounce: every keystroke rebuilds
    /// the view via <see cref="RebuildView"/>, which filters the already-in-memory
    /// <see cref="_allSeries"/> snapshot - no database round-trip, so it stays cheap even on a
    /// 2000+ series library (it used to re-query and re-materialize the whole library per keystroke).
    /// </summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    partial void OnSearchQueryChanged(string value)
    {
        RaiseChipAndEmptyState();
        SaveLibrarySettings();
        RebuildView();

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
        RaiseChipAndEmptyState();
        SaveLibrarySettings();
        RebuildView();
    }

    [RelayCommand]
    private void SetSearchMode(SearchMode mode) => SearchMode = mode;

    [ObservableProperty]
    private bool _filterUnreadOnly;

    partial void OnFilterUnreadOnlyChanged(bool value)
    {
        RaiseChipAndEmptyState();
        SaveLibrarySettings();
        RebuildView();
    }

    [ObservableProperty]
    private bool _filterMissingIssues;

    partial void OnFilterMissingIssuesChanged(bool value)
    {
        RaiseChipAndEmptyState();
        SaveLibrarySettings();
        RebuildView();
    }

    [ObservableProperty]
    private bool _filterTrackedOnly;

    partial void OnFilterTrackedOnlyChanged(bool value)
    {
        RaiseChipAndEmptyState();
        SaveLibrarySettings();
        RebuildView();
    }

    /// <summary>A-Z indexer only means something against an alphabetically-ordered, ungrouped flat
    /// list (docs/superpowers/specs/2026-08-09-library-toolbar-design.md Phase C) - now reads
    /// <see cref="IssueList"/>'s sort/group state directly since that's the only one left (see the
    /// constructor's relay for how this stays live).</summary>
    public bool ShowAlphabetIndex => IsSeriesGranularity
        ? SortField == LibrarySortField.Name && !IsGrouped
        : IssueList.SortField == IssueListSortField.Series && !IssueList.IsGrouped;

    // --- Toolbar chrome (docs/superpowers/specs/2026-08-27-library-browsing-4b-toolbar-rework-
    // design.md §2-§5) - one "View & Sort" tabbed popup replacing the old Filter/Sort/Group/Display
    // pills, plus a chips row carrying the live filter/sort/group state. ---

    [ObservableProperty]
    private string? _activeDropdown;

    public bool IsViewSortOpen => ActiveDropdown == "viewSort";
    public bool IsSearchModeOpen => ActiveDropdown == "searchMode";

    /// <summary>The "+ Add filter" chip's small popup (the old Filter popup's checkbox content).</summary>
    public bool IsAddFilterOpen => ActiveDropdown == "addFilter";

    /// <summary>Selection action bar's "Add to List" flyout (docs/superpowers/specs/2026-08-24-
    /// library-multiselect-slice2-design.md §2) - same single-active-dropdown mechanism.</summary>
    public bool IsAddToListOpen => ActiveDropdown == "addToList";

    partial void OnActiveDropdownChanged(string? value)
    {
        OnPropertyChanged(nameof(IsViewSortOpen));
        OnPropertyChanged(nameof(IsSearchModeOpen));
        OnPropertyChanged(nameof(IsAddFilterOpen));
        OnPropertyChanged(nameof(IsAddToListOpen));
    }

    [ObservableProperty]
    private ViewSortTab _viewSortActiveTab = ViewSortTab.View;

    public bool IsViewTabActive => ViewSortActiveTab == ViewSortTab.View;
    public bool IsSortTabActive => ViewSortActiveTab == ViewSortTab.Sort;
    public bool IsGroupTabActive => ViewSortActiveTab == ViewSortTab.Group;

    partial void OnViewSortActiveTabChanged(ViewSortTab value)
    {
        OnPropertyChanged(nameof(IsViewTabActive));
        OnPropertyChanged(nameof(IsSortTabActive));
        OnPropertyChanged(nameof(IsGroupTabActive));
    }

    [RelayCommand]
    private void ToggleViewSort() => ActiveDropdown = ActiveDropdown == "viewSort" ? null : "viewSort";

    /// <summary>Opens the View &amp; Sort popup on a specific tab - the chips row's
    /// <c>Sorted:</c>/<c>Grouped:</c> chips use this to jump straight to the matching tab.</summary>
    [RelayCommand]
    private void OpenViewSortTab(ViewSortTab tab)
    {
        ViewSortActiveTab = tab;
        ActiveDropdown = "viewSort";
    }

    [RelayCommand]
    private void ToggleSearchMode() => ActiveDropdown = ActiveDropdown == "searchMode" ? null : "searchMode";

    [RelayCommand]
    private void ToggleAddFilter() => ActiveDropdown = ActiveDropdown == "addFilter" ? null : "addFilter";

    [RelayCommand]
    private void ToggleAddToList() => ActiveDropdown = ActiveDropdown == "addToList" ? null : "addToList";

    /// <summary>The "+" button - opens the centered Add-issue overlay (design §6), a full
    /// FloatingPanel rather than the old light-dismiss popup.</summary>
    [ObservableProperty]
    private bool _isAddIssueOpen;

    [RelayCommand]
    private void OpenAddIssue()
    {
        NewIssueSeriesName = string.Empty;
        NewIssueNumber = string.Empty;
        NewIssueContentType = ContentType.Comic;
        NewIssueReadingMode = ReadingMode.RightToLeft;
        IsAddIssueOpen = true;
    }

    [RelayCommand]
    private void CloseAddIssue() => IsAddIssueOpen = false;

    // --- Chips row + empty state ---

    public bool HasActiveFilters =>
        FilterUnreadOnly || FilterMissingIssues || FilterTrackedOnly || SearchMode != SearchMode.All;

    private bool SeriesSortIsDefault => SortField == LibrarySortField.DateAdded && SortDirection == SortDirection.Descending;
    private bool IssueSortIsDefault => IssueList.SortField == IssueListSortField.Added && IssueList.SortDirection == SortDirection.Descending;

    public bool IsSortNonDefault => IsSeriesGranularity ? !SeriesSortIsDefault : !IssueSortIsDefault;
    public bool IsGroupNonDefault => IsSeriesGranularity ? IsGrouped : IssueList.IsGrouped;

    public bool HasVisibleChips => HasActiveFilters || IsSortNonDefault || IsGroupNonDefault;

    public bool ShowGroupChip => IsGroupNonDefault;
    public bool ShowSearchScopeChip => SearchMode != SearchMode.All;
    public string SearchScopeChipLabel => SearchModeLabel;

    /// <summary>Re-raises every chip/empty-state computed property - called from every filter/sort/
    /// group/search/granularity change hook (and the IssueList relay in the constructor).</summary>
    private void RaiseChipAndEmptyState()
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(IsSortNonDefault));
        OnPropertyChanged(nameof(IsGroupNonDefault));
        OnPropertyChanged(nameof(HasVisibleChips));
        OnPropertyChanged(nameof(ShowGroupChip));
        OnPropertyChanged(nameof(ShowSearchScopeChip));
        OnPropertyChanged(nameof(SearchScopeChipLabel));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyStateMessage));
        OnPropertyChanged(nameof(EmptyStateActionLabel));
        OnPropertyChanged(nameof(EmptyStateActionCommand));
    }

    /// <summary>Clears the filter toggles and resets the search scope to All; leaves the sidebar
    /// content-type/category filter and the search text alone (design §2).</summary>
    [RelayCommand]
    private void ClearAllFilters()
    {
        FilterUnreadOnly = false;
        FilterMissingIssues = false;
        FilterTrackedOnly = false;
        SearchMode = SearchMode.All;
    }

    [RelayCommand] private void ClearUnreadFilter() => FilterUnreadOnly = false;
    [RelayCommand] private void ClearMissingFilter() => FilterMissingIssues = false;
    [RelayCommand] private void ClearTrackedFilter() => FilterTrackedOnly = false;
    [RelayCommand] private void ClearSearchScope() => SearchMode = SearchMode.All;

    public bool ShowEmptyState => !HasAnyResults;

    public string EmptyStateMessage
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                return $"No results for “{SearchQuery.Trim()}”.";
            }

            return HasActiveFilters ? "No comics match this filter." : "This library is empty.";
        }
    }

    private bool FiltersOrSearchActive => HasActiveFilters || !string.IsNullOrWhiteSpace(SearchQuery);

    public string EmptyStateActionLabel => FiltersOrSearchActive ? "Clear filters" : "Scan folders";

    public IRelayCommand EmptyStateActionCommand =>
        FiltersOrSearchActive ? ClearAllFiltersCommand : OpenLibraryFoldersCommand;

    /// <summary>Empty-state "Scan folders" action - hands off to Preferences → Libraries via the
    /// ctor callback (no-op default keeps the VM standalone-testable).</summary>
    [RelayCommand]
    private void OpenLibraryFolders() => _goLibraryFolders();

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

    /// <summary>Called once from <c>App.axaml.cs</c> after <see cref="PluginHostService.Initialize"/> - the host doesn't exist yet when this ViewModel is constructed in <c>MainViewModel</c>'s own constructor.</summary>
    public void AttachHost(PluginHostService host)
    {
        _pluginHost = host;
        OnPropertyChanged(nameof(HasPluginHost));
    }

    public bool HasPluginHost => _pluginHost is not null;

    /// <summary>
    /// Real Library-hook trigger (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §5) -
    /// the right-clicked tile's context menu. Now that Library has a real multi-selection model
    /// (docs/superpowers/specs/2026-08-24-library-multiselect-slice1-design.md), this dispatches by
    /// selection-union like every other context-menu command (<see cref="EditIssueProperties"/>,
    /// <see cref="RevealIssue"/>): right-clicking a lone unselected tile runs against just that one
    /// issue, right-clicking while other tiles are checked runs against the whole selection. Any
    /// result (e.g. Duplicate Finder's match dialog) is surfaced by the plugin command itself via
    /// <c>IApplication.AskQuestion</c>, not by this method.
    /// </summary>
    [RelayCommand]
    private async Task RunLibraryPlugins(int issueId) => await RunLibraryPluginsOn(Selection.UnionForAction(issueId));

    /// <summary>Action bar counterpart, same as <see cref="BulkEditSelection"/>/<see cref="MarkSelectionRead"/> - runs against the whole current selection with no right-click target involved.</summary>
    [RelayCommand]
    private async Task RunLibraryPluginsOnSelection() => await RunLibraryPluginsOn(Selection.SelectedIds.ToList());

    private async Task RunLibraryPluginsOn(IReadOnlyList<int> issueIds)
    {
        if (_pluginHost is null || issueIds.Count == 0)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var issues = context.Issues.Where(i => issueIds.Contains(i.Id)).ToList();
        if (issues.Count == 0)
        {
            return;
        }

        await _pluginHost.RunLibraryHookAsync(issues);
    }

    /// <summary>Opens the clicked tile's series in Detail (docs/superpowers/specs/
    /// 2026-08-18-library-book-centric-redesign-design.md Slice 3 open question #1) - tiles
    /// themselves now open the Reader directly (<see cref="IssueListScreenViewModel.OpenIssueCommand"/>),
    /// so this is the tile context menu's own entry point to series-level actions/metadata.</summary>
    [RelayCommand]
    private void GoToSeries(int seriesId) => _goDetail(seriesId);

    /// <summary>
    /// Tile context menu's "Mark as Read"/"Mark as Unread" (docs/superpowers/specs/2026-08-23-mark-
    /// as-read-design.md), extended to the selection union in docs/superpowers/specs/2026-08-24-
    /// library-multiselect-slice2-design.md §1 - same union-then-loop shape as
    /// <see cref="DeleteIssueCommand"/> in Slice 1.
    /// </summary>
    [RelayCommand]
    private void MarkIssueRead(int issueId) => MarkIssuesRead(Selection.UnionForAction(issueId));

    /// <summary>Action bar's "Mark as Read" button - marks the whole current selection.</summary>
    [RelayCommand]
    private void MarkSelectionRead() => MarkIssuesRead(Selection.SelectedIds.ToList());

    private void MarkIssuesRead(IReadOnlyList<int> issueIds)
    {
        using var context = PaperbunkrDb.CreateContext();
        int marked = 0;
        foreach (int issueId in issueIds)
        {
            var issue = context.Issues.Find(issueId);
            if (issue is null)
            {
                continue;
            }

            IssueReadStateResolver.MarkAsRead(issue);
            marked++;
        }

        context.SaveChanges();
        LoadFromDatabase();

        if (marked > 1)
        {
            _showToast("Marked as read", $"Marked {marked} issues as read.");
        }
    }

    [RelayCommand]
    private void MarkIssueUnread(int issueId) => MarkIssuesUnread(Selection.UnionForAction(issueId));

    /// <summary>Action bar's "Mark as Unread" button - marks the whole current selection.</summary>
    [RelayCommand]
    private void MarkSelectionUnread() => MarkIssuesUnread(Selection.SelectedIds.ToList());

    private void MarkIssuesUnread(IReadOnlyList<int> issueIds)
    {
        using var context = PaperbunkrDb.CreateContext();
        int marked = 0;
        foreach (int issueId in issueIds)
        {
            var issue = context.Issues.Find(issueId);
            if (issue is null)
            {
                continue;
            }

            IssueReadStateResolver.MarkAsUnread(issue);
            marked++;
        }

        context.SaveChanges();
        LoadFromDatabase();

        if (marked > 1)
        {
            _showToast("Marked as unread", $"Marked {marked} issues as unread.");
        }
    }

    /// <summary>
    /// Action bar's "Add to List" flyout (docs/superpowers/specs/2026-08-24-library-multiselect-
    /// slice2-design.md §2) - adds the whole current selection to an existing reading list, skipping
    /// any issue already a member (no DB-level uniqueness constraint on <see cref="Paperbunkr.Data.Entities.ReadingListItem"/>,
    /// so this is an application-level guard).
    /// </summary>
    [RelayCommand]
    private void AddSelectionToReadingList(int readingListId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var list = context.ReadingLists.Find(readingListId);
        if (list is null)
        {
            return;
        }

        AddIssuesToReadingList(context, list, Selection.SelectedIds.ToList());
        context.SaveChanges();
        ActiveDropdown = null;
    }

    /// <summary>Context-menu "Add to Reading List ▸ &lt;list&gt;". Acts on the right-click union
    /// (current selection ∪ the right-clicked issue), unlike the selection-only action-bar
    /// <see cref="AddSelectionToReadingListCommand"/>. Parameter is <c>(issueId, readingListId)</c>.</summary>
    [RelayCommand]
    private void AddIssueToReadingList((int IssueId, int ReadingListId) target)
    {
        using var context = PaperbunkrDb.CreateContext();
        var list = context.ReadingLists.Find(target.ReadingListId);
        if (list is null)
        {
            return;
        }

        AddIssuesToReadingList(context, list, Selection.UnionForAction(target.IssueId));
        context.SaveChanges();
    }

    /// <summary>Context-menu "Add to Reading List ▸ New List…" - creates a list, then adds the
    /// right-click union to it. Union counterpart of <see cref="CreateReadingListAndAddSelectionCommand"/>.</summary>
    [RelayCommand]
    private void CreateReadingListAndAddIssue(int issueId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var now = DateTime.UtcNow;
        var list = new ReadingList
        {
            Name = "New Reading List",
            SortOrder = context.ReadingLists.Count(),
            Type = ReadingListType.User,
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.ReadingLists.Add(list);
        context.SaveChanges();

        AddIssuesToReadingList(context, list, Selection.UnionForAction(issueId));
        context.SaveChanges();
        LoadFromDatabase();
    }

    /// <summary>"New Reading List…" flyout entry - creates a list the same way
    /// <c>ReadingScreenViewModel.CreateNew</c> does, then immediately adds the current selection to it.</summary>
    [RelayCommand]
    private void CreateReadingListAndAddSelection()
    {
        using var context = PaperbunkrDb.CreateContext();
        var now = DateTime.UtcNow;
        var list = new ReadingList
        {
            Name = "New Reading List",
            SortOrder = context.ReadingLists.Count(),
            Type = ReadingListType.User,
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.ReadingLists.Add(list);
        // list.Id is only assigned once this actually persists - AddIssuesToReadingList needs the
        // real id to set ReadingListItem.ReadingListId, so this must run before that call (a real
        // bug caught by CreateReadingListAndAddSelection_CreatesListAndAddsWholeSelection: without
        // this, the item inserts below violated their FK constraint against an unpersisted Id 0).
        context.SaveChanges();

        AddIssuesToReadingList(context, list, Selection.SelectedIds.ToList());
        context.SaveChanges();
        ActiveDropdown = null;
        LoadFromDatabase();
    }

    private void AddIssuesToReadingList(PaperbunkrDbContext context, ReadingList list, IReadOnlyList<int> issueIds)
    {
        var existingIssueIds = context.ReadingListItems
            .Where(i => i.ReadingListId == list.Id)
            .Select(i => i.IssueId)
            .ToHashSet();

        int nextOrder = context.ReadingListItems.Count(i => i.ReadingListId == list.Id);
        int added = 0;
        int skipped = 0;
        foreach (int issueId in issueIds)
        {
            if (existingIssueIds.Contains(issueId))
            {
                skipped++;
                continue;
            }

            context.ReadingListItems.Add(new ReadingListItem
            {
                ReadingListId = list.Id,
                IssueId = issueId,
                SortOrder = nextOrder++,
            });
            added++;
        }

        string message = (added, skipped) switch
        {
            (0, > 0) => $"All {skipped} already in \"{list.Name}\".",
            (_, 0) => $"Added {added} to \"{list.Name}\".",
            _ => $"Added {added} to \"{list.Name}\" ({skipped} already in list).",
        };
        _showToast("Added to reading list", message);
    }

    /// <summary>Quick Rating + free-text Review in one popup (docs/ce-feature-inventory.md §A) - opens the lightweight overlay instead of the full single-book Issue Properties editor.</summary>
    [RelayCommand]
    private void OpenQuickRate(int issueId) => _onQuickRate(issueId);

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

    /// <summary>
    /// Tile context menu's "Delete Issue" (docs/superpowers/specs/2026-08-22-delete-functionality-
    /// design.md) - a nested-submenu confirm ("Delete Issue" → "Yes, delete") rather than
    /// <c>TwoStepConfirm</c>'s timed re-click: a context menu closes after every click, so there's
    /// no persistently-visible button to re-click within a window: a submenu is the natural
    /// equivalent - deliberate, requires opening a second menu level, not a single misclick away.
    /// Moves the file to the Recycle Bin (confirmed with the user, not a silent permanent delete)
    /// via <see cref="LibraryDeletionHelper"/>, which also removes any reading-list/event
    /// cross-references first (<c>ReadingListItem</c>/<c>EventMembership</c>'s FKs to Issue are
    /// both Restrict, not Cascade).
    /// </summary>
    [RelayCommand]
    private void DeleteIssue(int issueId) => DeleteIssues(Selection.UnionForAction(issueId));

    /// <summary>Action bar's "Delete" button (docs/superpowers/specs/2026-08-24-library-multiselect-
    /// slice1-design.md §6) - deletes the whole current selection, same underlying loop as the
    /// per-tile <see cref="DeleteIssueCommand"/>.</summary>
    [RelayCommand]
    private void DeleteSelection() => DeleteIssues(Selection.SelectedIds.ToList());

    private void DeleteIssues(IReadOnlyList<int> issueIds)
    {
        using var context = PaperbunkrDb.CreateContext();
        foreach (int issueId in issueIds)
        {
            var issue = context.Issues.Find(issueId);
            if (issue is null)
            {
                continue;
            }

            LibraryDeletionHelper.RemoveIssue(context, issue);
        }

        context.SaveChanges();
        Selection.Clear();
        SelectionCount = 0;
        LoadFromDatabase();
    }

    /// <summary>
    /// Tile context menu's new "Edit Properties…" entry (docs/superpowers/specs/2026-08-24-library-
    /// multiselect-slice1-design.md §5) - Library's first issue metadata-edit entry point (previously
    /// only reachable via Detail). Dispatches by selection-union count, same as
    /// <c>DetailTabsViewModel.EditIssueProperties</c>.
    /// </summary>
    [RelayCommand]
    private void EditIssueProperties(int issueId) => OpenIssueEditor(Selection.UnionForAction(issueId));

    /// <summary>Action bar's "Bulk Edit" button - edits the whole current selection.</summary>
    [RelayCommand]
    private void BulkEditSelection() => OpenIssueEditor(Selection.SelectedIds.ToList());

    private void OpenIssueEditor(IReadOnlyList<int> issueIds)
    {
        if (issueIds.Count == 0)
        {
            return;
        }

        if (issueIds.Count == 1)
        {
            _goIssueProperties(issueIds[0]);
        }
        else
        {
            _goBulkIssueProperties(issueIds);
        }
    }

    /// <summary>
    /// Gesture entry point for tile clicks with Ctrl/Shift held, and for the per-tile selection
    /// checkbox (always <paramref name="isShiftHeld"/> false there). Plain clicks never call this -
    /// they keep navigating to Reader/Detail exactly as before this feature existed. See docs/
    /// superpowers/specs/2026-08-24-library-multiselect-slice1-design.md §3.
    /// </summary>
    public void ToggleIssueSelection(IssueListRow row, bool isShiftHeld)
    {
        Selection.Toggle(GetOrderedVisibleIssueRows(), row, isShiftHeld);
        SelectionCount = Selection.Count;
    }

    [RelayCommand]
    private void ToggleIssueSelectionCheckbox(IssueListRow row) => ToggleIssueSelection(row, isShiftHeld: false);

    [RelayCommand]
    private void ClearSelection()
    {
        Selection.Clear(GetOrderedVisibleIssueRows());
        SelectionCount = 0;
    }

    /// <summary>Context menu's "Select All" (issue granularity) - selects every currently displayed row.</summary>
    [RelayCommand]
    private void SelectAllVisibleIssues()
    {
        Selection.SelectAll(GetOrderedVisibleIssueRows());
        SelectionCount = Selection.Count;
    }

    /// <summary>The currently displayed order for shift-range selection - the flattened group order
    /// when grouped (matches what the user actually sees top-to-bottom), the flat row order
    /// otherwise. <see cref="IssueListScreenViewModel.Rows"/>/<see cref="IssueListScreenViewModel.Groups"/>
    /// are mutually exclusive at any given time (see <see cref="IssueListScreenViewModel.Render"/>).</summary>
    private IList<IssueListRow> GetOrderedVisibleIssueRows() =>
        IssueList.IsGrouped
            ? IssueList.Groups.SelectMany(g => g.Items).ToList()
            : IssueList.Rows;

    [ObservableProperty]
    private int _selectionCount;

    public bool HasSelection => SelectionCount > 0;

    /// <summary>Drives the toolbar's normal-controls-vs-action-bar switch across both granularities
    /// (docs/superpowers/specs/2026-08-24-library-multiselect-slice3-design.md) - a plain
    /// <c>!HasSelection</c> binding would leave the normal toolbar visible underneath the series
    /// action bar, since the two selections are tracked independently.</summary>
    public bool HasAnySelection => HasSelection || HasSeriesSelection;

    public string DeleteConfirmLabel => SelectionCount > 1 ? $"Yes, delete {SelectionCount} issues" : "Yes, delete this issue";

    partial void OnSelectionCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasAnySelection));
        OnPropertyChanged(nameof(DeleteConfirmLabel));
    }

    /// <summary>Series-granularity counterpart to <see cref="ToggleIssueSelection"/> - same
    /// checkbox/ctrl-click/shift-click gestures, see docs/superpowers/specs/2026-08-24-library-
    /// multiselect-slice3-design.md.</summary>
    public void ToggleSeriesSelection(SeriesCardSample card, bool isShiftHeld)
    {
        SeriesSelection.Toggle(GetOrderedVisibleSeriesCards(), card, isShiftHeld);
        SeriesSelectionCount = SeriesSelection.Count;
    }

    [RelayCommand]
    private void ToggleSeriesSelectionCheckbox(SeriesCardSample card) => ToggleSeriesSelection(card, isShiftHeld: false);

    [RelayCommand]
    private void ClearSeriesSelection()
    {
        SeriesSelection.Clear(GetOrderedVisibleSeriesCards());
        SeriesSelectionCount = 0;
    }

    /// <summary>Context menu's "Select All" (series granularity) - selects every currently displayed card.</summary>
    [RelayCommand]
    private void SelectAllVisibleSeries()
    {
        SeriesSelection.SelectAll(GetOrderedVisibleSeriesCards());
        SeriesSelectionCount = SeriesSelection.Count;
    }

    /// <summary>The currently displayed order for shift-range selection - the flattened group order
    /// when grouped, the flat card order otherwise. <see cref="Covers"/>/<see cref="Groups"/> are
    /// mutually exclusive at any given time, same rationale as <see cref="GetOrderedVisibleIssueRows"/>.</summary>
    private IList<SeriesCardSample> GetOrderedVisibleSeriesCards() =>
        IsGrouped ? Groups.SelectMany(g => g.Items).ToList() : Covers;

    [ObservableProperty]
    private int _seriesSelectionCount;

    public bool HasSeriesSelection => SeriesSelectionCount > 0;

    public string DeleteSeriesConfirmLabel => SeriesSelectionCount > 1 ? $"Yes, delete {SeriesSelectionCount} series" : "Yes, delete this series";

    partial void OnSeriesSelectionCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSeriesSelection));
        OnPropertyChanged(nameof(HasAnySelection));
        OnPropertyChanged(nameof(DeleteSeriesConfirmLabel));
    }

    /// <summary>Action bar's "Bulk Edit" button (series granularity) - opens
    /// <see cref="BulkSeriesPropertiesScreenViewModel"/> for the whole current selection.</summary>
    [RelayCommand]
    private void BulkEditSeriesSelection() => _goBulkSeriesProperties(SeriesSelection.SelectedIds.ToList());

    /// <summary>Action bar's "Delete" button (series granularity) - deletes every currently selected series.</summary>
    [RelayCommand]
    private void DeleteSeriesSelection() => DeleteSeriesList(SeriesSelection.SelectedIds.ToList());

    /// <summary>Series-tile equivalent of <see cref="DeleteIssueCommand"/> - deletes every issue in the series (each one's file recycled, same as <see cref="DeleteIssueCommand"/>) and the series itself. Extended to the selection union in docs/superpowers/specs/2026-08-24-library-multiselect-slice3-design.md, same union-then-loop shape Slice 1 used for <see cref="DeleteIssueCommand"/>.</summary>
    [RelayCommand]
    private void DeleteSeries(int seriesId) => DeleteSeriesList(SeriesSelection.UnionForAction(seriesId));

    private void DeleteSeriesList(IReadOnlyList<int> seriesIds)
    {
        using var context = PaperbunkrDb.CreateContext();
        foreach (int seriesId in seriesIds)
        {
            var series = context.Series.Include(s => s.Issues).FirstOrDefault(s => s.Id == seriesId);
            if (series is null)
            {
                continue;
            }

            LibraryDeletionHelper.RemoveSeries(context, series);
        }

        context.SaveChanges();
        LoadFromDatabase();
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

    /// <summary>
    /// Tile context menu's "Set Reading Status" picker (docs/superpowers/specs/2026-08-19-metadata-
    /// model-reading-status-design.md) - same one-command-per-value shape as
    /// <see cref="SetSeriesStatus"/> above, and the user's own reading-progress relationship with the
    /// series rather than the publisher's release status. No <see cref="LoadFromDatabase"/> reload,
    /// same reasoning as <see cref="SetSeriesStatus"/>.
    /// </summary>
    private void SetSeriesReadingStatus(int seriesId, ReadingStatus status)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Find(seriesId);
        if (series is not null)
        {
            series.ReadingStatus = status;
            context.SaveChanges();
        }
    }

    [RelayCommand] private void SetSeriesReadingStatusUnknown(int seriesId) => SetSeriesReadingStatus(seriesId, ReadingStatus.Unknown);
    [RelayCommand] private void SetSeriesReadingStatusPlanned(int seriesId) => SetSeriesReadingStatus(seriesId, ReadingStatus.Planned);
    [RelayCommand] private void SetSeriesReadingStatusReading(int seriesId) => SetSeriesReadingStatus(seriesId, ReadingStatus.Reading);
    [RelayCommand] private void SetSeriesReadingStatusCompleted(int seriesId) => SetSeriesReadingStatus(seriesId, ReadingStatus.Completed);
    [RelayCommand] private void SetSeriesReadingStatusPaused(int seriesId) => SetSeriesReadingStatus(seriesId, ReadingStatus.Paused);
    [RelayCommand] private void SetSeriesReadingStatusDropped(int seriesId) => SetSeriesReadingStatus(seriesId, ReadingStatus.Dropped);
    [RelayCommand] private void SetSeriesReadingStatusReReading(int seriesId) => SetSeriesReadingStatus(seriesId, ReadingStatus.ReReading);

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
    private LibraryViewMode _viewMode = LibraryViewMode.PosterGrid;

    /// <summary>Phase 4a: the single poster grid, replacing Compact/Comfortable/Cover-only (docs/
    /// superpowers/specs/2026-08-27-library-browsing-4a-poster-grid-design.md).</summary>
    public bool IsPosterGrid => ViewMode == LibraryViewMode.PosterGrid;
    public bool IsPanoramaGrid => ViewMode == LibraryViewMode.PanoramaGrid;
    public bool IsListView => ViewMode == LibraryViewMode.List;
    public bool IsDetailsView => ViewMode == LibraryViewMode.Details;
    public bool IsTilesView => ViewMode == LibraryViewMode.Tiles;
    public bool IsIssueListView => ViewMode == LibraryViewMode.IssueList;

    partial void OnViewModeChanged(LibraryViewMode value)
    {
        OnPropertyChanged(nameof(IsPosterGrid));
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
        LibraryViewMode.PosterGrid => "Poster grid",
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
                OnPropertyChanged(nameof(PosterCardWidth));
                OnPropertyChanged(nameof(PosterCoverHeight));
                OnPropertyChanged(nameof(PosterCardHeight));
                OnPropertyChanged(nameof(EffectiveShowTileTitles));
                OnPropertyChanged(nameof(TilesThumbWidth));
                OnPropertyChanged(nameof(TilesThumbHeight));
                OnPropertyChanged(nameof(TilesCardWidth));
                SaveLibrarySettings();
            }
        }
    }

    /// <summary>Poster-grid tile title row height reserved in <see cref="PosterCardHeight"/> when
    /// titles show, so the <c>VirtualizingWrapPanel</c>'s <c>ItemHeight</c> is right in both toggle
    /// states (the old <c>ComfortableGrid</c> overflowed its box with the text row).</summary>
    private const double PosterTitleRowHeight = 34;

    /// <summary>Below this card width the title line is too cramped to read, so it auto-hides
    /// regardless of <see cref="ShowTileTitles"/> (docs/superpowers/specs/2026-08-27-library-
    /// browsing-4a-poster-grid-design.md §2).</summary>
    private const double PosterTitleHideThreshold = 108;

    public double PosterCardWidth => 150 * GridDensity;

    /// <summary>The cover box height (no title row) - what the tile's inner cover Border binds.</summary>
    public double PosterCoverHeight => 216 * GridDensity;

    /// <summary>Cover box + a fixed title-row allowance when titles show - what the
    /// <c>VirtualizingWrapPanel</c>'s <c>ItemHeight</c> binds so it reserves the right space.</summary>
    public double PosterCardHeight => PosterCoverHeight + (EffectiveShowTileTitles ? PosterTitleRowHeight : 0);

    public double TilesThumbWidth => 48 * GridDensity;
    public double TilesThumbHeight => 68 * GridDensity;
    public double TilesCardWidth => 260 * GridDensity;

    /// <summary>Phase 4a: poster tile title row on/off. Persisted via
    /// <c>AppSettings.LibraryShowTileTitles</c>; see <see cref="EffectiveShowTileTitles"/> for the
    /// density-gated value the grid actually binds.</summary>
    [ObservableProperty]
    private bool _showTileTitles = true;

    partial void OnShowTileTitlesChanged(bool value)
    {
        OnPropertyChanged(nameof(EffectiveShowTileTitles));
        OnPropertyChanged(nameof(PosterCardHeight));
        SaveLibrarySettings();
    }

    public bool EffectiveShowTileTitles => ShowTileTitles && PosterCardWidth >= PosterTitleHideThreshold;

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
