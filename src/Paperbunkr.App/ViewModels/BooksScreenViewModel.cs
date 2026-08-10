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
/// §1/§3, Phase 1) - independent of <see cref="LibraryScreenViewModel"/>, no reader wired yet.
/// Deliberately self-contained (its own inline scan-status text) rather than routed through
/// MainViewModel's toast plumbing for scan progress, matching the "simpler than Library's chrome"
/// scope call in the design spec - but book-card selection *does* use the shared toast (see
/// <see cref="SelectBook"/>), since a silent no-op on click read as broken rather than "not built
/// yet" during manual testing.
/// </summary>
public partial class BooksScreenViewModel : ViewModelBase
{
    private readonly FilePickerService _filePicker;
    private readonly BookFolderScanService _scanService;
    private readonly BookCoverThumbnailService _coverService;
    private readonly Action<string, string> _showToast;

    public BooksScreenViewModel(FilePickerService filePicker, BookFolderScanService scanService, BookCoverThumbnailService coverService, Action<string, string> showToast)
    {
        _filePicker = filePicker;
        _showToast = showToast;
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

    /// <summary>
    /// Reading isn't wired up yet (Phase 2 of the design spec) - a card click used to be a silent
    /// no-op, which read as broken during manual testing rather than "not built yet". Toasts
    /// instead so the gap is legible, until the real reader replaces this.
    /// </summary>
    [RelayCommand]
    private void SelectBook(BookCardSample? book)
    {
        if (book is null)
        {
            return;
        }

        _showToast("Reading not available yet", $"\"{book.Title}\" is imported, but the Novels reader isn't built yet.");
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
