using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Books grid (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md §1, Phase 1) -
/// independent of <see cref="LibraryScreenViewModel"/>. Folder management + scanning moved to
/// Preferences → Libraries (docs/superpowers/specs/2026-08-27-books-section-restyle-and-folders-to-
/// preferences-plan.md), so this screen is now purely the cover grid + reader routing:
/// EPUB opens the reflowable text reader, PDF opens the comic-panel-style page reader.
/// </summary>
public partial class BooksScreenViewModel : ViewModelBase
{
    private readonly Action<int, BookFormat> _goReaderForBook;
    private readonly Action _goLibrarySettings;

    public BooksScreenViewModel(Action<int, BookFormat> goReaderForBook, Action goLibrarySettings)
    {
        _goReaderForBook = goReaderForBook;
        _goLibrarySettings = goLibrarySettings;
        Books = new ObservableCollection<BookCardSample>();
    }

    public ObservableCollection<BookCardSample> Books { get; }

    public bool HasBooks => Books.Count > 0;

    public void LoadFromDatabase()
    {
        using var context = PaperbunkrDb.CreateContext();

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

    /// <summary>Empty-state action - jumps to Preferences → Libraries where book folders and the
    /// scan live now.</summary>
    [RelayCommand]
    private void OpenLibrarySettings() => _goLibrarySettings();

    /// <summary>
    /// Tile context menu's "Delete Book" (docs/superpowers/specs/2026-08-22-delete-functionality-
    /// design.md) - same nested-submenu confirm as Library's Delete Series/Issue. Moves the file to
    /// the Recycle Bin (confirmed with the user). <see cref="Entities.Book"/> has no reading-list/
    /// event cross-references (<see cref="Entities.BookBookmark"/>'s FK is Cascade), so no shared
    /// helper needed here.
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
}
