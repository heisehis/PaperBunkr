using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

public sealed partial class DetailListOptionField : ObservableObject
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public bool IsColumn { get; init; }

    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    private double _width;
}

/// <summary>
/// Editable draft of a detail layout (docs/superpowers/specs/2026-10-19-detail-layouts-v2-design.md). Comic: Poster captions and
/// overlays, List columns, Card elements, cover fit, sort and group. Manga: chapter columns, sort and group. Every field comes from
/// the Library's catalog, so the pickers offer what the Library's List Options offer.
/// </summary>
public partial class DetailListOptionsViewModel : ObservableObject
{
    public const string NoCaption = "(none)";

    private readonly bool _isComic;
    private readonly DetailListLayoutState _source;
    private readonly Action<DetailListLayoutState> _apply;
    private readonly Action _close;
    private readonly Dictionary<string, (IssueListSortField Field, int? VirtualTagId)> _sortByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _groupByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IssueListSortField> _captionByName = new(StringComparer.OrdinalIgnoreCase);

    private DetailListOptionsViewModel(bool isComic, DetailListLayoutState source, IReadOnlyList<VirtualTagDefinition> virtualTags,
        Action<DetailListLayoutState> apply, Action close)
    {
        _isComic = isComic;
        _source = DetailListLayoutStateJson.Normalize(source, manga: !isComic);
        _apply = apply;
        _close = close;
        ComicColumns = new ObservableCollection<DetailListOptionField>();
        CardElements = new ObservableCollection<DetailListOptionField>();
        ChapterColumns = new ObservableCollection<DetailListOptionField>();

        foreach (var descriptor in IssueListFieldCatalog.ColumnFields)
        {
            _captionByName[descriptor.DisplayName] = descriptor.Field;
        }

        CaptionChoices = new[] { NoCaption }.Concat(IssueListFieldCatalog.ColumnFields.Select(d => d.DisplayName)).ToArray();

        foreach (var descriptor in IssueListFieldCatalog.SortFields.Values.OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            _sortByName[descriptor.DisplayName] = (descriptor.Field, null);
        }

        foreach (var tag in virtualTags)
        {
            _sortByName.TryAdd($"Virtual tag: {tag.Name}", (IssueListSortField.VirtualTag, tag.Id));
        }

        SortChoices = _sortByName.Keys.ToArray();

        _groupByName[isComic ? "Run (volume)" : "Volume"] = isComic ? DetailGrouping.Run : DetailGrouping.Volume;
        _groupByName["No grouping"] = DetailGrouping.None;
        foreach (var descriptor in IssueListFieldCatalog.GroupFields.Values.OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            _groupByName.TryAdd(descriptor.DisplayName, DetailGrouping.ForField(descriptor.Field));
        }

        foreach (var tag in virtualTags)
        {
            _groupByName.TryAdd($"Virtual tag: {tag.Name}", DetailGrouping.ForVirtualTag(tag.Id));
        }

        GroupChoices = _groupByName.Keys.ToArray();
        Populate();
    }

    public static DetailListOptionsViewModel ForComic(DetailListLayoutState state, IReadOnlyList<VirtualTagDefinition> virtualTags,
        Action<DetailListLayoutState> apply, Action close) =>
        new(true, state, virtualTags, apply, close) { ActiveTab = state.ViewMode.ToString().ToLowerInvariant() };

    public static DetailListOptionsViewModel ForManga(DetailListLayoutState state, IReadOnlyList<VirtualTagDefinition> virtualTags,
        Action<DetailListLayoutState> apply, Action close) =>
        new(false, state, virtualTags, apply, close) { ActiveTab = "chapters" };

    public bool IsComic => _isComic;
    public bool IsManga => !_isComic;
    public ObservableCollection<DetailListOptionField> ComicColumns { get; }
    public ObservableCollection<DetailListOptionField> CardElements { get; }
    public ObservableCollection<DetailListOptionField> ChapterColumns { get; }

    public string[] CaptionChoices { get; }
    public string[] SortChoices { get; }
    public string[] GroupChoices { get; }
    public string[] CoverFitChoices { get; } = Enum.GetNames<LibraryGridCoverFit>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPosterTab), nameof(IsListTab), nameof(IsCardTab), nameof(IsChaptersTab), nameof(IsSortTab))]
    private string _activeTab = "poster";

    public bool IsPosterTab => IsComic && ActiveTab == "poster";
    public bool IsListTab => IsComic && ActiveTab == "list";
    public bool IsCardTab => IsComic && ActiveTab == "card";
    public bool IsChaptersTab => IsManga && ActiveTab == "chapters";
    public bool IsSortTab => ActiveTab == "sort";

    [ObservableProperty]
    private DetailListOptionField? _selectedField;

    [ObservableProperty]
    private DetailListOptionField? _selectedElement;

    [ObservableProperty]
    private string _caption1 = NoCaption;

    [ObservableProperty]
    private string _caption2 = NoCaption;

    [ObservableProperty]
    private string _caption3 = NoCaption;

    [ObservableProperty]
    private bool _hideCaptions;

    [ObservableProperty]
    private bool _showReadOverlay;

    [ObservableProperty]
    private bool _showEditionOverlay;

    [ObservableProperty]
    private bool _showCardStatus;

    [ObservableProperty]
    private string _coverFit = nameof(LibraryGridCoverFit.Poster);

    [ObservableProperty]
    private string _sortBy = string.Empty;

    [ObservableProperty]
    private bool _sortDescending;

    [ObservableProperty]
    private string _groupBy = string.Empty;

    [RelayCommand]
    private void SelectTab(string tab) => ActiveTab = tab;

    [RelayCommand]
    private void MoveFieldUp() => MoveSelected(CurrentColumnList(), SelectedField, -1, f => SelectedField = f);

    [RelayCommand]
    private void MoveFieldDown() => MoveSelected(CurrentColumnList(), SelectedField, +1, f => SelectedField = f);

    [RelayCommand]
    private void MoveElementUp() => MoveSelected(CardElements, SelectedElement, -1, f => SelectedElement = f);

    [RelayCommand]
    private void MoveElementDown() => MoveSelected(CardElements, SelectedElement, +1, f => SelectedElement = f);

    [RelayCommand]
    private void ShowAllColumns()
    {
        foreach (var field in CurrentColumnList()) field.IsChecked = true;
    }

    [RelayCommand]
    private void HideAllColumns()
    {
        foreach (var field in CurrentColumnList()) field.IsChecked = false;
    }

    [RelayCommand]
    private void Apply() => _apply(BuildState());

    [RelayCommand]
    private void Ok()
    {
        Apply();
        _close();
    }

    [RelayCommand]
    private void Cancel() => _close();

    /// <summary>The edited layout. Internal for tests.</summary>
    internal DetailListLayoutState BuildState()
    {
        var (sortField, sortTag) = _sortByName.TryGetValue(SortBy, out var sort) ? sort : (IssueListSortField.Number, (int?)null);
        string group = _groupByName.TryGetValue(GroupBy, out var g) ? g : (_isComic ? DetailGrouping.Run : DetailGrouping.Volume);
        var direction = SortDescending ? SortDirection.Descending : SortDirection.Ascending;

        if (_isComic)
        {
            var captions = new[] { Caption1, Caption2, Caption3 }
                .Where(c => _captionByName.ContainsKey(c))
                .Select(c => _captionByName[c])
                .ToList();
            return DetailListLayoutStateJson.Normalize(_source with
            {
                CaptionFields = captions,
                HideCaptions = HideCaptions,
                ShowReadOverlay = ShowReadOverlay,
                ShowEditionOverlay = ShowEditionOverlay,
                ComicColumns = SavedColumns(ComicColumns, _source.ComicColumns!),
                CardElements = CardElements.Where(f => f.IsChecked).Select(f => Enum.Parse<TileTextElement>(f.Key)).ToList(),
                ShowCardStatus = ShowCardStatus,
                CoverFit = Enum.TryParse<LibraryGridCoverFit>(CoverFit, out var fit) ? fit : LibraryGridCoverFit.Poster,
                SortField = sortField,
                SortVirtualTagId = sortTag,
                SortDirection = direction,
                GroupBy = group,
            });
        }

        return DetailListLayoutStateJson.Normalize(_source with
        {
            ChapterColumns = SavedColumns(ChapterColumns, _source.ChapterColumns!),
            ChapterSortBy = sortField,
            ChapterSortVirtualTagId = sortTag,
            ChapterSortDirection = direction,
            ChapterGroupBy = group,
        }, manga: true);
    }

    private ObservableCollection<DetailListOptionField> CurrentColumnList() => _isComic ? ComicColumns : ChapterColumns;

    /// <summary>
    /// What the list saves: every ticked column, plus the hidden ones the layout already had. The list offers all ~60 fields, but writing
    /// each unticked one back would bloat the layout and make an untouched OK look like an edit.
    /// </summary>
    private static IReadOnlyList<DetailListColumn> SavedColumns(IEnumerable<DetailListOptionField> fields, IReadOnlyList<DetailListColumn> source)
    {
        var known = source.Select(c => c.Field).ToHashSet(StringComparer.Ordinal);
        return fields.Where(f => f.IsChecked || known.Contains(f.Key)).Select(f => new DetailListColumn(f.Key, f.IsChecked, f.Width)).ToList();
    }

    private static void MoveSelected(ObservableCollection<DetailListOptionField> list, DetailListOptionField? field, int delta, Action<DetailListOptionField> reselect)
    {
        if (field is null) return;
        int index = list.IndexOf(field);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= list.Count) return;
        list.Move(index, target);
        reselect(field);
    }

    private void Populate()
    {
        PopulateColumns(ComicColumns, _source.ComicColumns!, manga: false);
        PopulateColumns(ChapterColumns, _source.ChapterColumns!, manga: true);

        var captions = _source.CaptionFields ?? Array.Empty<IssueListSortField>();
        Caption1 = CaptionName(captions, 0);
        Caption2 = CaptionName(captions, 1);
        Caption3 = CaptionName(captions, 2);
        HideCaptions = _source.HideCaptions;
        ShowReadOverlay = _source.ShowReadOverlay;
        ShowEditionOverlay = _source.ShowEditionOverlay;
        ShowCardStatus = _source.ShowCardStatus;
        CoverFit = _source.CoverFit.ToString();

        // Card elements: the chosen ones in their order, then the rest unchecked. Summary is a series-tile line, never on an issue card.
        var chosen = _source.CardElements ?? Array.Empty<TileTextElement>();
        foreach (var element in chosen.Concat(Enum.GetValues<TileTextElement>().Where(e => e != TileTextElement.Summary && !chosen.Contains(e))))
        {
            CardElements.Add(new DetailListOptionField { Key = element.ToString(), Name = ElementName(element), IsChecked = chosen.Contains(element) });
        }

        var sortField = _isComic ? _source.SortField : _source.ChapterSortBy;
        int? sortTag = _isComic ? _source.SortVirtualTagId : _source.ChapterSortVirtualTagId;
        SortBy = _sortByName.FirstOrDefault(p => p.Value.Field == sortField && p.Value.VirtualTagId == sortTag).Key
            ?? IssueListFieldCatalog.SortFields[IssueListSortField.Number].DisplayName;
        SortDescending = (_isComic ? _source.SortDirection : _source.ChapterSortDirection) == SortDirection.Descending;
        string group = _isComic ? _source.GroupBy : _source.ChapterGroupBy;
        GroupBy = _groupByName.FirstOrDefault(p => p.Value == group).Key ?? _groupByName.Keys.First();

        SelectedField = CurrentColumnList().FirstOrDefault();
        SelectedElement = CardElements.FirstOrDefault();
    }

    /// <summary>The saved columns in their order, then every other eligible field hidden, so the list offers all of them.</summary>
    private static void PopulateColumns(ObservableCollection<DetailListOptionField> target, IReadOnlyList<DetailListColumn> saved, bool manga)
    {
        foreach (var column in saved)
        {
            target.Add(new DetailListOptionField { Key = column.Field, Name = DetailColumnKeys.DisplayName(column.Field, manga), IsColumn = true, IsChecked = column.Visible, Width = column.Width });
        }

        var present = saved.Select(c => c.Field).ToHashSet(StringComparer.Ordinal);
        var extras = (manga ? DetailColumnKeys.MangaOnly : DetailColumnKeys.ComicOnly)
            .Concat(DetailColumnKeys.CatalogColumns.Select(f => f.ToString()));
        foreach (string key in extras)
        {
            if (present.Add(key))
            {
                double width = DetailColumnKeys.CatalogField(key) is { } field ? LibraryScreenViewModel.DefaultDetailsColumnWidth(field) : 60;
                target.Add(new DetailListOptionField { Key = key, Name = DetailColumnKeys.DisplayName(key, manga), IsColumn = true, IsChecked = false, Width = width });
            }
        }
    }

    private static string CaptionName(IReadOnlyList<IssueListSortField> captions, int index) =>
        index < captions.Count && IssueListFieldCatalog.SortFields.TryGetValue(captions[index], out var d) ? d.DisplayName : NoCaption;

    private static string ElementName(TileTextElement element) => element switch
    {
        TileTextElement.Title => "Title (headline)",
        TileTextElement.Number => "Issue number",
        TileTextElement.AgeRating => "Age rating",
        TileTextElement.PageCount => "Page count",
        TileTextElement.ReadPercentage => "Read %",
        TileTextElement.FileSize => "File size",
        TileTextElement.StoryArc => "Story arc",
        _ => element.ToString(),
    };
}
