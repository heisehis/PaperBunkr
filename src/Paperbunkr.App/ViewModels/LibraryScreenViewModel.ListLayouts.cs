using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// List layouts (docs/superpowers/specs/2026-10-04-list-layouts-design.md): what a Library layout is made of, how one is
/// captured and applied, and the pieces the List Options overlay and the Details header edit (column order and width,
/// thumbnail caption lines, tile text elements). The named layouts and the per-list bookkeeping are <see cref="ListLayouts"/>.
/// </summary>
public partial class LibraryScreenViewModel : IListLayoutHost
{
    /// <summary>The "List layouts" section of the Workspace menu, and the Edit Layouts overlay's view model.</summary>
    public ListLayoutsViewModel ListLayouts { get; private set; } = null!;

    /// <summary>True while a layout is being applied: the per-field change hooks must neither save nor re-render in between.</summary>
    private bool _applyingListLayout;

    /// <summary>The list whose layout is on screen, so a browse-history step that stays on the same list re-applies nothing.</summary>
    private string? _appliedLayoutKey;

    WorkspaceScreen IListLayoutHost.LayoutScreen => WorkspaceScreen.Library;

    public string LayoutSelectionKey =>
        _activeCollectionId is int collectionId ? $"collection:{collectionId}"
        : _activeContentType is { } contentType ? $"content:{contentType}"
        : "all";

    string IListLayoutHost.CaptureLayoutJson() => ListLayoutStateJson.Serialize(CaptureListLayout());

    void IListLayoutHost.ApplyLayoutJson(string json)
    {
        ApplyListLayout(ListLayoutStateJson.Deserialize(json));
        SaveLibrarySettings();
        RebuildView(ViewTrigger.SortGroup);
    }

    private void InitializeListLayouts(Services.ListLayoutService? service)
    {
        ListLayouts = new ListLayoutsViewModel(this, service, _promptForName)
        {
            CloseMenu = () => ActiveDropdown = null,
        };

        // The list on screen at launch shows its own layout: AppSettings mirrors most of it, but not the column
        // widths, the hidden columns' order, the caption lines or the tile elements.
        if (ListLayouts.Initialize() is { } json)
        {
            ApplyListLayout(ListLayoutStateJson.Deserialize(json));
        }

        _appliedLayoutKey = LayoutSelectionKey;
        ListLayouts.MarkApplied();
    }

    /// <summary>Called wherever the sidebar selection changes, before that change is saved and rendered.</summary>
    private void ApplyLayoutForSelection()
    {
        string key = LayoutSelectionKey;
        if (key == _appliedLayoutKey)
        {
            return;
        }

        _appliedLayoutKey = key;
        if (ListLayouts.ResolveForSelection() is { } json)
        {
            ListLayouts.ApplyWithoutTracking(() => ApplyListLayout(ListLayoutStateJson.Deserialize(json)));
        }
    }

    private ListLayoutState CaptureListLayout() => new(
        Columns: DetailsColumns.Count == 0
            ? null
            : DetailsColumns.Select(c => new ListLayoutColumn(c.Field, c.IsVisible, c.Width)).ToList(),
        ViewMode: ViewMode,
        CoverFit: GridCoverFit,
        CaptionFields: _captionFields,
        HideCaptions: !ShowTileTitles,
        TileElements: _tileElements,
        SortField: IssueList.SortField,
        SortDirection: IssueList.SortDirection,
        GroupField: IssueList.GroupField,
        SortVirtualTagId: IssueList.SortVirtualTagId,
        GroupVirtualTagId: IssueList.GroupVirtualTagId);

    /// <summary>
    /// Sets every layout field without saving or rendering - direct field writes, like <see cref="ApplyLibraryState"/>,
    /// so no <c>On*Changed</c> partial fires a save or a reload per field. The caller saves and renders once.
    /// </summary>
    private void ApplyListLayout(ListLayoutState s)
    {
        _applyingListLayout = true;
        try
        {
#pragma warning disable MVVMTK0034
            _viewMode = s.ViewMode;
            _gridCoverFit = s.CoverFit;
            _showTileTitles = !s.HideCaptions;
#pragma warning restore MVVMTK0034
            _captionFields = s.CaptionFields;
            _tileElements = s.TileElements;
            ApplyLayoutColumns(s.Columns);
            IssueList.ConfigureSortGroup(s.SortField, s.SortDirection, s.GroupField);
            IssueList.SortVirtualTagId = s.SortVirtualTagId;
            IssueList.GroupVirtualTagId = s.GroupVirtualTagId;
        }
        finally
        {
            _applyingListLayout = false;
        }

        RaiseListLayoutBindings();
    }

    private void RaiseListLayoutBindings()
    {
        foreach (var name in new[]
        {
            nameof(ViewMode), nameof(GridCoverFit), nameof(DisplayModeLabel),
            nameof(ShowTileTitles),
            nameof(IsGrouped), nameof(ActiveSortLabel), nameof(ActiveGroupLabel), nameof(ShowAlphabetIndex),
        })
        {
            OnPropertyChanged(name);
        }

        RaiseCaptionBindings();
        RaiseTileElementBindings();
        RaiseViewModeDerivedChanged();
        RaiseChipAndEmptyState();
    }

    // --- Details columns: order and width ---

    /// <summary>Widths the user set, kept across a column-set rebuild (a workspace apply rebuilds from the visible-name list alone).</summary>
    private readonly Dictionary<IssueListSortField, double> _detailsColumnWidths = new();

    private double DetailsColumnWidthFor(IssueListSortField field) =>
        _detailsColumnWidths.TryGetValue(field, out double width) ? width : DefaultDetailsColumnWidth(field);

    public static double DefaultDetailsColumnWidth(IssueListSortField field) => WideDetailsColumns.Contains(field) ? 220 : 150;

    /// <summary>Makes <see cref="DetailsColumns"/> match a layout's column list (null = the default set), touching the collection only where it differs.</summary>
    private void ApplyLayoutColumns(IReadOnlyList<ListLayoutColumn>? columns)
    {
        var target = new List<ListLayoutColumn>();
        if (columns is null)
        {
            _detailsColumnWidths.Clear();
            var visible = IssueListFieldCatalog.DefaultDetailsColumns;
            target.AddRange(visible.Select(f => new ListLayoutColumn(f, true, DefaultDetailsColumnWidth(f))));
        }
        else
        {
            target.AddRange(columns);
        }

        // Every column-eligible field is always present, so the pickers can offer it: the ones a layout does not name go last, hidden.
        var named = target.Select(c => c.Field).ToHashSet();
        foreach (var descriptor in IssueListFieldCatalog.ColumnFields)
        {
            if (named.Add(descriptor.Field))
            {
                target.Add(new ListLayoutColumn(descriptor.Field, false, DefaultDetailsColumnWidth(descriptor.Field)));
            }
        }

        target.RemoveAll(c => !IssueListFieldCatalog.SortFields.TryGetValue(c.Field, out var d) || d.Display is null);

        foreach (var column in target)
        {
            _detailsColumnWidths[column.Field] = column.Width;
        }

        bool sameOrder = target.Count == DetailsColumns.Count
            && target.Select(c => c.Field).SequenceEqual(DetailsColumns.Select(c => c.Field));
        if (sameOrder)
        {
            bool changed = false;
            for (int i = 0; i < target.Count; i++)
            {
                var column = DetailsColumns[i];
                changed |= column.IsVisible != target[i].Visible || !column.Width.Equals(target[i].Width);
                column.Width = target[i].Width;
                column.IsVisible = target[i].Visible;
            }

            if (changed)
            {
                DetailsColumnsChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        foreach (var column in DetailsColumns)
        {
            column.PropertyChanged -= OnDetailsColumnChanged;
        }

        DetailsColumns.Clear();
        foreach (var column in target)
        {
            AddDetailsColumn(column.Field, column.Visible);
        }

        _detailsColumnsSetting = SerializeDetailsColumns();
        DetailsColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Moves a column to another position in <see cref="DetailsColumns"/> (the Details header drag, and List Options'
    /// Move Up / Move Down). The index counts every column, hidden ones included.
    /// </summary>
    public void MoveDetailsColumn(DetailsColumn column, int newIndex)
    {
        int oldIndex = DetailsColumns.IndexOf(column);
        newIndex = Math.Clamp(newIndex, 0, DetailsColumns.Count - 1);
        if (oldIndex < 0 || oldIndex == newIndex)
        {
            return;
        }

        DetailsColumns.Move(oldIndex, newIndex);
        SaveLibrarySettings();
        DetailsColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops <paramref name="column"/> where <paramref name="target"/> is: in front of it when dragging left, behind it when dragging right.</summary>
    public void MoveDetailsColumnTo(DetailsColumn column, DetailsColumn target) =>
        MoveDetailsColumn(column, DetailsColumns.IndexOf(target));

    /// <summary>
    /// Sets a column's width. A drag calls this continuously with <paramref name="commit"/> false (the header and every
    /// realized row follow live) and once with true on release, which is the only write.
    /// </summary>
    public void SetDetailsColumnWidth(DetailsColumn column, double width, bool commit)
    {
        width = ListLayoutStateJson.ClampWidth(width);
        column.Width = width;
        _detailsColumnWidths[column.Field] = width;
        if (commit)
        {
            ListLayouts.TrackChange();
        }
    }

    // --- Thumbnail caption lines (CE ThumbnailConfig.CaptionIds) ---

    /// <summary>Null = the built-in two lines the poster cards have always shown; otherwise up to three fields, top to bottom.</summary>
    private IReadOnlyList<IssueListSortField>? _captionFields;

    public IReadOnlyList<IssueListSortField>? CaptionFields => _captionFields;

    public bool HasCustomCaptions => _captionFields is not null;

    public bool HasBuiltInCaptions => _captionFields is null;

    public IssueListSortField? CaptionField1 => CaptionFieldAt(0);

    public IssueListSortField? CaptionField2 => CaptionFieldAt(1);

    public IssueListSortField? CaptionField3 => CaptionFieldAt(2);

    private IssueListSortField? CaptionFieldAt(int index) =>
        _captionFields is { } fields && index < fields.Count ? fields[index] : null;

    /// <summary>How many caption lines a poster card reserves room for.</summary>
    private int CaptionLineCount => _captionFields?.Count ?? 2;

    private void RaiseCaptionBindings()
    {
        foreach (var name in new[]
        {
            nameof(CaptionFields), nameof(HasCustomCaptions), nameof(HasBuiltInCaptions),
            nameof(CaptionField1), nameof(CaptionField2), nameof(CaptionField3),
            nameof(PosterTitleTextHeight), nameof(EffectiveShowTileTitles), nameof(PosterCardHeight),
        })
        {
            OnPropertyChanged(name);
        }
    }

    // --- Tile text elements (CE ComicTextElements) ---

    /// <summary>Null = the two lines a tile has always shown (<see cref="ListLayoutState.DefaultTileElements"/>).</summary>
    private IReadOnlyList<TileTextElement>? _tileElements;

    public IReadOnlyList<TileTextElement> TileElements => _tileElements ?? ListLayoutState.DefaultTileElements;

    public bool TileShowsTitle => TileElements.Contains(TileTextElement.Title);

    private void RaiseTileElementBindings()
    {
        OnPropertyChanged(nameof(TileElements));
        OnPropertyChanged(nameof(TileShowsTitle));
    }

    // --- List Options overlay ---

    /// <summary>A List Options editor over the current list's layout; Apply writes back through <see cref="ApplyListOptions"/>.</summary>
    public ListOptionsViewModel CreateListOptions(Action close) =>
        ListOptionsViewModel.ForLibrary(CaptureListLayout(), IsTilesView ? "tiles" : IsGridView ? "thumbnails" : "details", ApplyListOptions, close);

    /// <summary>List Options' Apply: columns, caption lines and tile elements. Sort, group and view mode are not edited there (as in CE).</summary>
    public void ApplyListOptions(ListLayoutState edited)
    {
        var s = ListLayoutStateJson.Normalize(edited);
        _applyingListLayout = true;
        try
        {
#pragma warning disable MVVMTK0034
            _showTileTitles = !s.HideCaptions;
#pragma warning restore MVVMTK0034
            _captionFields = s.CaptionFields;
            _tileElements = s.TileElements;
            ApplyLayoutColumns(s.Columns);
        }
        finally
        {
            _applyingListLayout = false;
        }

        OnPropertyChanged(nameof(ShowTileTitles));
        RaiseCaptionBindings();
        RaiseTileElementBindings();
        SaveLibrarySettings();
    }
}
