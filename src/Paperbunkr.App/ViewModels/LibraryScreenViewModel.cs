using System;
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

    public LibraryScreenViewModel(Action<int> goDetail)
    {
        _goDetail = goDetail;
        Covers = new ObservableCollection<SeriesCardSample>();
        LoadFromDatabase();
    }

    public ObservableCollection<SeriesCardSample> Covers { get; }

    [ObservableProperty]
    private int _allSeriesCount;

    [ObservableProperty]
    private int _comicCount;

    [ObservableProperty]
    private int _mangaCount;

    public void LoadFromDatabase()
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series
            .Include(s => s.Issues)
            .OrderBy(s => s.SortName ?? s.Name)
            .ToList();

        Covers.Clear();
        foreach (var s in series)
        {
            Covers.Add(SeriesCardSample.FromSeries(s));
        }

        AllSeriesCount = series.Count;
        ComicCount = series.Count(s => s.ContentType == ContentType.Comic);
        MangaCount = series.Count(s => s.ContentType == ContentType.Manga);
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
