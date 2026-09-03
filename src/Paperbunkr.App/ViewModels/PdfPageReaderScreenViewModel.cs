using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
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

    private int _bookId;

    public PdfPageReaderScreenViewModel(Action goBack)
    {
        _goBack = goBack;
    }

    public ObservableCollection<BookAnnotationImageSummary> AnnotationImages { get; } = new();

    [ObservableProperty]
    private bool _isCaptureMode;

    [ObservableProperty]
    private bool _isCapturesOpen;

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

    /// <summary>Bottom-bar label for <c>ReaderChrome</c> (docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-design.md) - "{page} / {count}", the exact text this screen's own scrubber already showed.</summary>
    public string ProgressLabel => $"{PageNumber} / {PageCount}";

    partial void OnPageIndexChanged(int value)
    {
        OnPropertyChanged(nameof(PageNumber));
        OnPropertyChanged(nameof(ProgressLabel));
    }

    partial void OnPageCountChanged(int value) => OnPropertyChanged(nameof(ProgressLabel));

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

    /// <summary>
    /// PDF's first-ever theme setting (docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-
    /// design.md) - reuses the same <see cref="BookTheme"/> enum and <see cref="Book.ThemeOverride"/>/
    /// <see cref="AppSettings.BookReaderTheme"/> storage EPUB's reader already uses (both are already
    /// format-agnostic columns, not EPUB-specific, so no schema change was needed). The Font & Theme
    /// sheet only ever offers Light/Dark/Sepia for PDF (<see cref="Views.ReaderSettingsSheet.PdfThemes"/>),
    /// but the stored/bindable type stays the full shared enum.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasBackground))]
    private BookTheme _theme = BookTheme.MatchAppSkin;

    /// <summary>Tint for the backdrop behind the page image - the same per-theme table the EPUB reader's chrome uses, so a Light/Dark/Sepia choice here looks consistent with the rest of the redesigned HUD. Only tints the canvas/backdrop, never the rasterized PDF page pixels themselves (a real, separate image-processing feature, not in scope here).</summary>
    public IBrush CanvasBackground => BookThemeBrushes.ContentBackground(Theme);

    [ObservableProperty]
    private bool _isFontThemeOpen;

    /// <param name="startAt">Accepted for call-site parity with
    /// <see cref="BookReaderScreenViewModel.LoadBook(int, BookPosition?)"/> and ignored - the
    /// fixed-layout page reader has no chapter/offset position model (it always opens at page 0,
    /// and persists nothing). A chapter/bookmark jump from the Book Details screen only reaches
    /// this reader for a PDF book, which has neither chapters nor bookmarks.</param>
    public void LoadBook(int bookId, BookPosition? startAt = null)
    {
        _ = startAt;
        _bookId = bookId;
        _decoder?.Dispose();
        _decoder = null;
        IsCaptureMode = false;
        IsCapturesOpen = false;

        using var context = PaperbunkrDb.CreateContext();
        var book = context.Books.Include(b => b.AnnotationImages).Single(b => b.Id == bookId);

        BookTitle = book.Title;
        var appSettings = context.GetOrCreateAppSettings();
        HighQualityPageDisplay = appSettings.HighQualityPageDisplay;
        // Same null-coalescing per-book-override-then-global-default chain BookReaderScreenViewModel.
        // LoadBook already uses for Settings.Theme - Book.ThemeOverride/AppSettings.BookReaderTheme are
        // already format-agnostic columns, so PDF simply starts reading/writing them too.
        Theme = book.ThemeOverride ?? appSettings.BookReaderTheme;
        IsFontThemeOpen = false;
        ErrorMessage = null;
        ZoomLevel = 1.0;
        PageIndex = 0;

        AnnotationImages.Clear();
        foreach (var image in book.AnnotationImages.OrderByDescending(a => a.CreatedTime))
        {
            AnnotationImages.Add(ToSummary(image));
        }

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

    [RelayCommand]
    private void ToggleCaptureMode() => IsCaptureMode = !IsCaptureMode;

    [RelayCommand]
    private void OpenCaptures() => IsCapturesOpen = true;

    [RelayCommand]
    private void CloseCaptures() => IsCapturesOpen = false;

    [RelayCommand]
    private void OpenFontTheme() => IsFontThemeOpen = true;

    /// <summary>Persists on close, not per-tick - same "explicit action, not every intermediate change" shape <see cref="BookReaderScreenViewModel.PersistSettingsOverride"/> already uses for EPUB's own theme.</summary>
    [RelayCommand]
    private void CloseFontTheme()
    {
        IsFontThemeOpen = false;
        if (_bookId == 0)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var book = context.Books.FirstOrDefault(b => b.Id == _bookId);
        if (book is not null)
        {
            book.ThemeOverride = Theme;
            context.SaveChanges();
        }
    }

    [RelayCommand]
    private void SetTheme(BookTheme theme) => Theme = theme;

    [RelayCommand]
    private void DeleteCapture(BookAnnotationImageSummary? capture)
    {
        if (capture is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var entity = context.BookAnnotationImages.FirstOrDefault(a => a.Id == capture.Id);
        if (entity is not null)
        {
            context.BookAnnotationImages.Remove(entity);
            context.SaveChanges();
        }

        try
        {
            File.Delete(capture.ImagePath);
        }
        catch (IOException)
        {
        }

        AnnotationImages.Remove(capture);
    }

    /// <summary>
    /// A capture-rectangle drag just completed (design spec §"PDF area capture"). <paramref name="rect"/>
    /// is already fractional (0-1 of the page's own width/height) - <c>PdfPageReaderScreen.axaml.cs</c>
    /// is the one place with the visual-tree access (<c>PageCanvas.GetCurrentImageBounds()</c>) needed
    /// to convert the overlay's raw screen-space drag rect into that fractional form.
    /// </summary>
    public void CaptureRegion(Rect fractionalRect)
    {
        if (CurrentPage is null)
        {
            return;
        }

        string destinationDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Paperbunkr", "annotations");
        string imagePath = BookAnnotationCaptureService.CropAndSave(
            CurrentPage, fractionalRect.X, fractionalRect.Y, fractionalRect.Width, fractionalRect.Height, destinationDirectory);

        using var context = PaperbunkrDb.CreateContext();
        var entity = new BookAnnotationImage
        {
            BookId = _bookId,
            PageIndex = PageIndex,
            RectX = fractionalRect.X,
            RectY = fractionalRect.Y,
            RectWidth = fractionalRect.Width,
            RectHeight = fractionalRect.Height,
            ImagePath = imagePath,
            CreatedTime = DateTime.UtcNow,
        };
        context.BookAnnotationImages.Add(entity);
        context.SaveChanges();

        AnnotationImages.Insert(0, ToSummary(entity));
    }

    private static BookAnnotationImageSummary ToSummary(BookAnnotationImage image) => new()
    {
        Id = image.Id,
        PageIndex = image.PageIndex,
        ImagePath = image.ImagePath,
        Note = image.Note,
        CreatedTime = image.CreatedTime,
    };

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
