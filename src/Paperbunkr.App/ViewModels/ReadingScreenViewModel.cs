using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
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

    [ObservableProperty]
    private string _listName = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

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

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    public void LoadReadingList(int readingListId)
    {
        _activeReadingListId = readingListId;
        StatusMessage = null;

        using var context = PaperbunkrDb.CreateContext();
        var list = context.ReadingLists
            .Include(r => r.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.Series)
            .FirstOrDefault(r => r.Id == readingListId);
        if (list is null)
        {
            return;
        }

        ListName = list.Name;
        Subtitle = "Cross-series reading order · tracked list";

        var items = list.Items.OrderBy(i => i.SortOrder).ToList();
        TotalIssues = items.Count.ToString();
        OwnedIssues = items.Count(i => i.Issue is { FileIsMissing: false }).ToString();
        MissingIssues = items.Count(i => i.Issue is null || i.Issue.FileIsMissing).ToString();

        Groups.Clear();
        foreach (var group in items.GroupBy(i => i.GroupLabel ?? string.Empty))
        {
            var rows = new ObservableCollection<ReadingListItemRowViewModel>(
                group.Select(i => new ReadingListItemRowViewModel(i, MoveItemUp, MoveItemDown, RemoveItem)));
            Groups.Add(new ReadingListGroupViewModel { Label = group.Key, Rows = rows });
        }

        RefreshSidebar();
    }

    public void EnsureListLoaded()
    {
        if (_activeReadingListId is not null)
        {
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
        context.SaveChanges();
        LoadReadingList(listId);
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
            .AsEnumerable()
            .Where(i => (i.Series?.Name ?? string.Empty).Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
                || (i.Number ?? string.Empty).Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
            .Take(20);

        foreach (var issue in matches)
        {
            SearchResults.Add(new IssueSearchResult
            {
                IssueId = issue.Id,
                DisplayLabel = $"{issue.Series?.Name ?? "Unknown"} #{issue.Number}",
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
        context.SaveChanges();

        SearchResults.Clear();
        SearchQuery = string.Empty;
        LoadReadingList(listId);
    }

    [RelayCommand]
    private void CreateNew()
    {
        using var context = PaperbunkrDb.CreateContext();
        var list = new ReadingList { Name = "New Reading List", SortOrder = context.ReadingLists.Count() };
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
