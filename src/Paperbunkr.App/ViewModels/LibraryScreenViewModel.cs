using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Library grid + toolbar, ported from LibraryScreen.dc.html (Claude Design project 43c40b25),
/// "pills" toolbar variant (the default selected in the parent "Paperbunkr App" wireframe).
/// </summary>
public partial class LibraryScreenViewModel : ViewModelBase
{
    private readonly Action _goDetail;

    public LibraryScreenViewModel(Action goDetail)
    {
        _goDetail = goDetail;
        Covers = new ObservableCollection<SeriesCardSample>(BuildSampleCovers());
    }

    public ObservableCollection<SeriesCardSample> Covers { get; }

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

    [RelayCommand]
    private void SelectCard(SeriesCardSample? card) => _goDetail();

    private static SeriesCardSample[] BuildSampleCovers() =>
    [
        new() { CoverBrush = SeriesCardSample.Gradient("#3a2f45", "#8a4a2e"), Title = "THE CARTOGRAPHER'S VAULT", Name = "The Cartographer's Vault", Sub = "Comic · 18 issues", UnreadCount = 5 },
        new() { CoverBrush = SeriesCardSample.Gradient("#1e3a3f", "#2f7d6a"), Title = "NIGHTSHIFT ORCHID", Name = "Nightshift Orchid", Sub = "Manga · Vol. 1–9", UnreadCount = 2 },
        new() { CoverBrush = SeriesCardSample.Gradient("#442a1c", "#c9803f"), Title = "BRASS HORIZON", Name = "Brass Horizon", Sub = "Comic · 42 issues", UnreadCount = 0, Missing = true },
        new() { CoverBrush = SeriesCardSample.Gradient("#26313f", "#4a6b8a"), Title = "KILO STATION", Name = "Kilo Station", Sub = "Comic · 61 issues", UnreadCount = 14 },
        new() { CoverBrush = SeriesCardSample.Gradient("#3f2130", "#a34a5c"), Title = "THE SOVEREIGN'S CAGE", Name = "The Sovereign's Cage", Sub = "Manhwa · Ch. 1–88", UnreadCount = 9 },
        new() { CoverBrush = SeriesCardSample.Gradient("#1f2a1c", "#5c8a4a"), Title = "IRONCLAD REQUIEM", Name = "Ironclad Requiem", Sub = "Comic · 61 issues", UnreadCount = 0 },
        new() { CoverBrush = SeriesCardSample.Gradient("#2a2333", "#6a5ca3"), Title = "PAPER MOTH", Name = "Paper Moth", Sub = "Manga · Vol. 1–4", UnreadCount = 1 },
        new() { CoverBrush = SeriesCardSample.Gradient("#332118", "#8a5a2e"), Title = "NINTH HOUR BLADE", Name = "Ninth Hour Blade", Sub = "Manhua · Ch. 1–210", UnreadCount = 31, Missing = true },
        new() { CoverBrush = SeriesCardSample.Gradient("#3a2f45", "#8a4a2e"), Title = "ASHLIGHT", Name = "Ashlight", Sub = "Manhwa · Ch. 1–52", UnreadCount = 0 },
        new() { CoverBrush = SeriesCardSample.Gradient("#26313f", "#4a6b8a"), Title = "THE LAST CARTEL", Name = "The Last Cartel", Sub = "Comic · 12 issues", UnreadCount = 12 },
        new() { CoverBrush = SeriesCardSample.Gradient("#1e3a3f", "#2f7d6a"), Title = "IRON LOOM", Name = "Iron Loom", Sub = "Manga · Vol. 1–11", UnreadCount = 0 },
        new() { CoverBrush = SeriesCardSample.Gradient("#1f2a1c", "#5c8a4a"), Title = "VANTA REACH", Name = "Vanta Reach", Sub = "Manga · Vol. 1–6", UnreadCount = 3 },
    ];
}
