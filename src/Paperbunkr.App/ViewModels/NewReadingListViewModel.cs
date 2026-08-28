using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The "New Reading List" dialog (docs/superpowers/specs/2026-08-28-reading-lists-screen-redesign-
/// design.md → v2 revisions). Name + one of four build methods. On a successful create it invokes
/// <c>onCreated(listId)</c>, which <see cref="MainViewModel"/> wires to close the overlay and load
/// the new list.
/// </summary>
public partial class NewReadingListViewModel : ViewModelBase
{
    public enum BuildMethod { Blank, Import, Arc, Event }

    private const string DefaultName = "New Reading List";

    private readonly IFilePickerService _filePicker;
    private readonly Action<int> _onCreated;
    private readonly Action _onCancel;

    public NewReadingListViewModel(IFilePickerService filePicker, Action<int> onCreated, Action onCancel)
    {
        _filePicker = filePicker;
        _onCreated = onCreated;
        _onCancel = onCancel;
        ArcSourceOptions = ReadingScreenViewModel.ArcSourceOptions;
        _selectedArcSource = ArcSourceOptions[0];
    }

    [RelayCommand]
    private void Cancel() => _onCancel();

    /// <summary>Resets the dialog to its initial state each time it's opened.</summary>
    public void Reset()
    {
        Name = DefaultName;
        SelectedMethod = null;
        StatusMessage = null;
        ArcSearchQuery = string.Empty;
        ArcSearchResults.Clear();
        SelectedStoryEvent = null;
        StoryEventOptions.Clear();
        using var context = PaperbunkrDb.CreateContext();
        foreach (var e in context.StoryEvents.OrderBy(e => e.Name).Select(e => new StoryEventOption(e.Id, e.Name)))
        {
            StoryEventOptions.Add(e);
        }
    }

    [ObservableProperty]
    private string _name = DefaultName;

    [ObservableProperty]
    private BuildMethod? _selectedMethod;

    partial void OnSelectedMethodChanged(BuildMethod? value)
    {
        OnPropertyChanged(nameof(IsBlankMethod));
        OnPropertyChanged(nameof(IsImportMethod));
        OnPropertyChanged(nameof(IsArcMethod));
        OnPropertyChanged(nameof(IsEventMethod));
        OnPropertyChanged(nameof(CanCreate));
    }

    public bool IsBlankMethod => SelectedMethod == BuildMethod.Blank;
    public bool IsImportMethod => SelectedMethod == BuildMethod.Import;
    public bool IsArcMethod => SelectedMethod == BuildMethod.Arc;
    public bool IsEventMethod => SelectedMethod == BuildMethod.Event;

    [RelayCommand]
    private void SelectMethod(string method) =>
        SelectedMethod = Enum.Parse<BuildMethod>(method, ignoreCase: true);

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>The Create button is inert for Arc (each result has its own "Use") - it drives Blank/Import/Event.</summary>
    public bool CanCreate => SelectedMethod is BuildMethod.Blank or BuildMethod.Import
        || (SelectedMethod == BuildMethod.Event && SelectedStoryEvent is not null);

    [RelayCommand]
    private async Task Create()
    {
        switch (SelectedMethod)
        {
            case BuildMethod.Blank:
                CreateBlank();
                break;
            case BuildMethod.Import:
                await ImportFileAsync();
                break;
            case BuildMethod.Event:
                CreateFromEvent();
                break;
        }
    }

    private string ResolvedName => string.IsNullOrWhiteSpace(Name) ? DefaultName : Name.Trim();

    private bool NameWasCustomised => !string.Equals(ResolvedName, DefaultName, StringComparison.Ordinal);

    private void CreateBlank()
    {
        using var context = PaperbunkrDb.CreateContext();
        var now = DateTime.UtcNow;
        var list = new ReadingList
        {
            Name = ResolvedName,
            SortOrder = context.ReadingLists.Count(),
            Type = ReadingListType.User,
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.ReadingLists.Add(list);
        context.SaveChanges();
        _onCreated(list.Id);
    }

    private async Task ImportFileAsync()
    {
        string? path = await _filePicker.PickOpenFileAsync("Import Reading List", "cbl", "Reading List (.cbl / .csv)");
        if (path is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        int listId;
        if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            var result = CsvReadingListIO.Import(context, path, NameWasCustomised ? ResolvedName : null);
            listId = result.List.Id;
        }
        else
        {
            var list = CblReadingListIO.Import(context, path);
            if (NameWasCustomised)
            {
                list.Name = ResolvedName;
                context.SaveChanges();
            }

            listId = list.Id;
        }

        _onCreated(listId);
    }

    private void CreateFromEvent()
    {
        if (SelectedStoryEvent is not { } option)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var storyEvent = context.StoryEvents
            .Include(e => e.Members)
            .FirstOrDefault(e => e.Id == option.Id);
        if (storyEvent is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var list = new ReadingList
        {
            Name = NameWasCustomised ? ResolvedName : storyEvent.Name,
            SortOrder = context.ReadingLists.Count(),
            Type = ReadingListType.Event,
            StoryEventId = storyEvent.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };

        int sortOrder = 0;
        foreach (var member in storyEvent.Members.OrderBy(m => m.Position))
        {
            list.Items.Add(new ReadingListItem { IssueId = member.IssueId, SortOrder = sortOrder++, Role = member.Role });
        }

        context.ReadingLists.Add(list);
        context.SaveChanges();
        _onCreated(list.Id);
    }

    // --- Story event picker ---

    public ObservableCollection<StoryEventOption> StoryEventOptions { get; } = new();

    [ObservableProperty]
    private StoryEventOption? _selectedStoryEvent;

    partial void OnSelectedStoryEventChanged(StoryEventOption? value) => OnPropertyChanged(nameof(CanCreate));

    // --- Story arc search (its own minimal copy - screen-state on ReadingScreenViewModel isn't reusable here) ---

    public ArcSourceOption[] ArcSourceOptions { get; }

    [ObservableProperty]
    private ArcSourceOption _selectedArcSource;

    [ObservableProperty]
    private string _arcSearchQuery = string.Empty;

    public ObservableCollection<ArcSearchResultRow> ArcSearchResults { get; } = new();

    [RelayCommand]
    private async Task SearchArc()
    {
        if (string.IsNullOrWhiteSpace(ArcSearchQuery))
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var source = ReadingListSourceRegistry.Get(context, SelectedArcSource.Key);
        if (source is null)
        {
            StatusMessage = $"{SelectedArcSource.DisplayName} needs credentials — set them in Preferences → Connections.";
            return;
        }

        ArcSearchResults.Clear();
        StatusMessage = "Searching…";
        try
        {
            var results = await source.SearchAsync(ArcSearchQuery, CancellationToken.None);
            foreach (var result in results)
            {
                ArcSearchResults.Add(new ArcSearchResultRow(result));
            }

            StatusMessage = results.Count == 0 ? "No story arcs found." : null;
        }
        catch (ReadingListSourceException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task UseArc(ArcSearchResultRow? row)
    {
        if (row is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var source = ReadingListSourceRegistry.Get(context, SelectedArcSource.Key);
        if (source is null)
        {
            return;
        }

        try
        {
            var list = await ArcReadingListBuilder.CreateFromArcAsync(context, source, row.Result, CancellationToken.None);
            if (NameWasCustomised)
            {
                list.Name = ResolvedName;
                context.SaveChanges();
            }

            _onCreated(list.Id);
        }
        catch (Exception ex) when (ex is ReadingListSourceException or InvalidOperationException)
        {
            StatusMessage = ex.Message;
        }
    }

    public sealed record StoryEventOption(int Id, string Name)
    {
        public override string ToString() => Name;
    }
}
