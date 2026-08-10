using System;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// PDF Novels open here, not in <see cref="BookReaderScreenViewModel"/> - reflowable text
/// extraction from an arbitrary PDF is inherently unreliable (footnotes, multi-column layouts,
/// running headers interleave into the extracted text unpredictably; design spec §9.1 flagged this
/// as an accepted risk going in). Confirmed against real files during manual testing that the
/// fixed-layout page-image approach is the right call for PDFs specifically - EPUBs keep the real
/// reflowable reader, since EPUB's semantic HTML/CSS doesn't have this problem.
///
/// Reuses the exact same <see cref="Views.PageCanvas"/>/<see cref="PageImageDecoder"/>/
/// <see cref="Views.ZoomPanMath"/> pipeline the comic Reader (<see cref="ReaderScreenViewModel"/>)
/// already uses - PDF is already a supported comic page-image format there, and PageCanvas itself
/// has zero Issue/Series coupling (just a Bitmap + zoom/pan + left/right commands), so this is a
/// straight port of that page/zoom/pan pattern onto a <see cref="Book"/> instead of an
/// <see cref="Issue"/>, not a new gesture/rendering implementation.
/// </summary>
public partial class PdfPageReaderScreenViewModel : ViewModelBase
{
    private readonly Action _goBack;
    private IPageImageDecoder? _decoder;

    public PdfPageReaderScreenViewModel(Action goBack)
    {
        _goBack = goBack;
    }

    [ObservableProperty]
    private string _bookTitle = string.Empty;

    [ObservableProperty]
    private Bitmap? _currentPage;

    [ObservableProperty]
    private int _pageIndex;

    [ObservableProperty]
    private int _pageCount = 1;

    [ObservableProperty]
    private bool _highQualityPageDisplay = true;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    public double ProgressFraction => PageCount > 1 ? (double)PageIndex / (PageCount - 1) : 0;

    /// <summary>1-based, for display only - <see cref="PageIndex"/> itself stays 0-based to match <see cref="IPageImageDecoder.GetPage"/>.</summary>
    public int PageNumber => PageIndex + 1;

    partial void OnPageIndexChanged(int value) => OnPropertyChanged(nameof(PageNumber));

    private double _zoomLevel = 1.0;

    /// <summary>Hand-written clamp-then-cascade, same shape and same reason as <see cref="ReaderScreenViewModel.ZoomLevel"/> - a double-click reset or a page turn both just set this to 1.0 and trust the cascade to zero pan, rather than each caller doing it separately.</summary>
    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            double clamped = Math.Clamp(value, 1.0, 4.0);
            if (SetProperty(ref _zoomLevel, clamped) && clamped == 1.0)
            {
                PanOffsetX = 0;
                PanOffsetY = 0;
            }
        }
    }

    [ObservableProperty]
    private double _panOffsetX;

    [ObservableProperty]
    private double _panOffsetY;

    public void LoadBook(int bookId)
    {
        _decoder?.Dispose();
        _decoder = null;

        using var context = PaperbunkrDb.CreateContext();
        var book = context.Books.Single(b => b.Id == bookId);

        BookTitle = book.Title;
        HighQualityPageDisplay = context.GetOrCreateAppSettings().HighQualityPageDisplay;
        ErrorMessage = null;
        ZoomLevel = 1.0;
        PageIndex = 0;

        _decoder = PageImageDecoder.TryOpen(book.FilePath);
        if (_decoder is null)
        {
            ErrorMessage = "Couldn't open this PDF - unsupported or a damaged file.";
            PageCount = 1;
            CurrentPage = null;
            return;
        }

        PageCount = Math.Max(1, _decoder.PageCount);
        RefreshCurrentPage();
    }

    [RelayCommand]
    private void GoLeft() => ChangePage(-1);

    [RelayCommand]
    private void GoRight() => ChangePage(1);

    [RelayCommand]
    private void Close() => _goBack();

    private void ChangePage(int delta)
    {
        if (_decoder is null)
        {
            return;
        }

        int next = Math.Clamp(PageIndex + delta, 0, PageCount - 1);
        if (next == PageIndex)
        {
            return;
        }

        PageIndex = next;
        RefreshCurrentPage();
    }

    private void RefreshCurrentPage()
    {
        CurrentPage = _decoder?.GetPage(PageIndex);
        ZoomLevel = 1.0;
        OnPropertyChanged(nameof(ProgressFraction));
    }
}
