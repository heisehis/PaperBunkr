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
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Reading Lists screen, ported from ReadingScreen.dc.html (Claude Design project 43c40b25). Loads
/// a real <see cref="ReadingList"/> (via <see cref="LoadReadingList"/>/<see cref="EnsureListLoaded"/>)
/// instead of the wireframe's hardcoded "Kilo Station: Signal War" sample content — see
/// docs/superpowers/specs/2026-08-06-reading-lists-design.md. Unlike Smart Lists, edits persist
/// immediately (add/remove/reorder each call SaveChanges) rather than through a Save/Cancel draft —
/// the wireframe's header only ever showed Share/Refresh, no Save, for this screen.
/// </summary>
public partial class ReadingScreenViewModel : ViewModelBase
{
    private readonly IFilePickerService _filePicker;
    private int? _activeReadingListId;
    private bool _isLoadingList;

    public ReadingScreenViewModel(IFilePickerService filePicker)
    {
        _filePicker = filePicker;
        Lists = new ObservableCollection<ReadingListSummary>();
        Groups = new ObservableCollection<ReadingListGroupViewModel>();
        SearchResults = new ObservableCollection<IssueSearchResult>();
        RefreshSidebar();
    }

    public ObservableCollection<ReadingListSummary> Lists { get; }
    public ObservableCollection<ReadingListGroupViewModel> Groups { get; }
    public ObservableCollection<IssueSearchResult> SearchResults { get; }
    public ObservableCollection<StoryEventSearchResult> StoryEventSearchResults { get; } = new();

    /// <summary>Phase 4c overhaul (docs/superpowers/specs/2026-08-17-metadata-model-phase4c-reading-list-overhaul-design.md).</summary>
    public static ReadingListTypeOption[] TypeOptions => ReadingListTypeOption.All;

    [ObservableProperty]
    private string _listName = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private ReadingListType _selectedType = ReadingListType.User;

    /// <summary>
    /// Bound to the ComboBox's <c>SelectedItem</c> instead of <c>SelectedValue</c>/
    /// <c>SelectedValueBinding</c> - the latter resolves its binding path against this
    /// ViewModel's own DataContext, not the <c>ItemsSource</c> element type, so `{Binding Type}`
    /// there was silently unresolvable (a real, permanent XAML bug, not a build-tooling artifact -
    /// see docs/superpowers/specs/2026-08-18-selectedvaluebinding-xaml-fix-design.md).
    /// </summary>
    [ObservableProperty]
    private ReadingListTypeOption _selectedTypeOption = TypeOptions.First(o => o.Type == ReadingListType.User);

    partial void OnSelectedTypeOptionChanged(ReadingListTypeOption value) => SelectedType = value.Type;

    partial void OnSelectedTypeChanged(ReadingListType value)
    {
        if (!_isLoadingList)
        {
            PersistTypeChange(value);
        }
    }

    [ObservableProperty]
    private string? _linkedStoryEventName;

    [ObservableProperty]
    private string _createdAtLabel = string.Empty;

    [ObservableProperty]
    private bool _isLinkingStoryEvent;

    [ObservableProperty]
    private string _storyEventSearchQuery = string.Empty;

    [ObservableProperty]
    private string _totalIssues = "0";

    [ObservableProperty]
    private string _ownedIssues = "0";

    [ObservableProperty]
    private string _missingIssues = "0";

    [ObservableProperty]
    private string? _statusMessage;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatusMessage));

    /// <summary>Real empty states (P6, docs/alpha-todo.md) - previously a fresh install or a database with no reading lists just rendered a blank header and zeroed stat cards, with nothing telling the user what to do.</summary>
    public bool HasNoReadingLists => Lists.Count == 0;

    public bool HasNoItems => !HasNoReadingLists && Groups.Count == 0;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    public void LoadReadingList(int readingListId)
    {
        _activeReadingListId = readingListId;
        StatusMessage = null;

        using var context = PaperbunkrDb.CreateContext();
        var list = context.ReadingLists
            .Include(r => r.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.Series)
            .Include(r => r.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.MetadataProposals)
            .Include(r => r.StoryEvent)
            .FirstOrDefault(r => r.Id == readingListId);
        if (list is null)
        {
            return;
        }

        ListName = list.Name;
        Subtitle = "Cross-series reading order · tracked list";

        _isLoadingList = true;
        SelectedType = list.Type;
        SelectedTypeOption = TypeOptions.First(o => o.Type == list.Type);
        _isLoadingList = false;
        LinkedStoryEventName = list.StoryEvent?.Name;
        CreatedAtLabel = $"Created {list.CreatedAt:MMM d, yyyy}";

        var items = list.Items.OrderBy(i => i.SortOrder).ToList();
        TotalIssues = items.Count.ToString();
        OwnedIssues = items.Count(i => i.Issue is { FileIsMissing: false }).ToString();
        MissingIssues = items.Count(i => i.Issue is null || i.Issue.FileIsMissing).ToString();

        Groups.Clear();
        foreach (var group in items.GroupBy(i => i.GroupLabel ?? string.Empty))
        {
            var rows = new ObservableCollection<ReadingListItemRowViewModel>(
                group.Select(i => new ReadingListItemRowViewModel(i, MoveItemUp, MoveItemDown, RemoveItem, PersistFieldChange)));
            Groups.Add(new ReadingListGroupViewModel { Label = group.Key, Rows = rows });
        }

        RefreshSidebar();
    }

    /// <summary>
    /// Called on every navigation to the Reading screen. Re-loads the already-active list (so it
    /// reflects whatever changed elsewhere - e.g. a CE migration - since the last visit) or, on
    /// first visit, opens the first reading list.
    /// </summary>
    public void EnsureListLoaded()
    {
        if (_activeReadingListId is int activeId)
        {
            LoadReadingList(activeId);
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var firstId = context.ReadingLists.OrderBy(r => r.SortOrder).Select(r => (int?)r.Id).FirstOrDefault();
        if (firstId is int id)
        {
            LoadReadingList(id);
        }
    }

    public void RefreshSidebar()
    {
        using var context = PaperbunkrDb.CreateContext();
        var all = context.ReadingLists.Include(r => r.Items).OrderBy(r => r.SortOrder).ToList();

        Lists.Clear();
        foreach (var list in all)
        {
            Lists.Add(new ReadingListSummary
            {
                Id = list.Id,
                Name = list.Name,
                TotalCount = list.Items.Count,
                IsActive = list.Id == _activeReadingListId,
            });
        }

        OnPropertyChanged(nameof(HasNoReadingLists));
        OnPropertyChanged(nameof(HasNoItems));
    }

    // --- Phase 4c overhaul (docs/superpowers/specs/2026-08-17-metadata-model-phase4c-reading-
    // list-overhaul-design.md): Type/StoryEvent link/per-item Role+Notes, all persisting
    // immediately like every other edit on this screen. ---

    private void PersistTypeChange(ReadingListType value)
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var list = context.ReadingLists.Find(listId);
        if (list is null)
        {
            return;
        }

        list.Type = value;
        list.UpdatedAt = DateTime.UtcNow;
        context.SaveChanges();
    }

    private void PersistFieldChange(ReadingListItemRowViewModel row)
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var item = context.ReadingListItems.Find(row.Item.Id);
        var list = context.ReadingLists.Find(listId);
        if (item is null || list is null)
        {
            return;
        }

        item.Role = row.SelectedRole;
        item.Notes = row.Notes;
        list.UpdatedAt = DateTime.UtcNow;
        context.SaveChanges();
    }

    [RelayCommand]
    private void ToggleLinkStoryEvent()
    {
        IsLinkingStoryEvent = !IsLinkingStoryEvent;
        StoryEventSearchQuery = string.Empty;
        StoryEventSearchResults.Clear();
    }

    partial void OnStoryEventSearchQueryChanged(string value) => SearchStoryEvents();

    [RelayCommand]
    private void SearchStoryEvents()
    {
        StoryEventSearchResults.Clear();
        if (string.IsNullOrWhiteSpace(StoryEventSearchQuery))
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var matches = context.StoryEvents
            .AsEnumerable()
            .Where(e => e.Name.Contains(StoryEventSearchQuery, StringComparison.OrdinalIgnoreCase))
            .Take(20);

        foreach (var storyEvent in matches)
        {
            StoryEventSearchResults.Add(new StoryEventSearchResult { StoryEventId = storyEvent.Id, Name = storyEvent.Name });
        }
    }

    [RelayCommand]
    private void LinkStoryEvent(StoryEventSearchResult? target)
    {
        if (target is null || _activeReadingListId is not int listId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var list = context.ReadingLists.Find(listId);
        if (list is null)
        {
            return;
        }

        list.StoryEventId = target.StoryEventId;
        list.UpdatedAt = DateTime.UtcNow;
        context.SaveChanges();

        IsLinkingStoryEvent = false;
        StoryEventSearchQuery = string.Empty;
        StoryEventSearchResults.Clear();
        LoadReadingList(listId);
    }

    [RelayCommand]
    private void UnlinkStoryEvent()
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var list = context.ReadingLists.Find(listId);
        if (list is null)
        {
            return;
        }

        list.StoryEventId = null;
        list.UpdatedAt = DateTime.UtcNow;
        context.SaveChanges();
        LoadReadingList(listId);
    }

    private void MoveItemUp(ReadingListItemRowViewModel row) => Reorder(row, offset: -1);

    private void MoveItemDown(ReadingListItemRowViewModel row) => Reorder(row, offset: 1);

    private void Reorder(ReadingListItemRowViewModel row, int offset)
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var items = context.ReadingListItems.Where(i => i.ReadingListId == listId).OrderBy(i => i.SortOrder).ToList();
        int index = items.FindIndex(i => i.Id == row.Item.Id);
        int swapWith = index + offset;
        if (index < 0 || swapWith < 0 || swapWith >= items.Count)
        {
            return;
        }

        (items[index].SortOrder, items[swapWith].SortOrder) = (items[swapWith].SortOrder, items[index].SortOrder);
        BumpUpdatedAt(context, listId);
        context.SaveChanges();
        LoadReadingList(listId);
    }

    private void RemoveItem(ReadingListItemRowViewModel row)
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var item = context.ReadingListItems.FirstOrDefault(i => i.Id == row.Item.Id);
        if (item is null)
        {
            return;
        }

        context.ReadingListItems.Remove(item);
        BumpUpdatedAt(context, listId);
        context.SaveChanges();
        LoadReadingList(listId);
    }

    private static void BumpUpdatedAt(PaperbunkrDbContext context, int listId)
    {
        var list = context.ReadingLists.Find(listId);
        if (list is not null)
        {
            list.UpdatedAt = DateTime.UtcNow;
        }
    }

    [RelayCommand]
    private void Search()
    {
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var matches = context.Issues
            .Include(i => i.Series)
            .Include(i => i.MetadataProposals)
            .AsEnumerable()
            .Where(i => (i.Series?.Name ?? string.Empty).Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
                || (i.EffectiveNumber() ?? string.Empty).Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
            .Take(20);

        foreach (var issue in matches)
        {
            SearchResults.Add(new IssueSearchResult
            {
                IssueId = issue.Id,
                DisplayLabel = $"{issue.Series?.Name ?? "Unknown"} #{issue.EffectiveNumber()}",
            });
        }
    }

    [RelayCommand]
    private void AddIssue(IssueSearchResult? result)
    {
        if (result is null || _activeReadingListId is not int listId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        int nextOrder = context.ReadingListItems.Where(i => i.ReadingListId == listId).Select(i => (int?)i.SortOrder).Max() is int max ? max + 1 : 0;
        context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = result.IssueId, SortOrder = nextOrder });
        BumpUpdatedAt(context, listId);
        context.SaveChanges();

        SearchResults.Clear();
        SearchQuery = string.Empty;
        LoadReadingList(listId);
    }

    [RelayCommand]
    private void CreateNew()
    {
        using var context = PaperbunkrDb.CreateContext();
        var now = DateTime.UtcNow;
        var list = new ReadingList { Name = "New Reading List", SortOrder = context.ReadingLists.Count(), Type = ReadingListType.User, CreatedAt = now, UpdatedAt = now };
        context.ReadingLists.Add(list);
        context.SaveChanges();
        LoadReadingList(list.Id);
    }

    [RelayCommand]
    private void SelectList(ReadingListSummary? summary)
    {
        if (summary is not null)
        {
            LoadReadingList(summary.Id);
        }
    }

    [RelayCommand]
    private async Task ImportCbl()
    {
        string? path = await _filePicker.PickOpenFileAsync("Import CBL Reading List", "cbl", "CBL Reading List");
        if (path is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var list = CblReadingListIO.Import(context, path);
        StatusMessage = $"Imported '{list.Name}' with {list.Items.Count} issues.";
        LoadReadingList(list.Id);
    }

    [RelayCommand]
    private async Task ImportCsv()
    {
        string? path = await _filePicker.PickOpenFileAsync("Import CSV Reading List", "csv", "CSV Reading List");
        if (path is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var result = CsvReadingListIO.Import(context, path);
        StatusMessage = result.SkippedRows.Count == 0
            ? $"Imported '{result.List.Name}': {result.OwnedCount} owned, {result.PlaceholderCount} missing."
            : $"Imported '{result.List.Name}': {result.OwnedCount} owned, {result.PlaceholderCount} missing, {result.SkippedRows.Count} row(s) skipped.";
        LoadReadingList(result.List.Id);
    }

    [RelayCommand]
    private async Task ExportCbl()
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        string? path = await _filePicker.PickSaveFileAsync("Export CBL Reading List", $"{ListName}.cbl", "cbl", "CBL Reading List");
        if (path is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        CblReadingListIO.Export(context, listId, path);
        StatusMessage = $"Exported to {path}.";
    }

    [RelayCommand]
    private async Task ExportAsText()
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        string text = ReadingListTextExporter.Export(context, listId);

        string? path = await _filePicker.PickSaveFileAsync("Export Reading List as Text", $"{ListName}.txt", "txt", "Text File");
        if (path is not null)
        {
            await System.IO.File.WriteAllTextAsync(path, text);
            StatusMessage = $"Exported to {path}.";
        }
    }

    [RelayCommand]
    private async Task CopyAsText()
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        string text = ReadingListTextExporter.Export(context, listId);
        await _filePicker.SetClipboardTextAsync(text);
        StatusMessage = "Copied to clipboard.";
    }
}
