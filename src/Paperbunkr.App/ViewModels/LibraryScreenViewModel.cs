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
    private LibraryViewMode _viewMode = LibraryViewMode.Grid;

    public bool IsGridView => ViewMode == LibraryViewMode.Grid;
    public bool IsListView => ViewMode == LibraryViewMode.List;

    partial void OnViewModeChanged(LibraryViewMode value)
    {
        OnPropertyChanged(nameof(IsGridView));
        OnPropertyChanged(nameof(IsListView));
    }

    [RelayCommand]
    private void ShowGridView() => ViewMode = LibraryViewMode.Grid;

    [RelayCommand]
    private void ShowListView() => ViewMode = LibraryViewMode.List;

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
