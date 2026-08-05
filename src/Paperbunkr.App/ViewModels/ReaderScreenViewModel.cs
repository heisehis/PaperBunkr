using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Reader screen, ported from ReaderScreen.dc.html (Claude Design project 43c40b25).
/// The source component is deliberately minimal - only goBack is a real prop, everything
/// else (page counter, reading-mode label, thumbnails, progress) is static sample content
/// in that file, carried over here unchanged pending real page-decode wiring (§8).
/// </summary>
public partial class ReaderScreenViewModel : ViewModelBase
{
    public ReaderScreenViewModel(Action goBack)
    {
        _goBack = goBack;
        CoverBrush = SeriesCardSample.Gradient("#442a1c", "#c9803f");

        Thumbnails = new ObservableCollection<ReaderThumbnailSample>
        {
            new() { CoverBrush = CoverBrush },
            new() { CoverBrush = CoverBrush },
            new() { CoverBrush = CoverBrush, IsSelected = true },
            new() { CoverBrush = CoverBrush },
        };
    }

    private readonly Action _goBack;

    public ObservableCollection<ReaderThumbnailSample> Thumbnails { get; }

    public IBrush CoverBrush { get; }
    public string BreadcrumbSeries => "Library / Brass Horizon /";
    public string IssueTitle => "Issue #12 — What the Rivets Knew";
    public string ReadingModeLabel => "Right to Left ▾";
    public string PageLabel => "PAGE 14 / 24";
    public string PageNumber => "14";
    public string PageSubtitle => "Brass Horizon · #12";
    public double ProgressFraction => 0.58;

    [RelayCommand]
    private void GoBack() => _goBack();
}
