using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Books grid + folder management (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md
/// §1/§3, Phase 1) - independent of <see cref="LibraryScreenViewModel"/>. Deliberately self-contained
/// (its own inline scan-status text) rather than routed through MainViewModel's toast plumbing for
/// scan progress, matching the "simpler than Library's chrome" scope call in the design spec.
/// Book-card selection (<see cref="SelectBook"/>) now opens the real reader (Phase 2) - it briefly
/// toasted "not available yet" during Phase 1, when there was nothing to navigate to. Routes by
/// format: EPUB opens the reflowable text reader, PDF opens the comic-panel-style page reader
/// (reflowed text extraction from an arbitrary PDF proved unreliable enough in manual testing to
/// not be worth it - PDF is already a supported comic page-image format, so it gets that pipeline
/// instead of a bespoke one).
/// </summary>
public partial class BooksScreenViewModel : ViewModelBase
{
    private readonly FilePickerService _filePicker;
    private readonly BookFolderScanService _scanService;
    private readonly BookCoverThumbnailService _coverService;
    private readonly Action<int, BookFormat> _goReaderForBook;

    public BooksScreenViewModel(FilePickerService filePicker, BookFolderScanService scanService, BookCoverThumbnailService coverService, Action<int, BookFormat> goReaderForBook)
    {
        _filePicker = filePicker;
        _goReaderForBook = goReaderForBook;
        _scanService = scanService;
        _coverService = coverService;
        Books = new ObservableCollection<BookCardSample>();
        Folders = new ObservableCollection<BookFolderSummary>();
    }

    public ObservableCollection<BookCardSample> Books { get; }

    public ObservableCollection<BookFolderSummary> Folders { get; }

    public bool HasBooks => Books.Count > 0;

    public bool HasFolders => Folders.Count > 0;

    [ObservableProperty]
    private string? _scanStatus;

    [ObservableProperty]
    private bool _isScanning;

    public void LoadFromDatabase()
    {
        using var context = PaperbunkrDb.CreateContext();

        Folders.Clear();
        foreach (var folder in context.BookFolders.OrderBy(f => f.Path))
        {
            Folders.Add(new BookFolderSummary { Id = folder.Id, Path = folder.Path });
        }

        var books = context.Books
            .Include(b => b.BookSeries)
            .OrderBy(b => b.BookSeries != null ? b.BookSeries.SortName ?? b.BookSeries.Name : b.Title)
            .ThenBy(b => b.Title)
            .ToList();

        Books.Clear();
        foreach (var book in books)
        {
            Books.Add(BookCardSample.FromBook(book));
        }

        OnPropertyChanged(nameof(HasBooks));
        OnPropertyChanged(nameof(HasFolders));
    }

    [RelayCommand]
    private void SelectBook(BookCardSample? book)
    {
        if (book is null)
        {
            return;
        }

        _goReaderForBook(book.BookId, book.Format);
    }

    /// <summary>
    /// Tile context menu's "Delete Book" (docs/superpowers/specs/2026-08-22-delete-functionality-
    /// design.md) - same nested-submenu confirm as Library's Delete Series/Issue (a context menu
    /// closes after every click, so there's no persistently-visible button for TwoStepConfirm's
    /// timed re-click to apply to). Moves the file to the Recycle Bin (confirmed with the user),
    /// same as Library - <see cref="Entities.Book"/> has no reading-list/event-style cross-
    /// references to worry about (<see cref="Entities.BookBookmark"/>'s FK is the only thing
    /// pointing at it, and that's <c>DeleteBehavior.Cascade</c>), so no shared helper needed here.
    /// </summary>
    [RelayCommand]
    private void DeleteBook(int bookId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var book = context.Books.Find(bookId);
        if (book is null)
        {
            return;
        }

        RecycleBinHelper.SendToRecycleBin(book.FilePath);
        context.Books.Remove(book);
        context.SaveChanges();
        BookCoverImageCache.Invalidate(bookId);
        LoadFromDatabase();
    }

    [RelayCommand]
    private async Task AddFolder()
    {
        string? path = await _filePicker.PickFolderAsync("Add Book Folder");
        if (path is null)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            if (!context.BookFolders.Any(f => f.Path == path))
            {
                context.BookFolders.Add(new BookFolder { Path = path });
                context.SaveChanges();
            }
        }

        LoadFromDatabase();
    }

    [RelayCommand]
    private void RemoveFolder(BookFolderSummary folder)
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            var entity = context.BookFolders.FirstOrDefault(f => f.Id == folder.Id);
            if (entity is not null)
            {
                context.BookFolders.Remove(entity);
                context.SaveChanges();
            }
        }

        LoadFromDatabase();
    }

    [RelayCommand]
    private async Task ScanNow()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        ScanStatus = "Scanning…";
        try
        {
            var scanProgress = new Progress<(int Done, int Total)>(p => ScanStatus = $"Scanning… {p.Done}/{p.Total}");
            var result = await _scanService.ScanAllAsync(scanProgress);

            if (result.BooksAdded > 0)
            {
                // Same rationale as LibraryFolderScanner's ScanNow follow-up: newly-added books
                // have no cached cover yet, so generate them now instead of leaving the grid
                // showing blank placeholder gradients until a separate action runs this pipeline.
                var coverProgress = new Progress<(int Done, int Total)>(p => ScanStatus = $"Generating covers… {p.Done}/{p.Total}");
                await _coverService.GenerateAllAsync(coverProgress);
            }

            ScanStatus = result.BooksAdded == 0
                ? "No new books found."
                : $"Added {result.BooksAdded} book{(result.BooksAdded == 1 ? "" : "s")} across {result.SeriesTouched} series.";
        }
        catch (Exception ex)
        {
            ScanStatus = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            LoadFromDatabase();
        }
    }
}
