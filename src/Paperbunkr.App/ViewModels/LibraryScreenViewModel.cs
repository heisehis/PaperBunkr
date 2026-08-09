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

    private ContentType? _activeContentType;
    private int? _activeCategoryId;

    public LibraryScreenViewModel(Action<int> goDetail)
    {
        _goDetail = goDetail;
        Covers = new ObservableCollection<SeriesCardSample>();
        ContentTypes = new ObservableCollection<ContentTypeSummary>();
        Collections = new ObservableCollection<CategorySummary>();
        LoadFromDatabase();
    }

    public ObservableCollection<SeriesCardSample> Covers { get; }

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

        Covers.Clear();
        foreach (var s in filtered)
        {
            Covers.Add(SeriesCardSample.FromSeries(s));
        }

        OnPropertyChanged(nameof(IsAllSeriesActive));
        OnPropertyChanged(nameof(HasCollections));
    }

    [RelayCommand]
    private void SelectAllSeries()
    {
        _activeContentType = null;
        _activeCategoryId = null;
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

    partial void OnSearchQueryChanged(string value) => LoadFromDatabase();

    [ObservableProperty]
    private bool _filterUnreadOnly;

    partial void OnFilterUnreadOnlyChanged(bool value) => LoadFromDatabase();

    [ObservableProperty]
    private bool _filterMissingIssues;

    partial void OnFilterMissingIssuesChanged(bool value) => LoadFromDatabase();

    [ObservableProperty]
    private bool _filterTrackedOnly;

    partial void OnFilterTrackedOnlyChanged(bool value) => LoadFromDatabase();

    [ObservableProperty]
    private string? _activeDropdown;

    public bool IsFilterOpen => ActiveDropdown == "filter";
    public bool IsSortOpen => ActiveDropdown == "sort";
    public bool IsDisplayOpen => ActiveDropdown == "display";

    partial void OnActiveDropdownChanged(string? value)
    {
        OnPropertyChanged(nameof(IsFilterOpen));
        OnPropertyChanged(nameof(IsSortOpen));
        OnPropertyChanged(nameof(IsDisplayOpen));
    }

    [RelayCommand]
    private void ToggleFilter() => ActiveDropdown = ActiveDropdown == "filter" ? null : "filter";

    [RelayCommand]
    private void ToggleSort() => ActiveDropdown = ActiveDropdown == "sort" ? null : "sort";

    [RelayCommand]
    private void ToggleDisplay() => ActiveDropdown = ActiveDropdown == "display" ? null : "display";

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

    [ObservableProperty]
    private bool _isGeneratingCovers;

    [ObservableProperty]
    private int _coverGenerationDone;

    [ObservableProperty]
    private int _coverGenerationTotal;

    public double CoverGenerationFraction => CoverGenerationTotal > 0 ? (double)CoverGenerationDone / CoverGenerationTotal : 0;

    partial void OnCoverGenerationDoneChanged(int value) => OnPropertyChanged(nameof(CoverGenerationFraction));

    partial void OnCoverGenerationTotalChanged(int value) => OnPropertyChanged(nameof(CoverGenerationFraction));

    /// <summary>
    /// Generates real cover art for every issue that doesn't have one cached yet
    /// (docs/superpowers/specs/2026-08-06-cover-thumbnails-design.md §2). Reloads the library
    /// afterward - CoverImageCache doesn't cache misses, so newly generated thumbnails show up
    /// immediately.
    /// </summary>
    [RelayCommand]
    private async Task GenerateCovers()
    {
        if (IsGeneratingCovers)
        {
            return;
        }

        IsGeneratingCovers = true;
        CoverGenerationDone = 0;
        CoverGenerationTotal = 0;
        var progress = new Progress<(int Done, int Total)>(p =>
        {
            CoverGenerationDone = p.Done;
            CoverGenerationTotal = p.Total;
        });

        try
        {
            await new CoverThumbnailService().GenerateAllAsync(progress);
        }
        finally
        {
            IsGeneratingCovers = false;
            LoadFromDatabase();
        }
    }
}
