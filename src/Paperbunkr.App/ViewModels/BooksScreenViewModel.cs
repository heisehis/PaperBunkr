using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Books grid + chrome (docs/superpowers/specs/2026-08-27-books-screen-chrome-and-home-strip-
/// design.md) - search, a sort menu, and a group-by menu, deliberately lighter than
/// <see cref="LibraryScreenViewModel"/> (no view modes, chips, or selection). Folder management +
/// scanning live in Preferences → Libraries. Sort/group persist via <see cref="AppSettings"/>;
/// search does not.
/// </summary>
public partial class BooksScreenViewModel : ViewModelBase
{
    private readonly Action<int, BookFormat> _goReaderForBook;
    private readonly Action _goLibrarySettings;

    /// <summary>Every book, unfiltered/ungrouped - <see cref="Rebuild"/> derives <see cref="Books"/>
    /// / <see cref="Groups"/> from this on every search/sort/group change without re-querying.</summary>
    private readonly List<BookCardSample> _allCards = new();

    public BooksScreenViewModel(Action<int, BookFormat> goReaderForBook, Action goLibrarySettings)
    {
        _goReaderForBook = goReaderForBook;
        _goLibrarySettings = goLibrarySettings;
        Books = new ObservableCollection<BookCardSample>();
        Groups = new ObservableCollection<BookCardGroup>();

        LoadBooksSettings();
        LoadFromDatabase();
    }

    public ObservableCollection<BookCardSample> Books { get; }

    public ObservableCollection<BookCardGroup> Groups { get; }

    public bool IsGrouped => GroupField != BooksGroupField.None;

    public bool HasBooks => Books.Count > 0 || Groups.Count > 0;

    public bool ShowEmptyLibrary => _allCards.Count == 0;

    public bool ShowNoMatches => _allCards.Count > 0 && !HasBooks;

    // --- chrome state ---

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    partial void OnSearchQueryChanged(string value) => Rebuild();

    [ObservableProperty]
    private BooksSortField _sortField = BooksSortField.Title;

    partial void OnSortFieldChanged(BooksSortField value)
    {
        OnPropertyChanged(nameof(SortLabel));
        SaveBooksSettings();
        Rebuild();
    }

    [ObservableProperty]
    private SortDirection _sortDirection = SortDirection.Ascending;

    partial void OnSortDirectionChanged(SortDirection value)
    {
        OnPropertyChanged(nameof(SortDirectionGlyph));
        SaveBooksSettings();
        Rebuild();
    }

    [ObservableProperty]
    private BooksGroupField _groupField = BooksGroupField.None;

    partial void OnGroupFieldChanged(BooksGroupField value)
    {
        OnPropertyChanged(nameof(IsGrouped));
        OnPropertyChanged(nameof(GroupLabel));
        SaveBooksSettings();
        Rebuild();
    }

    public string SortLabel => SortField switch
    {
        BooksSortField.Author => "Author",
        BooksSortField.RecentlyAdded => "Recently added",
        BooksSortField.LastOpened => "Last opened",
        _ => "Title",
    };

    public string SortDirectionGlyph => SortDirection == SortDirection.Ascending ? "↑" : "↓";

    public string GroupLabel => GroupField switch
    {
        BooksGroupField.Series => "Series",
        BooksGroupField.Author => "Author",
        _ => "None",
    };

    [ObservableProperty]
    private string? _activeDropdown;

    public bool IsSortOpen => ActiveDropdown == "sort";
    public bool IsGroupOpen => ActiveDropdown == "group";

    partial void OnActiveDropdownChanged(string? value)
    {
        OnPropertyChanged(nameof(IsSortOpen));
        OnPropertyChanged(nameof(IsGroupOpen));
    }

    [RelayCommand] private void ToggleSort() => ActiveDropdown = ActiveDropdown == "sort" ? null : "sort";
    [RelayCommand] private void ToggleGroup() => ActiveDropdown = ActiveDropdown == "group" ? null : "group";
    [RelayCommand] private void SetSortField(BooksSortField field) => SortField = field;
    [RelayCommand] private void SetGroupField(BooksGroupField field) => GroupField = field;
    [RelayCommand] private void ToggleSortDirection() =>
        SortDirection = SortDirection == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
    [RelayCommand] private void ClearSearch() => SearchQuery = string.Empty;

    // --- data ---

    public void LoadFromDatabase()
    {
        using var context = PaperbunkrDb.CreateContext();

        var books = context.Books
            .Include(b => b.BookSeries)
            .ToList();

        _allCards.Clear();
        _allCards.AddRange(books.Select(BookCardSample.FromBook));

        Rebuild();
    }

    private void Rebuild()
    {
        IEnumerable<BookCardSample> cards = _allCards;

        string query = SearchQuery.Trim();
        if (query.Length > 0)
        {
            cards = cards.Where(c =>
                Contains(c.Title, query) || Contains(c.Author, query) || Contains(c.SeriesName, query));
        }

        var sorted = Sort(cards).ToList();

        Books.Clear();
        Groups.Clear();
        if (IsGrouped)
        {
            foreach (var group in GroupCards(sorted))
            {
                Groups.Add(group);
            }
        }
        else
        {
            foreach (var card in sorted)
            {
                Books.Add(card);
            }
        }

        OnPropertyChanged(nameof(HasBooks));
        OnPropertyChanged(nameof(ShowEmptyLibrary));
        OnPropertyChanged(nameof(ShowNoMatches));
    }

    private IEnumerable<BookCardSample> Sort(IEnumerable<BookCardSample> cards)
    {
        IOrderedEnumerable<BookCardSample> ordered = SortField switch
        {
            BooksSortField.Author => cards.OrderBy(c => c.Author ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            BooksSortField.RecentlyAdded => cards.OrderBy(c => c.AddedTime),
            BooksSortField.LastOpened => cards.OrderBy(c => c.LastOpenedTime ?? DateTime.MinValue),
            _ => cards.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase),
        };

        // Stable secondary key so equal primaries (e.g. two books with no author) don't shuffle.
        ordered = ordered.ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase);

        return SortDirection == SortDirection.Descending ? ordered.Reverse() : ordered;
    }

    private IEnumerable<BookCardGroup> GroupCards(List<BookCardSample> sorted)
    {
        const string standalone = "Standalone";
        const string unknownAuthor = "Unknown author";

        string KeyFor(BookCardSample c) => GroupField == BooksGroupField.Series
            ? (string.IsNullOrWhiteSpace(c.SeriesName) ? standalone : c.SeriesName!)
            : (string.IsNullOrWhiteSpace(c.Author) ? unknownAuthor : c.Author!);

        bool IsFallback(string key) => key is standalone or unknownAuthor;

        // Group order: the "Standalone"/"Unknown" bucket always last; the rest alphabetically for a
        // name sort, or by the group's newest book for a time sort (so the grouped view still reads
        // "most recent first" top-to-bottom). Books within each group keep the flat sort order that
        // GroupBy preserves.
        var groups = sorted.GroupBy(KeyFor).ToList();

        IEnumerable<IGrouping<string, BookCardSample>> ordered = SortField switch
        {
            BooksSortField.RecentlyAdded => groups
                .OrderBy(g => IsFallback(g.Key))
                .ThenByDescending(g => g.Max(c => c.AddedTime)),
            BooksSortField.LastOpened => groups
                .OrderBy(g => IsFallback(g.Key))
                .ThenByDescending(g => g.Max(c => c.LastOpenedTime ?? DateTime.MinValue)),
            _ => groups
                .OrderBy(g => IsFallback(g.Key))
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase),
        };

        foreach (var g in ordered)
        {
            yield return new BookCardGroup
            {
                Header = g.Key,
                Items = new ObservableCollection<BookCardSample>(g),
            };
        }
    }

    private static bool Contains(string? value, string query) =>
        !string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    // --- persistence (mirrors LibraryScreenViewModel.LoadLibrarySettings / SaveLibrarySettings) ---

    private void LoadBooksSettings()
    {
        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();
#pragma warning disable MVVMTK0034
        _sortField = settings.BooksSortField;
        _sortDirection = settings.BooksSortDirection;
        _groupField = settings.BooksGroupField;
#pragma warning restore MVVMTK0034
    }

    private void SaveBooksSettings()
    {
        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();
        settings.BooksSortField = SortField;
        settings.BooksSortDirection = SortDirection;
        settings.BooksGroupField = GroupField;
        context.SaveChanges();
    }

    // --- navigation / actions ---

    [RelayCommand]
    private void SelectBook(BookCardSample? book)
    {
        if (book is not null)
        {
            _goReaderForBook(book.BookId, book.Format);
        }
    }

    /// <summary>Empty-state action - jumps to Preferences → Libraries where book folders and the
    /// scan live now.</summary>
    [RelayCommand]
    private void OpenLibrarySettings() => _goLibrarySettings();

    /// <summary>
    /// Tile context menu's "Delete Book" (docs/superpowers/specs/2026-08-22-delete-functionality-
    /// design.md) - moves the file to the Recycle Bin. <see cref="Entities.Book"/> has no
    /// reading-list/event cross-references (<see cref="Entities.BookBookmark"/>'s FK is Cascade).
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
