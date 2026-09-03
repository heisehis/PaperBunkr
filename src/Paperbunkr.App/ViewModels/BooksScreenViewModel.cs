using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Books grid + chrome (docs/superpowers/specs/2026-08-27-books-screen-chrome-and-home-strip-
/// design.md) - search, a sort menu, and a group-by menu, lighter than
/// <see cref="LibraryScreenViewModel"/> (no view modes or filter chips). Multi-select + a bulk
/// editor + a series editor were added in B3 (docs/superpowers/specs/2026-08-27-books-bulk-series-
/// editing-design.md). Folder management + scanning live in Preferences → Libraries. Sort/group
/// persist via <see cref="AppSettings"/>; search and selection do not.
/// </summary>
public partial class BooksScreenViewModel : ViewModelBase, IContextMenuProvider
{
    /// <summary>Right-click menu for a book tile / series-group header (docs/superpowers/specs/
    /// 2026-08-29-context-menu-rebuild-design.md's own follow-up list named this screen - the old
    /// in-template Button.ContextMenu blocks never rendered at all, same root cause Library had
    /// before its rebuild: a ContextMenu popup doesn't render in this Avalonia build, and its
    /// $parent[UserControl] ancestor bindings resolve to null inside that popup's own visual tree
    /// anyway.</summary>
    IReadOnlyList<ContextMenuEntry>? IContextMenuProvider.BuildContextMenu(object? target) =>
        new BooksContextMenuBuilder(this).Build(target);

    private readonly Action<int> _goBookDetail;
    private readonly Action<int> _goBookSeriesDetail;
    private readonly Action<int> _goEditBook;
    private readonly Action<IReadOnlyList<int>> _goBulkEdit;
    private readonly Action<int> _goEditSeries;
    private readonly Action _goLibrarySettings;
    private readonly Action<string, string> _showToast;

    /// <summary>Every book, unfiltered/ungrouped - <see cref="Rebuild"/> derives <see cref="Books"/>
    /// / <see cref="Groups"/> from this on every search/sort/group change without re-querying.</summary>
    private readonly List<BookCardSample> _allCards = new();

    /// <summary>Set from code-behind's PointerPressed just before a card <see cref="CardClickCommand"/>
    /// fires, so <see cref="TileSelectionController{T}.Toggle"/> can range-extend; reset after each click.</summary>
    private bool _shiftHeld;

    public BooksScreenViewModel(Action<int> goBookDetail, Action<int> goBookSeriesDetail, Action<int> goEditBook,
        Action<IReadOnlyList<int>> goBulkEdit, Action<int> goEditSeries, Action goLibrarySettings,
        Action<string, string>? showToast = null,
        Action<string?, Action<string>>? promptForName = null,
        WorkspaceService? workspaceService = null)
    {
        _goBookDetail = goBookDetail;
        _goBookSeriesDetail = goBookSeriesDetail;
        _goEditBook = goEditBook;
        _goBulkEdit = goBulkEdit;
        _goEditSeries = goEditSeries;
        _goLibrarySettings = goLibrarySettings;
        _showToast = showToast ?? ((_, _) => { });
        _promptForName = promptForName ?? ((_, _) => { });
        _workspaceService = workspaceService ?? new WorkspaceService();
        Workspaces = new ObservableCollection<WorkspaceRow>();
        Books = new ObservableCollection<BookCardSample>();
        Groups = new ObservableCollection<BookCardGroup>();
        Collections = new ObservableCollection<CollectionSummary>();

        LoadBooksSettings();
        LoadFromDatabase();
        RefreshWorkspaces();
    }

    // --- multi-select (B3, mirrors LibraryScreenViewModel.Selection) ---

    public TileSelectionController<BookCardSample> Selection { get; } = new();

    public bool HasSelection => Selection.Count > 0;
    public int SelectionCount => Selection.Count;
    public string SelectionCountLabel => $"{Selection.Count} selected";

    /// <summary>The currently displayed flat card order (groups flattened) - the ordering
    /// <see cref="TileSelectionController{T}.Toggle"/>'s shift-range logic walks.</summary>
    private IList<BookCardSample> OrderedCards =>
        IsGrouped ? Groups.SelectMany(g => g.Items).ToList() : Books;

    /// <summary>Called by code-behind before a card click so shift-range works.</summary>
    public void SetShiftHeld(bool held) => _shiftHeld = held;

    public void ToggleBookSelection(BookCardSample card, bool isShiftHeld)
    {
        Selection.Toggle(OrderedCards, card, isShiftHeld);
        RaiseSelectionChanged();
    }

    [RelayCommand]
    private void ToggleBookSelectionCheckbox(BookCardSample? card)
    {
        if (card is not null)
        {
            ToggleBookSelection(card, isShiftHeld: false);
        }
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(SelectionCountLabel));
    }

    [RelayCommand]
    private void ClearSelection()
    {
        Selection.Clear(_allCards);
        RaiseSelectionChanged();
    }

    [RelayCommand]
    private void EditSelection()
    {
        var ids = Selection.SelectedIds.ToList();
        if (ids.Count > 0)
        {
            _goBulkEdit(ids);
        }
    }

    [RelayCommand]
    private void DeleteSelection()
    {
        var ids = Selection.SelectedIds.ToList();
        if (ids.Count == 0)
        {
            return;
        }

        Selection.Clear();
        RaiseSelectionChanged();
        DeleteBooks(ids);
    }

    public ObservableCollection<BookCardSample> Books { get; }

    public ObservableCollection<BookCardGroup> Groups { get; }

    /// <summary>Every <c>Collection</c> row, for the tile context menu's "Add to Collection ▸" submenu only - see <see cref="LoadFromDatabase"/>'s own note on why there's no <c>DeleteConfirm</c> here.</summary>
    public ObservableCollection<CollectionSummary> Collections { get; }

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
        OnPropertyChanged(nameof(IsWorkspaceOpen));
    }

    [RelayCommand] private void ToggleSort() => ActiveDropdown = ActiveDropdown == "sort" ? null : "sort";
    [RelayCommand] private void ToggleGroup() => ActiveDropdown = ActiveDropdown == "group" ? null : "group";
    [RelayCommand] private void SetSortField(BooksSortField field) => SortField = field;
    [RelayCommand] private void SetGroupField(BooksGroupField field) => GroupField = field;
    [RelayCommand] private void ToggleSortDirection() =>
        SortDirection = SortDirection == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
    [RelayCommand] private void ClearSearch() => SearchQuery = string.Empty;

    // --- Saved Workspaces (docs/superpowers/specs/2026-09-03-library-saved-workspaces-design.md) ---
    // Books' own independent list, parallel to LibraryScreenViewModel's. Three-field snapshots
    // (sort field + direction + group); search text is never captured, matching LoadBooksSettings.

    private readonly WorkspaceService _workspaceService;
    private readonly Action<string?, Action<string>> _promptForName;
    private int? _activeWorkspaceId;
    private bool _suppressWorkspaceTracking;

    public ObservableCollection<WorkspaceRow> Workspaces { get; }

    public bool IsWorkspaceOpen => ActiveDropdown == "workspace";

    public string ActiveWorkspaceLabel =>
        Workspaces.FirstOrDefault(w => w.Id == _activeWorkspaceId)?.Name ?? "Workspace";

    public bool HasReorderableWorkspaces => Workspaces.Count(w => !w.IsBuiltIn) > 1;

    [RelayCommand] private void ToggleWorkspace() => ActiveDropdown = ActiveDropdown == "workspace" ? null : "workspace";

    private void RefreshWorkspaces()
    {
        Workspaces.Clear();
        foreach (var w in _workspaceService.List(WorkspaceScreen.Books))
        {
            Workspaces.Add(new WorkspaceRow(w.Id, w.Name, w.IsBuiltIn, w.Id == _activeWorkspaceId));
        }

        OnPropertyChanged(nameof(ActiveWorkspaceLabel));
        OnPropertyChanged(nameof(HasReorderableWorkspaces));
    }

    private BooksWorkspaceState CaptureBooksState() => new(SortField, SortDirection, GroupField);

    [RelayCommand]
    private void ApplyWorkspace(int id)
    {
        var row = _workspaceService.List(WorkspaceScreen.Books).FirstOrDefault(w => w.Id == id);
        if (row is null)
        {
            RefreshWorkspaces();
            return;
        }

        var s = WorkspaceStateJson.DeserializeBooks(row.StateJson);
        _suppressWorkspaceTracking = true;
        try
        {
#pragma warning disable MVVMTK0034
            _sortField = s.SortField;
            _sortDirection = s.SortDirection;
            _groupField = s.GroupField;
#pragma warning restore MVVMTK0034
            _activeWorkspaceId = id;
            SaveBooksSettings();
            Rebuild();

            foreach (var name in new[]
            {
                nameof(SortField), nameof(SortDirection), nameof(GroupField),
                nameof(SortLabel), nameof(GroupLabel), nameof(SortDirectionGlyph), nameof(IsGrouped),
                nameof(ActiveWorkspaceLabel),
            })
            {
                OnPropertyChanged(name);
            }
        }
        finally
        {
            _suppressWorkspaceTracking = false;
        }

        RefreshWorkspaces();
        ActiveDropdown = null;
    }

    [RelayCommand]
    private void SaveWorkspaceAs() => _promptForName(null, name =>
    {
        string json = WorkspaceStateJson.Serialize(CaptureBooksState());
        var existing = _workspaceService.List(WorkspaceScreen.Books)
            .FirstOrDefault(w => !w.IsBuiltIn && string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));

        int id;
        if (existing is not null)
        {
            _workspaceService.UpdateState(existing.Id, json);
            id = existing.Id;
        }
        else
        {
            id = _workspaceService.Create(WorkspaceScreen.Books, name, json).Id;
        }

        _activeWorkspaceId = id;
        RefreshWorkspaces();
        PersistActiveWorkspaceId();
    });

    [RelayCommand]
    private void RenameWorkspace(int id)
    {
        var row = Workspaces.FirstOrDefault(w => w.Id == id);
        if (row is null || row.IsBuiltIn)
        {
            return;
        }

        _promptForName(row.Name, name =>
        {
            _workspaceService.Rename(id, name);
            RefreshWorkspaces();
        });
    }

    [RelayCommand]
    private void DeleteWorkspace(int id)
    {
        var row = Workspaces.FirstOrDefault(w => w.Id == id);
        if (row is null || row.IsBuiltIn)
        {
            return;
        }

        _workspaceService.Delete(id);
        if (_activeWorkspaceId == id)
        {
            _activeWorkspaceId = null;
            PersistActiveWorkspaceId();
        }

        RefreshWorkspaces();
    }

    [RelayCommand] private void MoveWorkspaceUp(int id) => MoveWorkspace(id, -1);
    [RelayCommand] private void MoveWorkspaceDown(int id) => MoveWorkspace(id, +1);

    private void MoveWorkspace(int id, int delta)
    {
        var user = Workspaces.Where(w => !w.IsBuiltIn).Select(w => w.Id).ToList();
        int index = user.IndexOf(id);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= user.Count)
        {
            return;
        }

        (user[index], user[target]) = (user[target], user[index]);
        _workspaceService.Reorder(WorkspaceScreen.Books, user);
        RefreshWorkspaces();
    }

    [RelayCommand]
    private void ResetToDefaultView()
    {
        var allBooks = _workspaceService.List(WorkspaceScreen.Books).FirstOrDefault(w => w.IsBuiltIn && w.Name == "All books");
        if (allBooks is not null)
        {
            ApplyWorkspace(allBooks.Id);
        }
    }

    private void PersistActiveWorkspaceId()
    {
        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();
        settings.BooksActiveWorkspaceId = _activeWorkspaceId;
        context.SaveChanges();
    }

    // --- data ---

    public void LoadFromDatabase()
    {
        using var context = PaperbunkrDb.CreateContext();

        var books = context.Books
            .Include(b => b.BookSeries)
            .ToList();

        _allCards.Clear();
        _allCards.AddRange(books.Select(BookCardSample.FromBook));

        Selection.Clear();
        RaiseSelectionChanged();

        // Read-only list for the tile context menu's "Add to Collection ▸" submenu - this screen
        // doesn't manage collections (no create/rename/delete UI here, that's the Library sidebar),
        // so unlike LibraryScreenViewModel.Collections there's no DeleteConfirm to wire up.
        Collections.Clear();
        foreach (var collection in context.Collections.Include(c => c.Items).OrderBy(c => c.SortOrder))
        {
            Collections.Add(new CollectionSummary { Id = collection.Id, Name = collection.Name, Count = collection.Items.Count, AccentColor = collection.AccentColor });
        }

        Rebuild();
    }

    /// <summary>Context-menu "Add to Collection ▸ {name}" for a book tile. Parameter is <c>(bookId, collectionId)</c>.</summary>
    [RelayCommand]
    private void AddBookToCollection((int BookId, int CollectionId) target)
    {
        using var context = PaperbunkrDb.CreateContext();
        var collection = context.Collections.Find(target.CollectionId);
        if (collection is null)
        {
            return;
        }

        int before = context.CollectionItems.Count(ci => ci.CollectionId == collection.Id);
        CollectionService.AddItems(context, collection.Id, bookIds: new[] { target.BookId });
        bool added = context.CollectionItems.Count(ci => ci.CollectionId == collection.Id) > before;
        _showToast("Added to collection", added ? $"Added to \"{collection.Name}\"." : $"Already in \"{collection.Name}\".");
    }

    /// <summary>"Add to Collection ▸ New collection…" for a book tile.</summary>
    [RelayCommand]
    private void CreateCollectionAndAddBook(int bookId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var collection = CollectionService.Create(context, "New Collection");
        CollectionService.AddItems(context, collection.Id, bookIds: new[] { bookId });
        LoadFromDatabase();
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

        // Re-apply selection to the freshly rebuilt cards so it survives a re-sort / re-group
        // (docs/superpowers/specs/2026-08-27-books-bulk-series-editing-design.md). LoadFromDatabase
        // is the one that actually clears it.
        foreach (var card in _allCards)
        {
            card.IsSelected = Selection.IsSelected(card.BookId);
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
                // Only a real Series section carries an id the header can navigate to - the
                // "Standalone" bucket and every Author group stay inert.
                BookSeriesId = GroupField == BooksGroupField.Series && !IsFallback(g.Key)
                    ? g.Select(c => c.BookSeriesId).FirstOrDefault(id => id is not null)
                    : null,
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
        _activeWorkspaceId = settings.BooksActiveWorkspaceId;
#pragma warning restore MVVMTK0034
    }

    private void SaveBooksSettings()
    {
        // See LibraryScreenViewModel.SaveLibrarySettings - any governed-field change that isn't an
        // apply drops the toolbar workspace label back to "Workspace".
        if (!_suppressWorkspaceTracking && _activeWorkspaceId is not null)
        {
            _activeWorkspaceId = null;
            OnPropertyChanged(nameof(ActiveWorkspaceLabel));
            for (int i = 0; i < Workspaces.Count; i++)
            {
                if (Workspaces[i].IsActive)
                {
                    Workspaces[i] = Workspaces[i] with { IsActive = false };
                }
            }
        }

        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();
        settings.BooksActiveWorkspaceId = _activeWorkspaceId;
        settings.BooksSortField = SortField;
        settings.BooksSortDirection = SortDirection;
        settings.BooksGroupField = GroupField;
        context.SaveChanges();
    }

    // --- navigation / actions ---

    /// <summary>Card click - navigates to Book Details normally, but toggles selection instead once
    /// the grid is in selection mode (docs/superpowers/specs/2026-08-27-books-bulk-series-editing-
    /// design.md). Shift-state comes from code-behind via <see cref="SetShiftHeld"/>.</summary>
    [RelayCommand]
    private void CardClick(BookCardSample? book)
    {
        if (book is null)
        {
            return;
        }

        if (HasSelection)
        {
            ToggleBookSelection(book, _shiftHeld);
        }
        else
        {
            _goBookDetail(book.BookId);
        }

        _shiftHeld = false;
    }

    /// <summary>Grouped-by-Series section header click - opens that series' Book Details view.
    /// No-op for the "Standalone" bucket and Author groups (their <see cref="BookCardGroup.BookSeriesId"/>
    /// is null and the header is disabled in XAML anyway).</summary>
    [RelayCommand]
    private void OpenSeries(int? bookSeriesId)
    {
        if (bookSeriesId is int id)
        {
            _goBookSeriesDetail(id);
        }
    }

    /// <summary>Grid card context menu's "Edit…" - opens the Book Properties overlay
    /// (docs/superpowers/specs/2026-08-27-book-properties-editor-design.md).</summary>
    [RelayCommand]
    private void EditBook(int bookId) => _goEditBook(bookId);

    /// <summary>Grouped-by-Series section header context menu's "Edit series…" - opens the
    /// BookSeries properties overlay (docs/superpowers/specs/2026-08-27-books-bulk-series-editing-
    /// design.md). No-op for null (non-series headers).</summary>
    [RelayCommand]
    private void EditSeries(int? bookSeriesId)
    {
        if (bookSeriesId is int id)
        {
            _goEditSeries(id);
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
    private void DeleteBook(int bookId) => DeleteBooks(new[] { bookId });

    /// <summary>Deletes each book (Recycle Bin + row), invalidates its cover, then prunes any
    /// <see cref="Entities.BookSeries"/> left with no books
    /// (docs/superpowers/specs/2026-08-27-books-bulk-series-editing-design.md, component d). One
    /// context, one final <see cref="LoadFromDatabase"/>.</summary>
    private void DeleteBooks(IEnumerable<int> bookIds)
    {
        var ids = bookIds.ToList();
        using var context = PaperbunkrDb.CreateContext();
        var books = context.Books.Where(b => ids.Contains(b.Id)).ToList();
        if (books.Count == 0)
        {
            return;
        }

        var affectedSeriesIds = books.Select(b => b.BookSeriesId).Where(id => id is not null).Distinct().ToList();

        foreach (var book in books)
        {
            RecycleBinHelper.SendToRecycleBin(book.FilePath);
            context.Books.Remove(book);
        }

        context.SaveChanges();

        foreach (var book in books)
        {
            BookCoverImageCache.Invalidate(book.Id);
        }

        foreach (var seriesId in affectedSeriesIds)
        {
            BookSeriesMaintenance.PruneIfEmpty(context, seriesId);
        }

        LoadFromDatabase();
    }
}
