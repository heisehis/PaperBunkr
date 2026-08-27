using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists;
using Paperbunkr.Data.ReadingLists.Sources;

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
    private readonly Action<int, int> _goReaderForIssueInReadingList;
    private readonly Action<int> _openProperties;
    private int? _activeReadingListId;
    private IReadingListSource? _currentArcSource;
    private string? _currentArcSourceKey;

    /// <summary>All lists from the last <see cref="RefreshSidebar"/> query, before any tag filter - <see cref="Lists"/> is the filtered view actually shown.</summary>
    private List<ReadingListSummary> _allListSummaries = new();

    public ReadingScreenViewModel(IFilePickerService filePicker, Action<int, int> goReaderForIssueInReadingList, Action<int>? openProperties = null)
    {
        _filePicker = filePicker;
        _goReaderForIssueInReadingList = goReaderForIssueInReadingList;
        _openProperties = openProperties ?? (_ => { });
        Lists = new ObservableCollection<ReadingListSummary>();
        Groups = new ObservableCollection<ReadingListGroupViewModel>();
        SearchResults = new ObservableCollection<IssueSearchResult>();
        RefreshSidebar();
    }

    public ObservableCollection<ReadingListSummary> Lists { get; }
    public ObservableCollection<ReadingListGroupViewModel> Groups { get; }
    public ObservableCollection<IssueSearchResult> SearchResults { get; }
    public ObservableCollection<StoryEventSearchResult> StoryEventSearchResults { get; } = new();

    /// <summary>Weighted/categorized tags on the active list itself (docs/superpowers/specs/2026-08-23-reading-list-tags-design.md) - distinct from its member issues' own IssueTags. CanReweight is always true here (a Reading List is always one concrete list, no series-vs-issue ambiguity).</summary>
    public ObservableCollection<TagPillViewModel> Tags { get; } = new();

    [RelayCommand]
    private void OpenProperties()
    {
        if (_activeReadingListId is int listId)
        {
            _openProperties(listId);
        }
    }

    [ObservableProperty]
    private string _listName = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    /// <summary>Read-only display now - editing moved to the properties overlay (docs/superpowers/specs/2026-08-23-reading-list-tags-design.md); this was the one field on this screen with real inline immediate-persist before.</summary>
    [ObservableProperty]
    private string _typeLabel = string.Empty;

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

    /// <summary>
    /// Live search, same as <see cref="OnStoryEventSearchQueryChanged"/> - the explicit
    /// <see cref="SearchCommand"/>/button stays as a manual re-trigger, but typing alone is now
    /// enough to see matches, which matters most for <see cref="LinkingRow"/> (relink is otherwise
    /// a dead text box with no visible way to pick a comic until a separate click).
    /// </summary>
    partial void OnSearchQueryChanged(string value) => Search();

    /// <summary>Whether the active list is linked to an external arc source (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md §5) - governs the Refresh button's visibility.</summary>
    [ObservableProperty]
    private bool _isArcLinked;

    /// <summary>"via ComicVine" etc. - null unless <see cref="IsArcLinked"/> (docs/superpowers/specs/2026-08-22-cbl-manager-arc-cover-and-synopsis-design.md).</summary>
    [ObservableProperty]
    private string? _arcSourceLabel;

    /// <summary>Downloaded lazily by <see cref="LoadArcCoverAsync"/> once <see cref="LoadReadingList"/> knows the list has a cover URL - null until then, or if there's no cover/the download failed.</summary>
    [ObservableProperty]
    private Bitmap? _arcCoverImage;

    public void LoadReadingList(int readingListId)
    {
        _activeReadingListId = readingListId;
        StatusMessage = null;
        LinkingRow = null;

        using var context = PaperbunkrDb.CreateContext();
        var list = context.ReadingLists
            .Include(r => r.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.Series)
            .Include(r => r.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.MetadataProposals)
            .Include(r => r.StoryEvent)
            .Include(r => r.Tags)
            .FirstOrDefault(r => r.Id == readingListId);
        if (list is null)
        {
            return;
        }

        ListName = list.Name;
        Subtitle = !string.IsNullOrEmpty(list.Description) ? list.Description : "Cross-series reading order · tracked list";
        TypeLabel = ReadingListTypeOption.FormatLabel(list.Type);

        Tags.Clear();
        foreach (var tag in list.Tags.OrderBy(t => t.Value, StringComparer.OrdinalIgnoreCase))
        {
            Tags.Add(new TagPillViewModel(tag.Value, tag.Category, tag.Weight, FilterByTag, w => ReweightListTag(readingListId, tag.Value, w)));
        }

        LinkedStoryEventName = list.StoryEvent?.Name;
        CreatedAtLabel = $"Created {list.CreatedAt:MMM d, yyyy}";
        IsArcLinked = !string.IsNullOrEmpty(list.Source);
        ArcSourceLabel = IsArcLinked ? $"via {ReadingListSourceRegistry.GetDisplayName(list.Source!)}" : null;

        ArcCoverImage = ArcCoverImageCache.Get(list.Id);
        if (ArcCoverImage is null && !string.IsNullOrEmpty(list.CoverImageUrl))
        {
            _ = LoadArcCoverAsync(list.Id, list.CoverImageUrl);
        }

        var items = list.Items.OrderBy(i => i.SortOrder).ToList();
        TotalIssues = items.Count.ToString();
        OwnedIssues = items.Count(i => i.Issue is { FileIsMissing: false }).ToString();
        MissingIssues = items.Count(i => i.Issue is null || i.Issue.FileIsMissing).ToString();

        Groups.Clear();
        foreach (var group in items.GroupBy(i => i.GroupLabel ?? string.Empty))
        {
            var rows = new ObservableCollection<ReadingListItemRowViewModel>(
                group.Select(i => new ReadingListItemRowViewModel(i, MoveItemUp, MoveItemDown, RemoveItem, PersistFieldChange, StartLink, OpenIssue)));
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

    /// <summary>The tag a chip click narrowed <see cref="Lists"/> to, or null when unfiltered (docs/superpowers/specs/2026-08-23-reading-list-tags-design.md) - new capability, this screen had no sidebar search/filter before.</summary>
    [ObservableProperty]
    private string? _activeTagFilter;

    public bool HasActiveTagFilter => ActiveTagFilter is not null;

    partial void OnActiveTagFilterChanged(string? value)
    {
        OnPropertyChanged(nameof(HasActiveTagFilter));
        ApplyTagFilter();
    }

    private void FilterByTag(string value) => ActiveTagFilter = value;

    [RelayCommand]
    private void ClearTagFilter() => ActiveTagFilter = null;

    public void RefreshSidebar()
    {
        using var context = PaperbunkrDb.CreateContext();
        var all = context.ReadingLists.Include(r => r.Items).Include(r => r.Tags).OrderBy(r => r.SortOrder).ToList();

        _allListSummaries = all.Select(list =>
        {
            int listId = list.Id;
            return new ReadingListSummary
            {
                Id = list.Id,
                Name = list.Name,
                TotalCount = list.Items.Count,
                IsActive = list.Id == _activeReadingListId,
                HasTag = value => list.Tags.Any(t => string.Equals(t.Value, value, StringComparison.OrdinalIgnoreCase)),
                DeleteConfirm = new TwoStepConfirm(() => DeleteReadingList(listId), idleLabel: "Delete", armedLabel: "Confirm delete?"),
            };
        }).ToList();

        ApplyTagFilter();
    }

    private void ApplyTagFilter()
    {
        Lists.Clear();
        var filter = ActiveTagFilter;
        foreach (var summary in _allListSummaries)
        {
            if (filter is null || summary.HasTag(filter))
            {
                Lists.Add(summary);
            }
        }

        OnPropertyChanged(nameof(HasNoReadingLists));
        OnPropertyChanged(nameof(HasNoItems));
    }

    private void ReweightListTag(int readingListId, string value, IssueTagWeight weight)
    {
        using var context = PaperbunkrDb.CreateContext();
        var tag = context.ReadingListTags.FirstOrDefault(t => t.ReadingListId == readingListId && t.Value == value);
        if (tag is null)
        {
            return;
        }

        tag.Weight = weight;
        context.SaveChanges();
    }

    /// <summary>
    /// Deletes a whole reading list (docs/superpowers/specs/2026-08-22-delete-functionality-design.md) -
    /// cascade-deletes its <see cref="ReadingListItem"/> rows at the database level
    /// (<c>ReadingListId</c>'s FK is <c>DeleteBehavior.Cascade</c>, confirmed in
    /// <c>PaperbunkrDbContext.OnModelCreating</c>), never the underlying <c>Issue</c>s themselves
    /// (that FK is <c>Restrict</c> - an issue can belong to many lists). If the deleted list was the
    /// active one, falls back to the next available list, or clears the screen entirely if none are
    /// left.
    /// </summary>
    private void DeleteReadingList(int readingListId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var list = context.ReadingLists.Find(readingListId);
        if (list is null)
        {
            return;
        }

        context.ReadingLists.Remove(list);
        context.SaveChanges();

        if (_activeReadingListId == readingListId)
        {
            _activeReadingListId = null;
            var nextId = context.ReadingLists.OrderBy(r => r.SortOrder).Select(r => (int?)r.Id).FirstOrDefault();
            if (nextId is int id)
            {
                LoadReadingList(id);
                return;
            }

            ListName = string.Empty;
            Subtitle = string.Empty;
            TypeLabel = string.Empty;
            CreatedAtLabel = string.Empty;
            Groups.Clear();
            Tags.Clear();
            TotalIssues = "0";
            OwnedIssues = "0";
            MissingIssues = "0";
        }

        RefreshSidebar();
    }

    // --- Phase 4c overhaul (docs/superpowers/specs/2026-08-17-metadata-model-phase4c-reading-
    // list-overhaul-design.md): StoryEvent link/per-item Role+Notes, persisting immediately like
    // every other edit on this screen (Type moved to the properties overlay - docs/superpowers/
    // specs/2026-08-23-reading-list-tags-design.md). ---

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

    /// <summary>Click-to-read (docs/superpowers/specs/2026-08-23-cbl-manager-manual-editing-and-list-aware-reading-design.md §2) - Missing rows have <see cref="StartLink"/> instead, no open action.</summary>
    private void OpenIssue(ReadingListItemRowViewModel? row)
    {
        if (row is null || !row.IsOwned || _activeReadingListId is not int listId)
        {
            return;
        }

        _goReaderForIssueInReadingList(row.Item.IssueId, listId);
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
            .Where(i => !i.IsPlaceholder
                && ((i.Series?.Name ?? string.Empty).Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
                    || (i.EffectiveNumber() ?? string.Empty).Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)))
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
        if (LinkingRow is { } row)
        {
            ReadingListItemLinker.Relink(context, row.Item.Id, result.IssueId);
            LinkingRow = null;
        }
        else
        {
            int nextOrder = context.ReadingListItems.Where(i => i.ReadingListId == listId).Select(i => (int?)i.SortOrder).Max() is int max ? max + 1 : 0;
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = result.IssueId, SortOrder = nextOrder });
            BumpUpdatedAt(context, listId);
            context.SaveChanges();
        }

        SearchResults.Clear();
        SearchQuery = string.Empty;
        LoadReadingList(listId);
    }

    /// <summary>
    /// Manual relink (docs/superpowers/specs/2026-08-23-cbl-manager-manual-editing-and-list-aware-
    /// reading-design.md §1) - puts the existing library search into "linking" mode instead of
    /// adding a new row; the next <see cref="AddIssue"/> pick relinks <see cref="LinkingRow"/>
    /// instead of appending.
    /// </summary>
    [ObservableProperty]
    private ReadingListItemRowViewModel? _linkingRow;

    partial void OnLinkingRowChanged(ReadingListItemRowViewModel? value)
    {
        OnPropertyChanged(nameof(IsLinking));
        OnPropertyChanged(nameof(AddResultButtonLabel));
        OnPropertyChanged(nameof(LinkingBannerText));
    }

    public bool IsLinking => LinkingRow is not null;

    public string AddResultButtonLabel => LinkingRow is null ? "Add" : "Link";

    public string? LinkingBannerText => LinkingRow is null ? null : $"Linking {LinkingRow.Name} #{LinkingRow.Number} — pick a result below, or Cancel";

    private void StartLink(ReadingListItemRowViewModel row)
    {
        LinkingRow = row;
        SearchResults.Clear();
        SearchQuery = string.Empty;
    }

    [RelayCommand]
    private void CancelLink()
    {
        LinkingRow = null;
        SearchResults.Clear();
        SearchQuery = string.Empty;
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

    // --- External story-arc lookup (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-
    // design.md §5): search a story arc across six sources, auto-build a matched ReadingList from
    // it, and refresh an arc-linked list later. ---

    public static ArcSourceOption[] ArcSourceOptions { get; } = ReadingListSourceRegistry.All
        .Select(s => new ArcSourceOption(s.Key, s.DisplayName, s.RequiresCredentials, s.HasBrowsableCatalog))
        .ToArray();

    public ObservableCollection<ArcSearchResultRow> ArcSearchResults { get; } = new();

    [ObservableProperty]
    private bool _isArcSearchOpen;

    [ObservableProperty]
    private ArcSourceOption _selectedArcSource = ArcSourceOptions[0];

    [ObservableProperty]
    private string _arcSearchQuery = string.Empty;

    [ObservableProperty]
    private string? _arcSearchStatus;

    partial void OnSelectedArcSourceChanged(ArcSourceOption value)
    {
        _currentArcSource = null;
        _currentArcSourceKey = null;
        ArcSearchResults.Clear();
        ArcSearchQuery = string.Empty;
        RefreshArcSourceStatus(value);
    }

    [RelayCommand]
    private void ToggleArcSearch()
    {
        IsArcSearchOpen = !IsArcSearchOpen;
        ArcSearchQuery = string.Empty;
        ArcSearchResults.Clear();
        ArcSearchStatus = null;

        if (IsArcSearchOpen)
        {
            // SelectedArcSource's own [ObservableProperty] initializer sets its default value
            // directly, bypassing the setter - so OnSelectedArcSourceChanged never fires for
            // whatever source is selected by default when the panel is opened for the first time.
            // Without this, the panel opened blank (no hint, no auto-browsed catalog) until the
            // user manually switched sources once - a real bug found live, not a hypothetical.
            RefreshArcSourceStatus(SelectedArcSource);
        }
    }

    private void RefreshArcSourceStatus(ArcSourceOption source)
    {
        if (source.HasBrowsableCatalog)
        {
            _ = BrowseSourceCatalogAsync();
        }
        else
        {
            ArcSearchStatus = $"{source.DisplayName} is a live search - type a story arc or event name above.";
        }
    }

    /// <summary>
    /// Auto-loads a browsable source's whole catalog when it's selected (docs/superpowers/specs/
    /// 2026-08-22-cbl-manager-curated-browse-design.md) - lets someone unfamiliar with a source's
    /// reading lists browse what's actually available instead of needing to already know a title to
    /// search for, and doubles as the "how many entries does this source have" scope indicator.
    /// Reuses <see cref="IReadingListSource.SearchAsync"/> with an empty query, which the four
    /// browsable adapters already treat as "match everything" - no new adapter method needed.
    /// </summary>
    private async Task BrowseSourceCatalogAsync()
    {
        var source = ResolveCurrentArcSource();
        if (source is null)
        {
            return;
        }

        ArcSearchResults.Clear();
        ArcSearchStatus = "Loading catalog…";
        try
        {
            var results = await source.SearchAsync(string.Empty, CancellationToken.None);
            foreach (var result in results.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            {
                ArcSearchResults.Add(new ArcSearchResultRow(result));
            }

            ArcSearchStatus = results.Count == 0
                ? $"{source.DisplayName} has no titles available right now."
                : $"{results.Count} title(s) available from {source.DisplayName} - browse below or narrow with a search.";
        }
        catch (ReadingListSourceException ex)
        {
            ArcSearchStatus = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SearchArc()
    {
        if (string.IsNullOrWhiteSpace(ArcSearchQuery))
        {
            return;
        }

        var source = ResolveCurrentArcSource();
        if (source is null)
        {
            return;
        }

        ArcSearchResults.Clear();
        ArcSearchStatus = "Searching…";
        try
        {
            var results = await source.SearchAsync(ArcSearchQuery, CancellationToken.None);
            if (results.Count == 0)
            {
                ArcSearchStatus = "No story arcs found.";
                return;
            }

            foreach (var result in results)
            {
                ArcSearchResults.Add(new ArcSearchResultRow(result));
            }
            ArcSearchStatus = null;
        }
        catch (ReadingListSourceException ex)
        {
            ArcSearchStatus = ex.Message;
        }
    }

    [RelayCommand]
    private async Task UseArc(ArcSearchResultRow? row)
    {
        if (row is null)
        {
            return;
        }

        var source = ResolveCurrentArcSource();
        if (source is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        try
        {
            var list = await ArcReadingListBuilder.CreateFromArcAsync(context, source, row.Result, CancellationToken.None);
            StatusMessage = $"Created '{list.Name}' with {list.Items.Count} issue(s).";
            IsArcSearchOpen = false;
            ArcSearchResults.Clear();
            ArcSearchQuery = string.Empty;
            LoadReadingList(list.Id);
        }
        catch (Exception ex) when (ex is ReadingListSourceException or InvalidOperationException)
        {
            ArcSearchStatus = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RefreshArcList()
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        try
        {
            var result = await ArcReadingListBuilder.RefreshAsync(context, listId, CancellationToken.None);
            StatusMessage = $"Added {result.AddedCount}, replaced {result.ReplacedPlaceholderCount} placeholder(s), {result.StillMissingCount} still missing.";
            LoadReadingList(listId);
        }
        catch (Exception ex) when (ex is ReadingListSourceException or InvalidOperationException)
        {
            StatusMessage = ex.Message;
        }
    }

    private IReadingListSource? ResolveCurrentArcSource()
    {
        if (_currentArcSource is not null && _currentArcSourceKey == SelectedArcSource.Key)
        {
            return _currentArcSource;
        }

        using var context = PaperbunkrDb.CreateContext();
        var source = ReadingListSourceRegistry.Get(context, SelectedArcSource.Key);
        if (source is null)
        {
            ArcSearchStatus = $"{SelectedArcSource.DisplayName} needs credentials - set them in Preferences → Advanced → Reading List Sources.";
            return null;
        }

        _currentArcSource = source;
        _currentArcSourceKey = SelectedArcSource.Key;
        return source;
    }

    /// <summary>
    /// Downloads and caches an arc-linked list's cover art in the background (docs/superpowers/
    /// specs/2026-08-22-cbl-manager-arc-cover-and-synopsis-design.md) - never blocks
    /// <see cref="LoadReadingList"/> itself. Guards against the list having since changed underneath
    /// it (fast list-switching while a slow download is still in flight) by re-checking
    /// <see cref="_activeReadingListId"/> before assigning the result.
    /// </summary>
    private async Task LoadArcCoverAsync(int readingListId, string coverImageUrl)
    {
        var bitmap = await ArcCoverImageCache.DownloadAndCacheAsync(readingListId, coverImageUrl, CancellationToken.None);
        if (bitmap is not null && _activeReadingListId == readingListId)
        {
            ArcCoverImage = bitmap;
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
