using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Books grid + chrome (docs/superpowers/specs/2026-08-27-books-screen-chrome-and-home-strip-
/// design.md) - search, a sort menu, and a group-by menu, lighter than
/// <see cref="LibraryScreenViewModel"/> (no view modes or filter chips). Multi-select + a bulk
/// editor + a series editor were added in B3 (docs/superpowers/specs/2026-08-27-books-bulk-series-
/// editing-design.md). Folder management + scanning live in Preferences → Libraries. Sort/group
/// persist via <see cref="AppSettings"/>; search and selection do not.
/// </summary>
public partial class BooksScreenViewModel : ViewModelBase, IContextMenuProvider, IListLayoutHost
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
    private PreferencesScreenViewModel? _preferencesForScan;

    public bool IsBookFolderOperationRunning => _preferencesForScan?.IsAnyBookFolderOperationRunning ?? false;

    /// <summary>Connects the Books toolbar to the shared Preferences book-scan workflow.</summary>
    public void AttachManualScan(PreferencesScreenViewModel preferences)
    {
        if (_preferencesForScan is not null)
        {
            _preferencesForScan.PropertyChanged -= OnPreferencesScanStateChanged;
        }

        _preferencesForScan = preferences;
        _preferencesForScan.PropertyChanged += OnPreferencesScanStateChanged;
        OnPropertyChanged(nameof(IsBookFolderOperationRunning));
    }

    private void OnPreferencesScanStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PreferencesScreenViewModel.IsAnyBookFolderOperationRunning)
            or nameof(PreferencesScreenViewModel.IsScanningBooks)
            or nameof(PreferencesScreenViewModel.IsClearingBookCoverCache))
        {
            OnPropertyChanged(nameof(IsBookFolderOperationRunning));
        }
    }

    [RelayCommand]
    private async Task ScanBooks()
    {
        if (_preferencesForScan is null || IsBookFolderOperationRunning)
        {
            return;
        }

        await _preferencesForScan.ScanBookFoldersFromToolbarAsync(LoadFromDatabase);
    }

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
        WorkspaceService? workspaceService = null,
        ListLayoutService? listLayoutService = null)
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
        InitializeListLayouts(listLayoutService);
        LoadFromDatabase();
        RefreshWorkspaces();
    }

    // --- List layouts (docs/superpowers/specs/2026-10-04-list-layouts-design.md) ---
    // Books has no columns, captions or tiles: a Books layout is its sort field, direction and group, the same three
    // fields a Books workspace holds. It has one list, so one remembered layout (key "all").

    /// <summary>The "List layouts" section of the Workspace menu, and the Edit Layouts overlay's view model.</summary>
    public ListLayoutsViewModel ListLayouts { get; private set; } = null!;

    WorkspaceScreen IListLayoutHost.LayoutScreen => WorkspaceScreen.Books;

    string IListLayoutHost.LayoutSelectionKey => "all";

    string IListLayoutHost.CaptureLayoutJson() => WorkspaceStateJson.Serialize(CaptureBooksState());

    void IListLayoutHost.ApplyLayoutJson(string json)
    {
        SetBooksLayoutFields(WorkspaceStateJson.DeserializeBooks(json));
        SaveBooksSettings();
        Rebuild();
        RaiseBooksLayoutBindings();
    }

    private void InitializeListLayouts(ListLayoutService? service)
    {
        ListLayouts = new ListLayoutsViewModel(this, service, _promptForName)
        {
            CloseMenu = () => ActiveDropdown = null,
        };

        if (ListLayouts.Initialize() is { } json)
        {
            SetBooksLayoutFields(WorkspaceStateJson.DeserializeBooks(json));
        }

        ListLayouts.MarkApplied();
    }

    private void SetBooksLayoutFields(BooksWorkspaceState s)
    {
#pragma warning disable MVVMTK0034
        _sortField = s.SortField;
        _sortDirection = s.SortDirection;
        _groupField = s.GroupField;
#pragma warning restore MVVMTK0034
    }

    private void RaiseBooksLayoutBindings()
    {
        foreach (var name in new[]
        {
            nameof(SortField), nameof(SortDirection), nameof(GroupField),
            nameof(SortLabel), nameof(GroupLabel), nameof(SortDirectionGlyph), nameof(IsGrouped),
        })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>A List Options editor over the Books list's sort and group.</summary>
    public ListOptionsViewModel CreateListOptions(Action close) =>
        ListOptionsViewModel.ForBooks(CaptureBooksState(), state => ListLayouts.ApplyToCurrentList(WorkspaceStateJson.Serialize(state)), close);

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

    /// <summary>Ctrl+A: selects every card currently shown.</summary>
    [RelayCommand]
    private void SelectAllVisible()
    {
        Selection.SelectAll(OrderedCards);
        RaiseSelectionChanged();
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

    partial void OnSearchQueryChanged(string value)
    {
        OnPropertyChanged(nameof(HasSearch));
        OnPropertyChanged(nameof(NoMatchesMessage));
        Rebuild();
    }

    public bool HasSearch => !string.IsNullOrWhiteSpace(SearchQuery);

    /// <summary>"No books match “x”." while searching, otherwise the tab-specific line.</summary>
    public string NoMatchesMessage => HasSearch ? $"No books match “{SearchQuery.Trim()}”." : EmptyTabMessage;

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
        RaiseLayoutFlags();
        SaveBooksSettings();
        Rebuild();
    }

    public string SortLabel => SortField switch
    {
        BooksSortField.Author => "Author",
        BooksSortField.RecentlyAdded => "Recently added",
        BooksSortField.LastOpened => "Last opened",
        BooksSortField.Rating => "Rating",
        BooksSortField.Progress => "Progress",
        BooksSortField.Publisher => "Publisher",
        BooksSortField.Published => "Published",
        BooksSortField.SeriesOrder => "Series order",
        _ => "Title",
    };

    public string SortDirectionGlyph => SortDirection == SortDirection.Ascending ? "↑" : "↓";

    public string GroupLabel => GroupField switch
    {
        BooksGroupField.Series => "Series",
        BooksGroupField.Author => "Author",
        BooksGroupField.Status => "Status",
        BooksGroupField.Publisher => "Publisher",
        BooksGroupField.Language => "Language",
        BooksGroupField.Genre => "Genre",
        _ => "None",
    };

    // --- status tabs, author filter, shelf, view mode (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md) ---

    public ObservableCollection<BooksStatusTabItem> StatusTabs { get; } = new(new[]
    {
        new BooksStatusTabItem(BooksStatusTab.All, "All"),
        new BooksStatusTabItem(BooksStatusTab.Reading, "Reading"),
        new BooksStatusTabItem(BooksStatusTab.WantToRead, "Want to read"),
        new BooksStatusTabItem(BooksStatusTab.Unread, "Unread"),
        new BooksStatusTabItem(BooksStatusTab.Finished, "Finished"),
        new BooksStatusTabItem(BooksStatusTab.Abandoned, "Abandoned"),
    });

    [ObservableProperty]
    private BooksStatusTab _activeStatusTab = BooksStatusTab.All;

    partial void OnActiveStatusTabChanged(BooksStatusTab value)
    {
        OnPropertyChanged(nameof(EmptyTabMessage));
        OnPropertyChanged(nameof(NoMatchesMessage));
        SaveBrowseViewSettings();
        Rebuild();
    }

    [RelayCommand]
    private void SetStatusTab(BooksStatusTab tab) => ActiveStatusTab = tab;

    private void RefreshStatusTabs()
    {
        var counts = BooksBrowseQuery.TabCounts(_allCards);
        foreach (var item in StatusTabs)
        {
            item.Count = counts.GetValueOrDefault(item.Tab);
            item.IsActive = item.Tab == ActiveStatusTab;
        }
    }

    /// <summary>The one-line reason the list is empty under the current tab/filters (the "no matches" state).</summary>
    public string EmptyTabMessage => ActiveStatusTab switch
    {
        BooksStatusTab.All => "No books match your search or filters.",
        BooksStatusTab.Reading => "Nothing is marked as Reading right now.",
        BooksStatusTab.WantToRead => "No books on your Want to read list.",
        BooksStatusTab.Unread => "No unread books match.",
        BooksStatusTab.Finished => "No finished books match.",
        _ => "No abandoned books match.",
    };

    /// <summary>"Author: X" chip, set when Book Detail's author link opens the grid (docs/superpowers/specs/2026-10-27-books-slice2b-detail-design.md §7).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAuthorFilter), nameof(AuthorFilterLabel))]
    private string? _authorFilter;

    public bool HasAuthorFilter => !string.IsNullOrWhiteSpace(AuthorFilter);

    public string AuthorFilterLabel => $"Author: {AuthorFilter}";

    partial void OnAuthorFilterChanged(string? value) => Rebuild();

    /// <summary>"7 books · 3 finished" for the chosen author, over everything they have in the library (not narrowed by the tab or search).</summary>
    [ObservableProperty]
    private string _authorSummary = string.Empty;

    /// <summary>0-1: finished books over all of the author's books, for the thin bar.</summary>
    [ObservableProperty]
    private double _authorFinishedFraction;

    private void RefreshAuthorSummary()
    {
        if (string.IsNullOrWhiteSpace(AuthorFilter))
        {
            AuthorSummary = string.Empty;
            AuthorFinishedFraction = 0;
            return;
        }

        var mine = _allCards.Where(c => !c.IsGhost && string.Equals(c.Author?.Trim(), AuthorFilter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        int finished = mine.Count(c => c.Status == BookReadingStatus.Finished);
        AuthorSummary = $"{mine.Count} book{(mine.Count == 1 ? string.Empty : "s")} · {finished} finished";
        AuthorFinishedFraction = mine.Count == 0 ? 0 : (double)finished / mine.Count;
    }

    [RelayCommand]
    private void ClearAuthorFilter() => AuthorFilter = null;

    /// <summary>Book ids allowed by the chosen shelf; null = no shelf. Session-only.</summary>
    private IReadOnlySet<int>? _shelfIds;

    // --- Shelves menu (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §4) ---

    /// <summary>Test seam; production uses the real database.</summary>
    internal BookShelfService ShelfService { get; set; } = new();

    public ObservableCollection<BookShelfRow> BuiltInShelves { get; } = new();

    public ObservableCollection<BookShelfRow> SmartShelves { get; } = new();

    public ObservableCollection<BookShelfRow> CollectionShelves { get; } = new();

    public bool HasSmartShelves => SmartShelves.Count > 0;

    public bool HasCollectionShelves => CollectionShelves.Count > 0;

    private BookShelf? _activeShelf;

    public bool HasActiveShelf => _activeShelf is not null;

    /// <summary>"Shelves" while none is chosen, otherwise "Shelf: Favorites".</summary>
    public string ShelfButtonLabel => _activeShelf is null ? "Shelves" : $"Shelf: {_activeShelf.Name}";

    public bool IsShelvesOpen => ActiveDropdown == "shelves";

    // "Add books" menu: ways to bring books in beyond the scanned folders (Slice 4).
    public bool IsAddMenuOpen => ActiveDropdown == "add";

    /// <summary>Set by MainViewModel: opens the Import from Calibre dialog.</summary>
    public Action? GoCalibreImport { get; set; }

    /// <summary>Set by MainViewModel: opens the OPDS catalog browser.</summary>
    public Action? GoCatalogs { get; set; }

    [RelayCommand]
    private void BrowseCatalogs()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ActiveDropdown = null);
        GoCatalogs?.Invoke();
    }

    [RelayCommand]
    private void ToggleAddMenu() => ActiveDropdown = ActiveDropdown == "add" ? null : "add";

    [RelayCommand]
    private void ImportFromCalibre()
    {
        // Deferred: this runs from a menu row Button.Click still routing through the popup that closing it would detach.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ActiveDropdown = null);
        GoCalibreImport?.Invoke();
    }

    [RelayCommand]
    private void ToggleShelves()
    {
        if (ActiveDropdown != "shelves")
        {
            RefreshShelves();
        }

        ActiveDropdown = ActiveDropdown == "shelves" ? null : "shelves";
    }

    /// <summary>Rebuilds the three shelf lists from the database (new smart lists / collections appear without a restart) and keeps the choice if it still exists.</summary>
    private void RefreshShelves()
    {
        BuiltInShelves.Clear();
        SmartShelves.Clear();
        CollectionShelves.Clear();

        var shelves = ShelfService.List();
        foreach (var shelf in shelves)
        {
            var row = new BookShelfRow(shelf) { IsActive = _activeShelf?.Key == shelf.Key };
            (shelf.Kind switch
            {
                BookShelfKind.SmartList => SmartShelves,
                BookShelfKind.Collection => CollectionShelves,
                _ => BuiltInShelves,
            }).Add(row);
        }

        if (_activeShelf is not null && shelves.All(s => s.Key != _activeShelf.Key))
        {
            SetShelf(null);
        }

        OnPropertyChanged(nameof(HasSmartShelves));
        OnPropertyChanged(nameof(HasCollectionShelves));
    }

    [RelayCommand]
    private void SelectShelf(BookShelfRow? row)
    {
        // The row's own button raised this click; closing the popup (which detaches that button) is deferred a tick (CLAUDE.md: don't detach a control from inside the event it is raising).
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ActiveDropdown = null);
        SetShelf(row?.Shelf);
    }

    [RelayCommand]
    private void ClearShelf() => SetShelf(null);

    private void SetShelf(BookShelf? shelf)
    {
        _activeShelf = shelf;
        _shelfIds = shelf is null ? null : ShelfService.Resolve(shelf);

        foreach (var row in BuiltInShelves.Concat(SmartShelves).Concat(CollectionShelves))
        {
            row.IsActive = shelf is not null && row.Shelf.Key == shelf.Key;
        }

        OnPropertyChanged(nameof(HasActiveShelf));
        OnPropertyChanged(nameof(ShelfButtonLabel));
        Rebuild();
    }

    [ObservableProperty]
    private BooksViewMode _viewMode = BooksViewMode.Grid;

    partial void OnViewModeChanged(BooksViewMode value)
    {
        OnPropertyChanged(nameof(IsBookshelfView));
        OnPropertyChanged(nameof(IsGridView));
        RaiseLayoutFlags();
        RefreshAlphabetIndex();
        SaveBrowseViewSettings();
    }

    public bool IsBookshelfView => ViewMode == BooksViewMode.Bookshelf;

    // The four content layouts: covers or spines, flat or grouped.
    public bool IsGridUngrouped => IsGridView && !IsGrouped;
    public bool IsGridGrouped => IsGridView && IsGrouped;
    public bool IsShelfUngrouped => IsBookshelfView && !IsGrouped;
    public bool IsShelfGrouped => IsBookshelfView && IsGrouped;

    // --- A-Z jump rail (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §7) ---

    /// <summary>The rail's 27 letters, dimmed where nothing in the current view starts with them.</summary>
    [ObservableProperty]
    private IReadOnlyList<AlphabetIndexEntry> _alphabetIndex = System.Array.Empty<AlphabetIndexEntry>();

    /// <summary>Both views (books redesign §3.4: the rail jumps with ScrollIntoView, which a virtualized list supports), a flat Title/Author
    /// sort or any grouping but Status.</summary>
    [ObservableProperty]
    private bool _showAlphabetIndex;

    private void RefreshAlphabetIndex()
    {
        ShowAlphabetIndex = HasBooks && BooksAlphabet.Applies(IsGrouped, GroupField, SortField);
        AlphabetIndex = ShowAlphabetIndex ? BooksAlphabet.Build(Books, Groups, IsGrouped, SortField) : System.Array.Empty<AlphabetIndexEntry>();
    }

    private void RaiseLayoutFlags()
    {
        OnPropertyChanged(nameof(IsGridUngrouped));
        OnPropertyChanged(nameof(IsGridGrouped));
        OnPropertyChanged(nameof(IsShelfUngrouped));
        OnPropertyChanged(nameof(IsShelfGrouped));
    }

    public bool IsGridView => ViewMode == BooksViewMode.Grid;

    [RelayCommand]
    private void SetViewMode(BooksViewMode mode) => ViewMode = mode;

    /// <summary>Persists the tab and view mode on their own: they are not part of a workspace, so they must not reset the workspace label the way a sort change does.</summary>
    private void SaveBrowseViewSettings()
    {
        if (_loadingBrowseViewSettings)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();
        settings.BooksActiveStatusTab = ActiveStatusTab;
        settings.BooksViewMode = ViewMode;
        context.SaveChanges();
    }

    private bool _loadingBrowseViewSettings;

    [ObservableProperty]
    private string? _activeDropdown;

    public bool IsSortOpen => ActiveDropdown == "sort";
    public bool IsGroupOpen => ActiveDropdown == "group";

    partial void OnActiveDropdownChanged(string? value)
    {
        OnPropertyChanged(nameof(IsSortOpen));
        OnPropertyChanged(nameof(IsGroupOpen));
        OnPropertyChanged(nameof(IsShelvesOpen));
        OnPropertyChanged(nameof(IsAddMenuOpen));
        OnPropertyChanged(nameof(IsWorkspaceOpen));
        OnPropertyChanged(nameof(IsSelectionStatusOpen));
        OnPropertyChanged(nameof(IsSelectionRateOpen));
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

        // Deferred: this command runs from a row Button.Click still routing through the Workspace
        // Popup's own content. Closing the popup synchronously here detaches that same row's visual
        // tree mid-route, which crashes Avalonia's detach walk with an ArgumentOutOfRangeException
        // (see Paperbunkr.App.Controls.SuggestBox.Commit for the fully diagnosed case).
        Dispatcher.UIThread.Post(() => ActiveDropdown = null);
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

        // Deferred: this command runs from the row's own ✕ Button.Click still routing through the
        // Workspaces row's own ItemsControl - see ApplyWorkspace above for the fully diagnosed crash
        // this avoids.
        Dispatcher.UIThread.Post(RefreshWorkspaces);
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

        // Deferred: this command runs from the row's own ▲/▼ Button.Click still routing through the
        // Workspaces row's own ItemsControl - see ApplyWorkspace above for the fully diagnosed crash
        // this avoids.
        Dispatcher.UIThread.Post(RefreshWorkspaces);
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
            .Include(b => b.Tags)
            .ToList();

        // Time left for the Reading books' card captions (redesign §2): one read of their sessions, then Slice 2b's pace per book.
        var readingIds = books.Where(b => b.Status == BookReadingStatus.Reading).Select(b => b.Id).ToHashSet();
        var sessionsByBook = readingIds.Count == 0
            ? new Dictionary<int, List<ReadingEvent>>()
            : context.ReadingEvents.AsNoTracking()
                .Where(e => e.ItemType == ReadingItemType.Novel && readingIds.Contains(e.ItemId))
                .ToList()
                .GroupBy(e => e.ItemId)
                .ToDictionary(g => g.Key, g => g.ToList());

        _allCards.Clear();
        _allCards.AddRange(books.Select(b => BookCardSample.FromBook(b, sessionsByBook.TryGetValue(b.Id, out var sessions)
            ? Paperbunkr.Data.Books.BookReadingStatsResolver.Compute(sessions, b.CharacterCount, Paperbunkr.Data.Books.BookProgress.Compute(b), finished: false).TimeLeft
            : null)));

        Selection.Clear();
        RaiseSelectionChanged();

        if (_activeShelf is not null)
        {
            _shelfIds = ShelfService.Resolve(_activeShelf);
        }

        // Read-only list for the tile context menu's "Add to Collection ▸" submenu - this screen
        // doesn't manage collections (no create/rename/delete UI here, that's the Library sidebar),
        // so unlike LibraryScreenViewModel.Collections there's no DeleteConfirm to wire up.
        Collections.Clear();
        foreach (var collection in context.Collections.Include(c => c.Items).OrderBy(c => c.SortOrder))
        {
            Collections.Add(new CollectionSummary { Id = collection.Id, Name = collection.Name, Count = collection.Items.Count, AccentColor = collection.AccentColor });
        }

        RefreshSpread();
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

    // --- Plugin API v2 Books hook (docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-hooks-plan.md §3/§4) ---

    private PluginHostService? _pluginHost;

    public bool HasPluginHost => _pluginHost is not null;

    /// <summary>Called once from <c>App.axaml.cs</c> after <see cref="PluginHostService.Initialize"/> - same pattern as <c>LibraryScreenViewModel.AttachHost</c>.</summary>
    public void AttachHost(PluginHostService host)
    {
        _pluginHost = host;
        OnPropertyChanged(nameof(HasPluginHost));
    }

    /// <summary>Enabled commands registered under the Books hook, for the tile context menu's plugin submenu - one entry per command (unlike Library's single hardcoded "Find Duplicates" item, since this is a new anchor with no single-plugin assumption baked in).</summary>
    public IEnumerable<Command> NovelBooksPluginCommands => _pluginHost?.GetNovelBooksCommands() ?? Enumerable.Empty<Command>();

    /// <summary>Runs one Books-hook command against a single book tile. Parameter is <c>(bookId, command)</c>.</summary>
    [RelayCommand]
    private async Task RunNovelBooksPlugin((int BookId, Command Command) target)
    {
        if (_pluginHost is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var book = context.Books.Find(target.BookId);
        if (book is null)
        {
            return;
        }

        await _pluginHost.RunNovelBooksCommandAsync(target.Command, new[] { book });
    }

    /// <summary>Staggered grid entrance (docs/superpowers/specs/2026-09-12-entrance-animation-v2-
    /// design.md) - set true by <see cref="Rebuild"/>, the one funnel every real trigger (nav-in
    /// reload via <see cref="LoadFromDatabase"/>, search/sort/sort-direction changes) runs through.
    /// Read once per container preparation by <see cref="Controls.EntranceAnimation.Prepare"/>, not
    /// a live binding, so ordinary scroll-driven virtualization recycling never replays it.</summary>
    [ObservableProperty]
    private bool _playEntranceAnimation;

    /// <summary>The options <see cref="BooksBrowseQuery"/> runs with: tab, search, shelf and author filters plus the sort/group state.</summary>
    private BooksBrowseOptions BrowseOptions => new(ActiveStatusTab, SearchQuery, _shelfIds, AuthorFilter, SortField, SortDirection, GroupField);

    private void Rebuild()
    {
        PlayEntranceAnimation = false; // pulse: a one-shot entrance opens on a real false -> true change (Motion v2 D, Q19)
        PlayEntranceAnimation = true;
        OnPropertyChanged(nameof(ShowSpread)); // every tab, search, shelf and author change funnels through here
        RaiseChromeLabels();

        var options = BrowseOptions;
        var sorted = BooksBrowseQuery.Sort(BooksBrowseQuery.Filter(_allCards, options), options.Sort, options.Direction);

        Books.Clear();
        Groups.Clear();
        if (IsGrouped)
        {
            var groups = BooksBrowseQuery.Group(sorted, GroupField, SortField);
            // A ghost says "you do not own this", so it is only shown when nothing hides books (no tab, shelf, author or search).
            if (GroupField == BooksGroupField.Series && options.Tab == BooksStatusTab.All && options.ShelfIds is null
                && string.IsNullOrWhiteSpace(options.Author) && string.IsNullOrWhiteSpace(options.Search))
            {
                BooksBrowseQuery.AddGhosts(groups, _allCards, SortField, SortDirection);
            }

            foreach (var group in groups)
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

        RefreshStatusTabs();
        RefreshAuthorSummary();
        OnPropertyChanged(nameof(HasBooks));
        RefreshAlphabetIndex();
        OnPropertyChanged(nameof(ShowEmptyLibrary));
        OnPropertyChanged(nameof(ShowNoMatches));
    }

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
        _activeStatusTab = settings.BooksActiveStatusTab;
        _viewMode = settings.BooksViewMode;
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

        // Null only while the constructor is still seeding.
        ListLayouts?.TrackChange();
    }

    // --- navigation / actions ---

    /// <summary>Card click - navigates to Book Details normally, but toggles selection instead once
    /// the grid is in selection mode (docs/superpowers/specs/2026-08-27-books-bulk-series-editing-
    /// design.md). Shift-state comes from code-behind via <see cref="SetShiftHeld"/>.</summary>
    [RelayCommand]
    private void CardClick(BookCardSample? book)
    {
        if (book is null || book.IsGhost)
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

    /// <summary>Set by MainViewModel: opens the single-book "Match metadata" dialog.</summary>
    public Action<int>? GoMatchBook { get; set; }

    /// <summary>Set by MainViewModel: runs the bulk match as an Activity Center job.</summary>
    public Action<IReadOnlyList<int>>? RunBulkMatch { get; set; }

    [RelayCommand]
    private void MatchBook(int bookId) => GoMatchBook?.Invoke(bookId);

    [RelayCommand]
    private void MatchSelection()
    {
        var ids = Selection.SelectedIds.ToList();
        if (ids.Count > 0)
        {
            RunBulkMatch?.Invoke(ids);
        }
    }

    /// <summary>Tile context menu's "Set status ▸ {status}". Parameter is <c>(bookId, status)</c>.</summary>
    [RelayCommand]
    private void SetBookStatus((int BookId, BookReadingStatus Status) target)
    {
        if (new BookQuickEditService().SetStatus(new[] { target.BookId }, target.Status))
        {
            LoadFromDatabase();
        }
    }

    /// <summary>Tile context menu's "Rate ▸ {stars}". Parameter is <c>(bookId, rating)</c>; a null or 0 rating clears it.</summary>
    [RelayCommand]
    private void RateBook((int BookId, float? Rating) target)
    {
        if (new BookQuickEditService().SetRating(new[] { target.BookId }, target.Rating))
        {
            LoadFromDatabase();
        }
    }

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
