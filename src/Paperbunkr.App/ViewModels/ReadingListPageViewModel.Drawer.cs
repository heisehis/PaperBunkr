using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The Add-issues drawer (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md §9, decision Q15): the Library tab (search,
/// multi-select, add a whole series, and relinking a missing entry) and the Story arc tab (the CBL Manager arc lookup,
/// docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md). The search and arc code is ported unchanged from the previous
/// screen; only where it opens changed (a drawer instead of inline panels).
/// </summary>
public partial class ReadingListPageViewModel
{
    public const string DrawerLibraryTab = "Library";
    public const string DrawerArcTab = "Arc";

    private IReadingListSource? _currentArcSource;
    private string? _currentArcSourceKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryTab))]
    [NotifyPropertyChangedFor(nameof(IsArcTab))]
    private string _drawerTab = DrawerLibraryTab;

    public bool IsLibraryTab => DrawerTab == DrawerLibraryTab;

    public bool IsArcTab => DrawerTab == DrawerArcTab;

    [ObservableProperty]
    private bool _isDrawerOpen;

    [RelayCommand]
    public void OpenDrawer(string? tab)
    {
        DrawerTab = tab == DrawerArcTab ? DrawerArcTab : DrawerLibraryTab;
        IsDrawerOpen = true;
        if (IsArcTab && !IsArcSearchOpen)
        {
            IsArcSearchOpen = true;
            ArcSearchQuery = string.Empty;
            ArcSearchResults.Clear();
            ArcSearchStatus = null;
            RefreshArcSourceStatus(SelectedArcSource);
        }
    }

    [RelayCommand]
    public void CloseDrawer()
    {
        if (!IsDrawerOpen && LinkingRow is null)
        {
            return;
        }

        IsDrawerOpen = false;
        IsArcSearchOpen = false;
        LinkingRow = null;
        SearchResults.Clear();
        SearchQuery = string.Empty;
        SearchSelection.Clear();
        RaiseSearchSelectionState();
    }

    /// <summary>＋ Add issues.</summary>
    [RelayCommand]
    private void ToggleAddIssues()
    {
        if (IsDrawerOpen && IsLibraryTab)
        {
            CloseDrawer();
        }
        else
        {
            OpenDrawer(DrawerLibraryTab);
        }
    }

    public ObservableCollection<IssueSearchResult> SearchResults { get; } = new();

    public TileSelectionController<IssueSearchResult> SearchSelection { get; } = new();

    public bool AnySearchSelected => SearchSelection.Count > 0;

    public string AddSelectedLabel => $"Add {SearchSelection.Count} selected";

    private void RaiseSearchSelectionState()
    {
        OnPropertyChanged(nameof(AnySearchSelected));
        OnPropertyChanged(nameof(AddSelectedLabel));
    }

    [RelayCommand]
    private void ToggleSearchSelection(IssueSearchResult? result)
    {
        if (result is null)
        {
            return;
        }

        SearchSelection.Toggle(SearchResults, result, isShiftHeld: false);
        RaiseSearchSelectionState();
    }

    [RelayCommand]
    private void ClearSearchSelection()
    {
        SearchSelection.Clear(SearchResults);
        RaiseSearchSelectionState();
    }

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>Live search - typing alone shows matches, which matters most while relinking.</summary>
    partial void OnSearchQueryChanged(string value)
    {
        SearchSelection.Clear(SearchResults);
        Search();
        RaiseSearchSelectionState();
    }

    [RelayCommand]
    private void AddSelectedIssues()
    {
        if (_activeReadingListId is not int listId || SearchSelection.Count == 0 || IsLinking)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            ReadingListManager.AddIssues(
                context,
                listId,
                SearchResults.Where(r => SearchSelection.SelectedIds.Contains(r.Id)).Select(r => r.IssueId).ToList());
            context.SaveChanges();
        }

        SearchSelection.Clear();
        SearchQuery = string.Empty;
        RaiseSearchSelectionState();
        LoadReadingList(listId);
    }

    /// <summary>
    /// Whether drag-and-drop file import is enabled (Preferences → General, CE
    /// <c>Settings.DisableDragDrop</c> inverted). Read fresh so a Preferences toggle takes effect
    /// without reloading the screen - the code-behind <c>DragOver</c>/<c>Drop</c> handlers check it.
    /// </summary>
    public bool DragDropImportEnabled
    {
        get
        {
            using var context = PaperbunkrDb.CreateContext();
            return context.GetOrCreateAppSettings().EnableDragDropImport;
        }
    }

    /// <summary>
    /// Drag-and-drop import entry point for <see cref="Views.ReadingScreen"/>'s code-behind
    /// <c>Drop</c> handler (docs/superpowers/specs/2026-08-31-drag-and-drop-import-design.md) - runs
    /// the shared <see cref="DragImportService"/>, then attaches every resolved issue (freshly
    /// imported + already-in-library) that isn't already a member of the active list, reusing the
    /// same existing-set / <c>nextOrder</c> pattern <see cref="AddSelectedIssues"/> uses.
    /// </summary>
    public async Task ImportDroppedPathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || !DragDropImportEnabled || _activeReadingListId is not int listId)
        {
            return;
        }

        // Job-tracked, not a fire-and-forget toast (docs/superpowers/specs/2026-09-06-feedback-
        // notification-system-design.md §6) - DragImportService exposes no progress callback, so
        // this is an indeterminate job (no Done/Total set) rather than a real percentage, but it's
        // still tracked in the Activity Center and its completion follows the normal toast-policy
        // path instead of an unconditional direct toast.
        using var job = _activity.StartJob(ActivityJobKind.Import, "Importing dropped files");
        var result = await new DragImportService().ImportAsync(paths);

        int added = 0;
        if (result.IssueIds.Count > 0)
        {
            using var context = PaperbunkrDb.CreateContext();
            added = ReadingListManager.AddIssues(context, listId, result.IssueIds).Added;
            if (added > 0)
            {
                context.SaveChanges();
            }
        }

        LoadReadingList(listId);

        var parts = new List<string>();
        if (added > 0)
        {
            parts.Add($"{added} comic{(added == 1 ? "" : "s")} added");
        }

        if (result.Imported > 0)
        {
            parts.Add($"{result.Imported} newly imported");
        }

        if (result.SkippedUnsupported > 0)
        {
            parts.Add($"{result.SkippedUnsupported} skipped");
        }

        if (result.ReadingListsImported > 0)
        {
            parts.Add($"{result.ReadingListsImported} reading list{(result.ReadingListsImported == 1 ? "" : "s")} imported");
        }

        job.Succeed(parts.Count > 0 ? string.Join(", ", parts) + "." : "Nothing to import.");
    }

    [RelayCommand]
    private void AddAllOfSeries(IssueSearchResult? result)
    {
        if (result is null || _activeReadingListId is not int listId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            var seriesIssues = context.Issues.Where(i => i.SeriesId == result.SeriesId && !i.IsPlaceholder).AsEnumerable().OrderByNumber();
            ReadingListManager.AddIssues(context, listId, seriesIssues.Select(i => i.Id).ToList());
            context.SaveChanges();
        }

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
                SeriesId = issue.SeriesId,
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
            ReadingListManager.AddIssues(context, listId, new[] { result.IssueId });
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
        // Real bug found 2026-09-16: without this, "Find & link" showed the LinkingBannerText
        // ("Linking X — pick a result below, or Cancel") but the actual search box/results panel
        // stayed hidden, since that panel is gated on the separate IsAddIssuesOpen flag - normally
        // only flipped by the unrelated "+ Add issues" button - which StartLink never touched. The
        // button did nothing observable, exactly matching the user report.
        OpenDrawer(DrawerLibraryTab);
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
    [NotifyPropertyChangedFor(nameof(SelectedArcSourceText))]
    private ArcSourceOption _selectedArcSource = ArcSourceOptions[0];

    /// <summary>Registry display names, unique per source - the string-only <c>SuggestBox</c> arc
    /// picker's list, and its round-trip key back to the option.</summary>
    public string[] ArcSourceNames { get; } = ArcSourceOptions.Select(o => o.DisplayName).ToArray();

    public string SelectedArcSourceText
    {
        get => SelectedArcSource.DisplayName;
        set
        {
            var match = ArcSourceOptions.FirstOrDefault(o => o.DisplayName == value);
            if (match is not null)
            {
                SelectedArcSource = match;
            }
        }
    }

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

    /// <summary>⋯ → Build from a story arc… - opens (or closes) the drawer on its Story arc tab.</summary>
    [RelayCommand]
    private void ToggleArcSearch()
    {
        IsArcSearchOpen = !IsArcSearchOpen;
        IsDrawerOpen = IsArcSearchOpen;
        DrawerTab = IsArcSearchOpen ? DrawerArcTab : DrawerLibraryTab;
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
            PlaceInThisFolder(list.Id);
            ArcSearchResults.Clear();
            ArcSearchQuery = string.Empty;
            CloseDrawer();
            _openList(list.Id);
        }
        catch (Exception ex) when (ex is ReadingListSourceException or InvalidOperationException)
        {
            ArcSearchStatus = ex.Message;
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
}
