using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>One row of List Options' Details tab: a column, whether it shows, and how wide it is.</summary>
public sealed partial class ListOptionColumn : ObservableObject
{
    public required IssueListSortField Field { get; init; }

    public required string Name { get; init; }

    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    private double _width;
}

/// <summary>One row of List Options' Tiles tab.</summary>
public sealed partial class TileElementOption : ObservableObject
{
    public required TileTextElement Element { get; init; }

    public required string Name { get; init; }

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>
/// The List Options overlay (docs/superpowers/specs/2026-10-04-list-layouts-design.md §8) - CE's <c>ListLayoutDialog</c>:
/// a Details tab (checkable, ordered columns; Move Up / Move Down / Show All / Hide All), a Thumbnails tab (three caption
/// lines, or no text) and a Tiles tab (text elements, Default). Like CE's it does not edit sort, group or view mode on
/// the Library; Books has no columns, captions or tiles, so its one tab is sort and group.
///
/// It edits a draft. Apply hands the draft to the screen; OK applies and closes; Cancel just closes.
/// </summary>
public partial class ListOptionsViewModel : ViewModelBase
{
    public const string NoneLabel = "(None)";

    private readonly ListLayoutState? _libraryState;
    private readonly Action<ListLayoutState>? _applyLibrary;
    private readonly Action<BooksWorkspaceState>? _applyBooks;
    private readonly Action _close;
    private readonly Dictionary<string, IssueListSortField> _captionFieldByName = new(StringComparer.OrdinalIgnoreCase);

    private ListOptionsViewModel(ListLayoutState? libraryState, Action<ListLayoutState>? applyLibrary, Action<BooksWorkspaceState>? applyBooks, Action close)
    {
        _libraryState = libraryState;
        _applyLibrary = applyLibrary;
        _applyBooks = applyBooks;
        _close = close;
        Columns = new ObservableCollection<ListOptionColumn>();
        TileElements = new ObservableCollection<TileElementOption>();
        CaptionChoices = new List<string>();
        BooksSortChoices = new List<string>();
        BooksGroupChoices = new List<string>();
    }

    /// <param name="initialTab"><c>details</c>, <c>thumbnails</c> or <c>tiles</c> - CE opens the dialog on the tab of the current view mode.</param>
    public static ListOptionsViewModel ForLibrary(ListLayoutState state, string initialTab, Action<ListLayoutState> apply, Action close)
    {
        var vm = new ListOptionsViewModel(state, apply, null, close) { ActiveTab = initialTab };

        // Every column-eligible field, the layout's own first and in its order.
        var listed = new HashSet<IssueListSortField>();
        foreach (var column in state.Columns ?? DefaultColumns())
        {
            if (IssueListFieldCatalog.SortFields.TryGetValue(column.Field, out var descriptor) && descriptor.Display is not null && listed.Add(column.Field))
            {
                vm.Columns.Add(new ListOptionColumn { Field = column.Field, Name = descriptor.DisplayName, IsChecked = column.Visible, Width = column.Width });
            }
        }

        foreach (var descriptor in IssueListFieldCatalog.ColumnFields)
        {
            if (listed.Add(descriptor.Field))
            {
                vm.Columns.Add(new ListOptionColumn
                {
                    Field = descriptor.Field,
                    Name = descriptor.DisplayName,
                    IsChecked = false,
                    Width = LibraryScreenViewModel.DefaultDetailsColumnWidth(descriptor.Field),
                });
            }
        }

        vm.SelectedColumn = vm.Columns.FirstOrDefault();

        vm.CaptionChoices.Add(NoneLabel);
        foreach (var descriptor in IssueListFieldCatalog.ColumnFields.OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (vm._captionFieldByName.TryAdd(descriptor.DisplayName, descriptor.Field))
            {
                vm.CaptionChoices.Add(descriptor.DisplayName);
            }
        }

        vm.UseBuiltInCaptions = state.CaptionFields is null;
        vm.HideCaptions = state.HideCaptions;
        vm.Caption1 = vm.CaptionNameAt(state.CaptionFields, 0);
        vm.Caption2 = vm.CaptionNameAt(state.CaptionFields, 1);
        vm.Caption3 = vm.CaptionNameAt(state.CaptionFields, 2);

        var checkedElements = (state.TileElements ?? ListLayoutState.DefaultTileElements).ToHashSet();
        foreach (var element in Enum.GetValues<TileTextElement>())
        {
            vm.TileElements.Add(new TileElementOption { Element = element, Name = TileElementName(element), IsChecked = checkedElements.Contains(element) });
        }

        return vm;
    }

    public static ListOptionsViewModel ForBooks(BooksWorkspaceState state, Action<BooksWorkspaceState> apply, Action close)
    {
        var vm = new ListOptionsViewModel(null, null, apply, close) { ActiveTab = "books" };
        vm.BooksSortChoices.AddRange(Enum.GetValues<BooksSortField>().Select(BooksSortName));
        vm.BooksGroupChoices.AddRange(Enum.GetValues<BooksGroupField>().Select(BooksGroupName));
        vm.BooksSort = BooksSortName(state.SortField);
        vm.BooksGroup = BooksGroupName(state.GroupField);
        vm.BooksSortDescending = state.SortDirection == SortDirection.Descending;
        return vm;
    }

    public bool IsLibrary => _applyLibrary is not null;

    public bool IsBooks => _applyBooks is not null;

    // --- tabs ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailsTab), nameof(IsThumbnailsTab), nameof(IsTilesTab))]
    private string _activeTab = "details";

    public bool IsDetailsTab => IsLibrary && ActiveTab == "details";

    public bool IsThumbnailsTab => IsLibrary && ActiveTab == "thumbnails";

    public bool IsTilesTab => IsLibrary && ActiveTab == "tiles";

    [RelayCommand]
    private void SelectTab(string tab) => ActiveTab = tab;

    // --- Details ---

    public ObservableCollection<ListOptionColumn> Columns { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveColumnUpCommand), nameof(MoveColumnDownCommand))]
    private ListOptionColumn? _selectedColumn;

    private bool CanMoveColumnUp() => SelectedColumn is not null && Columns.IndexOf(SelectedColumn) > 0;

    private bool CanMoveColumnDown() => SelectedColumn is not null && Columns.IndexOf(SelectedColumn) < Columns.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveColumnUp))]
    private void MoveColumnUp() => MoveSelectedColumn(-1);

    [RelayCommand(CanExecute = nameof(CanMoveColumnDown))]
    private void MoveColumnDown() => MoveSelectedColumn(+1);

    private void MoveSelectedColumn(int delta)
    {
        if (SelectedColumn is not { } column)
        {
            return;
        }

        int index = Columns.IndexOf(column);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= Columns.Count)
        {
            return;
        }

        Columns.Move(index, target);

        // ObservableCollection.Move makes a ListBox drop its selection.
        SelectedColumn = column;
        MoveColumnUpCommand.NotifyCanExecuteChanged();
        MoveColumnDownCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ShowAllColumns()
    {
        foreach (var column in Columns)
        {
            column.IsChecked = true;
        }
    }

    /// <summary>CE's Hide All. A layout with no visible column falls back to the default set when it is applied, so this cannot leave an empty table.</summary>
    [RelayCommand]
    private void HideAllColumns()
    {
        foreach (var column in Columns)
        {
            column.IsChecked = false;
        }
    }

    // --- Thumbnails ---

    public List<string> CaptionChoices { get; }

    /// <summary>The poster cards' built-in two lines (series and number, or name and summary). Off = the three pickers below decide.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPickCaptions))]
    private bool _useBuiltInCaptions = true;

    /// <summary>CE's "Do not show any Text".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPickCaptions))]
    private bool _hideCaptions;

    public bool CanPickCaptions => !UseBuiltInCaptions && !HideCaptions;

    [ObservableProperty]
    private string _caption1 = NoneLabel;

    [ObservableProperty]
    private string _caption2 = NoneLabel;

    [ObservableProperty]
    private string _caption3 = NoneLabel;

    private string CaptionNameAt(IReadOnlyList<IssueListSortField>? fields, int index) =>
        fields is not null && index < fields.Count && IssueListFieldCatalog.SortFields.TryGetValue(fields[index], out var d)
            ? d.DisplayName
            : NoneLabel;

    // --- Tiles ---

    public ObservableCollection<TileElementOption> TileElements { get; }

    /// <summary>CE's "Default" button.</summary>
    [RelayCommand]
    private void ResetTileElements()
    {
        var defaults = ListLayoutState.DefaultTileElements.ToHashSet();
        foreach (var option in TileElements)
        {
            option.IsChecked = defaults.Contains(option.Element);
        }
    }

    // --- Books ---

    public List<string> BooksSortChoices { get; }

    public List<string> BooksGroupChoices { get; }

    [ObservableProperty]
    private string _booksSort = string.Empty;

    [ObservableProperty]
    private string _booksGroup = string.Empty;

    [ObservableProperty]
    private bool _booksSortDescending;

    // --- Apply / OK / Cancel ---

    [RelayCommand]
    private void Apply()
    {
        if (_applyLibrary is not null)
        {
            _applyLibrary(BuildLibraryState());
        }
        else if (_applyBooks is not null)
        {
            _applyBooks(BuildBooksState());
        }
    }

    [RelayCommand]
    private void Ok()
    {
        Apply();
        _close();
    }

    [RelayCommand]
    private void Cancel() => _close();

    /// <summary>The draft as a layout: the fields this overlay edits, on top of the ones it was opened with.</summary>
    public ListLayoutState BuildLibraryState()
    {
        var captions = UseBuiltInCaptions
            ? null
            : new[] { Caption1, Caption2, Caption3 }
                .Where(name => _captionFieldByName.ContainsKey(name ?? string.Empty))
                .Select(name => _captionFieldByName[name])
                .ToList();

        var checkedTiles = TileElements.Where(o => o.IsChecked).Select(o => o.Element).ToList();
        bool tilesAreDefault = checkedTiles.ToHashSet().SetEquals(ListLayoutState.DefaultTileElements);

        return (_libraryState ?? new ListLayoutState()) with
        {
            Columns = Columns.Select(c => new ListLayoutColumn(c.Field, c.IsChecked, ListLayoutStateJson.ClampWidth(c.Width))).ToList(),
            CaptionFields = captions,
            HideCaptions = HideCaptions,
            TileElements = tilesAreDefault ? null : checkedTiles,
        };
    }

    public BooksWorkspaceState BuildBooksState()
    {
        var sort = Enum.GetValues<BooksSortField>().FirstOrDefault(f => BooksSortName(f) == BooksSort);
        var group = Enum.GetValues<BooksGroupField>().FirstOrDefault(f => BooksGroupName(f) == BooksGroup);
        return new BooksWorkspaceState(sort, BooksSortDescending ? SortDirection.Descending : SortDirection.Ascending, group);
    }

    private static IEnumerable<ListLayoutColumn> DefaultColumns() =>
        IssueListFieldCatalog.DefaultDetailsColumns.Select(f => new ListLayoutColumn(f, true, LibraryScreenViewModel.DefaultDetailsColumnWidth(f)));

    public static string TileElementName(TileTextElement element) => element switch
    {
        TileTextElement.Title => "Title",
        TileTextElement.Series => "Series",
        TileTextElement.Summary => "Type and issue count (series tiles)",
        _ => Enum.TryParse<IssueListSortField>(element.ToString(), out var field) && IssueListFieldCatalog.SortFields.TryGetValue(field, out var d)
            ? d.DisplayName
            : element.ToString(),
    };

    private static string BooksSortName(BooksSortField field) => field switch
    {
        BooksSortField.RecentlyAdded => "Recently added",
        BooksSortField.LastOpened => "Last opened",
        _ => field.ToString(),
    };

    private static string BooksGroupName(BooksGroupField field) => field.ToString();
}
