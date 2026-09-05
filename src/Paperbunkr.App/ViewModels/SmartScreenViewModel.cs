using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.SmartLists;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Smart Lists screen, ported from SmartScreen.dc.html (Claude Design project 43c40b25). Loads a
/// real <see cref="SmartList"/> (via <see cref="LoadSmartList"/>/<see cref="EnsureListLoaded"/>)
/// and evaluates it live via <see cref="SmartListQueryBuilder"/> instead of the wireframe's
/// hardcoded "Unread Salvage Noir" sample content — see
/// docs/superpowers/specs/2026-08-06-smart-lists-design.md.
///
/// The rule builder is a nested AND/OR group tree (docs/superpowers/specs/2026-08-28-smartlist-
/// engine-v2-design.md §2) rooted at <see cref="RootGroup"/>; a single-group list renders the same
/// as the pre-v2 flat pill list.
/// </summary>
public partial class SmartScreenViewModel : ViewModelBase
{
    private readonly Action<int> _goToSeries;
    private readonly Action<int> _goToBook;

    public SmartScreenViewModel(Action<int> goToSeries, Action<int> goToBook)
    {
        _goToSeries = goToSeries;
        _goToBook = goToBook;
        BuiltInLists = new ObservableCollection<SmartListSummary>();
        MaintenanceLists = new ObservableCollection<SmartListSummary>();
        CustomLists = new ObservableCollection<SmartListSummary>();
        SeriesLists = new ObservableCollection<SmartListSummary>();
        NovelLists = new ObservableCollection<SmartListSummary>();
        Results = new ObservableCollection<IssueCardSample>();
        SeriesResults = new ObservableCollection<SeriesCardSample>();
        NovelResults = new ObservableCollection<BookCardSample>();
        RefreshSidebar();
    }

    // "Missing Files"/"Duplicate Candidates" render under a separate Maintenance heading in the
    // wireframe sidebar, matching the seed order in PaperbunkrDb.SeedSystemSmartLists. Both are
    // Issue-kind built-ins - Series/Novel lists are always user-created (docs/superpowers/specs/
    // 2026-08-30-smart-collections-design.md), so they only ever land in SeriesLists/NovelLists,
    // never split into built-in/maintenance sub-groups.
    private static readonly string[] MaintenanceListNames = ["Missing Files", "Duplicate Candidates"];

    private int? _activeSmartListId;
    private SmartList? _workingList;
    private IReadOnlyList<VirtualTagOption> _virtualTagOptions = [];

    public ObservableCollection<SmartListSummary> BuiltInLists { get; }
    public ObservableCollection<SmartListSummary> MaintenanceLists { get; }
    public ObservableCollection<SmartListSummary> CustomLists { get; }
    public ObservableCollection<SmartListSummary> SeriesLists { get; }
    public ObservableCollection<SmartListSummary> NovelLists { get; }

    /// <summary>
    /// Plugin API v2 CreateBookList hook (docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-
    /// hooks-plan.md §6) - one entry per enabled command, computed fresh each time it's selected
    /// (spec: "computed by scanning the whole library each time it's opened"), not backed by any
    /// <see cref="SmartList"/> row. Doesn't fit <see cref="SmartListQueryBuilder"/>'s DB-row model at
    /// all, so this is a parallel sidebar section rather than shoehorned into the existing ones.
    /// Synthetic negative ids (never collide with a real <see cref="SmartList.Id"/>) map back to the
    /// actual <see cref="Command"/> via <see cref="_pluginListCommands"/>.
    /// </summary>
    public ObservableCollection<SmartListSummary> PluginLists { get; } = new();

    private readonly Dictionary<int, Command> _pluginListCommands = new();
    private int? _activePluginListId;
    private PluginHostService? _pluginHost;

    public bool HasPluginHost => _pluginHost is not null;

    /// <summary>Called once from <c>App.axaml.cs</c> after <see cref="PluginHostService.Initialize"/> - same pattern as <c>LibraryScreenViewModel.AttachHost</c>.</summary>
    public void AttachHost(PluginHostService host)
    {
        _pluginHost = host;
        OnPropertyChanged(nameof(HasPluginHost));
        RefreshPluginLists();
    }

    private void RefreshPluginLists()
    {
        PluginLists.Clear();
        _pluginListCommands.Clear();
        if (_pluginHost is null)
        {
            return;
        }

        int syntheticId = -1;
        foreach (var command in _pluginHost.Engine.GetCommands(PluginHooks.CreateBookList))
        {
            _pluginListCommands[syntheticId] = command;
            PluginLists.Add(new SmartListSummary
            {
                Id = syntheticId,
                Name = command.Name,
                TargetKind = SmartListTargetKind.Issue,
                IsActive = syntheticId == _activePluginListId,
                DeleteConfirm = null,
            });
            syntheticId--;
        }
    }

    /// <summary>Runs the plugin command fresh and shows its returned issues as the current results - the CreateBookList counterpart to <see cref="LoadSmartList"/>.</summary>
    private async Task LoadPluginListAsync(int syntheticId)
    {
        if (_pluginHost is null || !_pluginListCommands.TryGetValue(syntheticId, out var command) || command.Environment is null)
        {
            return;
        }

        _activeSmartListId = null;
        _activePluginListId = syntheticId;
        _workingList = null;
        RootGroup = null;
        ListName = command.Name;
        Subtitle = "Plugin smart list · recomputed each time it's opened";
        IsReadOnly = true;

        var result = await _pluginHost.RunCommandAsync(command, new CreateBookListHookGlobals { Environment = command.Environment! }).ConfigureAwait(true);

        Results.Clear();
        SeriesResults.Clear();
        NovelResults.Clear();

        if (result.ReturnValue is IEnumerable<Issue> issues)
        {
            using var context = PaperbunkrDb.CreateContext();
            var ids = issues.Select(i => i.Id).ToList();
            var loaded = context.Issues.Include(i => i.Series).Where(i => ids.Contains(i.Id)).ToList();
            foreach (var issue in loaded)
            {
                Results.Add(new IssueCardSample
                {
                    Id = issue.Id,
                    SeriesId = issue.SeriesId,
                    Title = string.IsNullOrWhiteSpace(issue.EffectiveNumber()) ? "#?" : $"#{issue.EffectiveNumber()}",
                    IsUnread = issue.LastPageRead is null or 0,
                    CoverBrush = SeriesCardSample.CoverBrushFor(issue.Series!.Name),
                    CoverIssueId = issue.Id,
                });
            }
        }

        MatchCountLabel = Results.Count.ToString();
        NotifyResultsChanged();
        RefreshSidebar();
        RefreshPluginLists();
    }

    /// <summary>The root AND/OR group of the currently-open list (spec §2). Null until a list is loaded.</summary>
    [ObservableProperty]
    private SmartListGroupViewModel? _rootGroup;

    /// <summary>
    /// The list's actual matched issues (docs/superpowers/specs/
    /// 2026-08-09-smart-lists-results-view-design.md) - previously only <see cref="MatchCountLabel"/>
    /// was kept, even though <see cref="SmartListQueryBuilder.Build"/> always computed the full set.
    /// </summary>
    public ObservableCollection<IssueCardSample> Results { get; }

    /// <summary>Live matches for a Series-target list (docs/superpowers/specs/2026-08-30-smart-collections-design.md) - populated instead of <see cref="Results"/> when the active list's <see cref="SmartList.TargetKind"/> is <see cref="SmartListTargetKind.Series"/>.</summary>
    public ObservableCollection<SeriesCardSample> SeriesResults { get; }

    /// <summary>Live matches for a Novel-target list - populated instead of <see cref="Results"/> when the active list's kind is <see cref="SmartListTargetKind.Novel"/>.</summary>
    public ObservableCollection<BookCardSample> NovelResults { get; }

    /// <summary>
    /// XAML's compiled-binding <c>!</c> negation needs a real <see langword="bool"/> - the results
    /// collections have no bindable <c>Count</c>-as-bool, so this exists purely for the empty-state
    /// <c>IsVisible</c> toggle, raised manually in <see cref="RecomputeMatchCount()"/> since
    /// <see cref="ObservableCollection{T}"/> doesn't raise property-changed for a derived property.
    /// Reflects whichever of the three results collections is active for the current list's kind.
    /// </summary>
    public bool HasResults => Results.Count > 0 || SeriesResults.Count > 0 || NovelResults.Count > 0;

    /// <summary>Per-kind visibility for the three results grids - only one is ever non-empty at a time (each RecomputeMatchCount overload clears the other two), but the view needs a real bool per grid to gate IsVisible.</summary>
    public bool HasIssueResults => Results.Count > 0;

    public bool HasSeriesResults => SeriesResults.Count > 0;

    public bool HasNovelResults => NovelResults.Count > 0;

    public string NoResultsMessage => _workingList?.TargetKind switch
    {
        SmartListTargetKind.Series => "No series match this list yet.",
        SmartListTargetKind.Novel => "No novels match this list yet.",
        _ => "No issues match this list yet.",
    };

    [ObservableProperty]
    private string _listName = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _matchCountLabel = "0";

    [ObservableProperty]
    private bool _isReadOnly;

    /// <summary>
    /// Sidebar "Maintenance" group expand/collapse (P6 follow-up, docs/alpha-todo.md) - the "▾"
    /// caret next to that heading in MainWindow.axaml used to be a plain unbound TextBlock: it
    /// looked like a working collapse toggle but did nothing at all, so the group was always shown
    /// regardless. Real toggle now, matching the collapse affordance it always visually implied.
    /// </summary>
    [ObservableProperty]
    private bool _isMaintenanceExpanded = true;

    [RelayCommand]
    private void ToggleMaintenance() => IsMaintenanceExpanded = !IsMaintenanceExpanded;

    /// <summary>Loads the given smart list into the rule builder and refreshes the sidebar's live counts.</summary>
    public void LoadSmartList(int smartListId)
    {
        _activeSmartListId = smartListId;

        using var context = PaperbunkrDb.CreateContext();
        var list = SmartListTreeLoader.LoadWithTree(context, smartListId);
        if (list is null)
        {
            return;
        }

        _workingList = list;
        ListName = list.Name;
        IsReadOnly = list.IsSystem;
        Subtitle = list.IsSystem
            ? "Built-in smart list · read-only"
            : "Custom smart list · updates live as your library changes";

        _virtualTagOptions = context.VirtualTagDefinitions
            .Where(t => t.IsEnabled)
            .OrderBy(t => t.SortOrder)
            .Select(t => new VirtualTagOption(t.Id, t.Name))
            .ToList();

        RootGroup = new SmartListGroupViewModel(
            list.RootGroup,
            list.TargetKind,
            onChanged: RecomputeMatchCount,
            isReadOnly: () => IsReadOnly,
            _virtualTagOptions,
            onRemove: null);

        // One library load feeds both the active Issue-kind list's Results and every Issue-kind
        // list's sidebar count - opening the screen used to materialize the whole library twice
        // (once here, once in RefreshSidebar) plus once more per sidebar list. Series/Novel lists
        // are a newer, smaller surface (docs/superpowers/specs/2026-08-30-smart-collections-design.md)
        // and don't share that batched-snapshot optimization - see RefreshSidebarCore.
        var all = context.SmartLists.OrderBy(s => s.SortOrder).ToList();
        var trees = LoadTrees(context, all);
        var issueTrees = trees.Where(t => t.TargetKind == SmartListTargetKind.Issue).ToList();
        var snapshot = SmartListQueryBuilder.LoadSnapshot(
            context, issueTrees.SelectMany(t => SmartListQueryBuilder.Flatten(t.RootGroup)).ToList());

        if (list.TargetKind == SmartListTargetKind.Issue)
        {
            RecomputeMatchCount(snapshot);
        }
        else
        {
            RecomputeMatchCount();
        }

        var matchCounts = issueTrees.ToDictionary(t => t.Id, t => SmartListQueryBuilder.Evaluate(snapshot, t).Count);
        foreach (var seriesList in trees.Where(t => t.TargetKind == SmartListTargetKind.Series))
        {
            matchCounts[seriesList.Id] = SeriesSmartListQueryBuilder.MatchCount(context, seriesList);
        }

        foreach (var novelList in trees.Where(t => t.TargetKind == SmartListTargetKind.Novel))
        {
            matchCounts[novelList.Id] = NovelSmartListQueryBuilder.MatchCount(context, novelList);
        }

        RefreshSidebarCore(all, matchCounts);
    }

    /// <summary>Loads each list's full nested condition tree (<see cref="SmartListTreeLoader"/>), dropping any that vanished.</summary>
    private static List<SmartList> LoadTrees(PaperbunkrDbContext context, IEnumerable<SmartList> lists) =>
        lists.Select(s => SmartListTreeLoader.LoadWithTree(context, s.Id)).OfType<SmartList>().ToList();

    /// <summary>
    /// Called on every navigation to the Smart screen. Re-loads the already-active list (so match
    /// counts and results reflect whatever changed elsewhere - e.g. a CE migration - since the
    /// last visit) or, on first visit, opens the first custom list, falling back to the first list
    /// overall.
    /// </summary>
    public void EnsureListLoaded()
    {
        if (_activeSmartListId is int activeId)
        {
            LoadSmartList(activeId);
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var firstId = context.SmartLists
            .OrderBy(s => s.IsSystem) // custom (false) lists first
            .ThenBy(s => s.SortOrder)
            .Select(s => (int?)s.Id)
            .FirstOrDefault();

        if (firstId is int id)
        {
            LoadSmartList(id);
        }
    }

    public void RefreshSidebar()
    {
        using var context = PaperbunkrDb.CreateContext();
        var all = context.SmartLists.OrderBy(s => s.SortOrder).ToList();
        var trees = LoadTrees(context, all);
        var issueTrees = trees.Where(t => t.TargetKind == SmartListTargetKind.Issue).ToList();

        // One library load for every Issue-kind list's count, not one full-library materialization
        // per list - the Smart screen open path was ~N of them. Series/Novel lists are evaluated
        // individually below - a much smaller, newer surface that doesn't need that optimization yet
        // (docs/superpowers/specs/2026-08-30-smart-collections-design.md).
        var matchCounts = SmartListQueryBuilder.MatchCounts(context, issueTrees);
        foreach (var seriesList in trees.Where(t => t.TargetKind == SmartListTargetKind.Series))
        {
            matchCounts[seriesList.Id] = SeriesSmartListQueryBuilder.MatchCount(context, seriesList);
        }

        foreach (var novelList in trees.Where(t => t.TargetKind == SmartListTargetKind.Novel))
        {
            matchCounts[novelList.Id] = NovelSmartListQueryBuilder.MatchCount(context, novelList);
        }

        RefreshSidebarCore(all, matchCounts);
    }

    private void RefreshSidebarCore(List<SmartList> all, Dictionary<int, int> matchCounts)
    {
        BuiltInLists.Clear();
        MaintenanceLists.Clear();
        CustomLists.Clear();
        SeriesLists.Clear();
        NovelLists.Clear();

        foreach (var list in all)
        {
            int listId = list.Id;
            var summary = new SmartListSummary
            {
                Id = list.Id,
                Name = list.Name,
                MatchCount = matchCounts.TryGetValue(list.Id, out var mc) ? mc : 0,
                IsActive = list.Id == _activeSmartListId,
                TargetKind = list.TargetKind,
                DeleteConfirm = list.IsSystem
                    ? null
                    : new TwoStepConfirm(() => DeleteSmartList(listId), idleLabel: "Delete", armedLabel: "Confirm delete?"),
            };

            if (list.TargetKind == SmartListTargetKind.Series)
            {
                SeriesLists.Add(summary);
            }
            else if (list.TargetKind == SmartListTargetKind.Novel)
            {
                NovelLists.Add(summary);
            }
            else if (!list.IsSystem)
            {
                CustomLists.Add(summary);
            }
            else if (MaintenanceListNames.Contains(list.Name))
            {
                MaintenanceLists.Add(summary);
            }
            else
            {
                BuiltInLists.Add(summary);
            }
        }
    }

    /// <summary>
    /// Deletes a custom smart list (docs/superpowers/specs/2026-08-22-delete-functionality-design.md) -
    /// never offered for a built-in/maintenance list (<see cref="SmartListSummary.DeleteConfirm"/> is
    /// null for those). The list's <see cref="SmartList.RootGroup"/> and its whole condition tree
    /// cascade with it (confirmed in <c>PaperbunkrDbContext.OnModelCreating</c>). Always calls
    /// <see cref="RefreshSidebar"/> itself, even along the "fell back to another list" branch:
    /// <see cref="EnsureListLoaded"/> only refreshes the sidebar as a side effect of successfully
    /// loading *something*.
    /// </summary>
    private void DeleteSmartList(int smartListId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var list = context.SmartLists.Find(smartListId);
        if (list is null || list.IsSystem)
        {
            return;
        }

        context.SmartLists.Remove(list);
        context.SaveChanges();

        if (_activeSmartListId == smartListId)
        {
            _activeSmartListId = null;
            EnsureListLoaded();
        }

        RefreshSidebar();
    }

    private void RecomputeMatchCount()
    {
        if (_workingList is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();

        if (_workingList.TargetKind == SmartListTargetKind.Series)
        {
            var transient = new SmartList { RootGroup = _workingList.RootGroup };
            SeriesResults.Clear();
            foreach (var series in SeriesSmartListQueryBuilder.Build(context, transient))
            {
                SeriesResults.Add(SeriesCardSample.FromSeries(series));
            }

            Results.Clear();
            NovelResults.Clear();
            MatchCountLabel = SeriesResults.Count.ToString();
            NotifyResultsChanged();
            return;
        }

        if (_workingList.TargetKind == SmartListTargetKind.Novel)
        {
            var transient = new SmartList { RootGroup = _workingList.RootGroup };
            NovelResults.Clear();
            foreach (var book in NovelSmartListQueryBuilder.Build(context, transient))
            {
                NovelResults.Add(BookCardSample.FromBook(book));
            }

            Results.Clear();
            SeriesResults.Clear();
            MatchCountLabel = NovelResults.Count.ToString();
            NotifyResultsChanged();
            return;
        }

        RecomputeMatchCount(SmartListQueryBuilder.LoadSnapshot(
            context, SmartListQueryBuilder.Flatten(_workingList.RootGroup).ToList()));
    }

    /// <summary>
    /// Evaluates the in-memory (possibly unsaved) working tree against <paramref name="snapshot"/>,
    /// so results/count update as the user edits before Save persists anything. Takes a prebuilt
    /// snapshot so <see cref="LoadSmartList"/> can share one library load between this and the
    /// sidebar counts instead of materializing the library twice on every screen open.
    /// </summary>
    private void RecomputeMatchCount(SmartListQueryBuilder.LibrarySnapshot snapshot)
    {
        if (_workingList is null)
        {
            return;
        }

        var transient = new SmartList { RootGroup = _workingList.RootGroup };
        var matched = SmartListQueryBuilder.Evaluate(snapshot, transient);

        Results.Clear();
        SeriesResults.Clear();
        NovelResults.Clear();
        foreach (var issue in matched)
        {
            Results.Add(new IssueCardSample
            {
                Id = issue.Id,
                SeriesId = issue.SeriesId,
                Title = string.IsNullOrWhiteSpace(issue.EffectiveNumber()) ? "#?" : $"#{issue.EffectiveNumber()}",
                IsUnread = issue.LastPageRead is null or 0,
                CoverBrush = SeriesCardSample.CoverBrushFor(issue.Series!.Name),
                CoverIssueId = issue.Id, // lazy async decode via AsyncCoverImage - see IssueCardSample.CoverIssueId
                CoverKey = CoverFingerprint.Stem(issue.Id, issue.FilePath, issue.FileSize),
            });
        }

        MatchCountLabel = Results.Count.ToString();
        NotifyResultsChanged();
    }

    private void NotifyResultsChanged()
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasIssueResults));
        OnPropertyChanged(nameof(HasSeriesResults));
        OnPropertyChanged(nameof(HasNovelResults));
        OnPropertyChanged(nameof(NoResultsMessage));
    }

    [RelayCommand]
    private void SelectResult(IssueCardSample? issue)
    {
        if (issue is not null)
        {
            _goToSeries(issue.SeriesId);
        }
    }

    [RelayCommand]
    private void SelectSeriesResult(SeriesCardSample? series)
    {
        if (series is not null)
        {
            _goToSeries(series.SeriesId);
        }
    }

    [RelayCommand]
    private void SelectNovelResult(BookCardSample? book)
    {
        if (book is not null)
        {
            _goToBook(book.Id);
        }
    }

    /// <summary>Top-level "+ Add condition" — appends to the root group, matching the pre-v2 flat-list affordance.</summary>
    [RelayCommand]
    private void AddCondition()
    {
        if (IsReadOnly || RootGroup is null)
        {
            return;
        }

        RootGroup.AddConditionCommand.Execute(null);
    }

    [RelayCommand]
    private void Save()
    {
        if (_workingList is null || IsReadOnly)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var existing = SmartListTreeLoader.LoadWithTree(context, _workingList.Id);
        if (existing is null)
        {
            return;
        }

        existing.Name = ListName;

        // Rebuild the persisted tree from the working copy without disturbing the root group row
        // itself (severing a required 1:1 nav mid-save throws). Removing each direct child - a
        // condition or a nested group - takes its whole subtree with it via the configured cascade.
        var root = existing.RootGroup;
        root.Mode = _workingList.RootGroup.Mode;
        context.SmartListConditions.RemoveRange(root.Conditions.ToList());
        context.SmartListConditionGroups.RemoveRange(root.ChildGroups.ToList());
        context.SaveChanges();

        var rebuilt = CloneGroup(_workingList.RootGroup);
        root.Conditions.AddRange(rebuilt.Conditions);
        root.ChildGroups.AddRange(rebuilt.ChildGroups);
        context.SaveChanges();

        LoadSmartList(existing.Id);
    }

    [RelayCommand]
    private void Cancel()
    {
        if (_activeSmartListId is int id)
        {
            LoadSmartList(id);
        }
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (_workingList is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var clone = new SmartList
        {
            Name = $"{ListName} Copy",
            IsSystem = false,
            TargetKind = _workingList.TargetKind,
            SortOrder = context.SmartLists.Count(),
            RootGroup = CloneGroup(_workingList.RootGroup),
        };

        context.SmartLists.Add(clone);
        context.SaveChanges();
        LoadSmartList(clone.Id);
    }

    [RelayCommand]
    private void CreateNew() => CreateNewList(SmartListTargetKind.Issue);

    [RelayCommand]
    private void CreateNewSeriesList() => CreateNewList(SmartListTargetKind.Series);

    [RelayCommand]
    private void CreateNewNovelList() => CreateNewList(SmartListTargetKind.Novel);

    private void CreateNewList(SmartListTargetKind kind)
    {
        using var context = PaperbunkrDb.CreateContext();
        var list = new SmartList
        {
            Name = kind switch
            {
                SmartListTargetKind.Series => "New Series Smart List",
                SmartListTargetKind.Novel => "New Novel Smart List",
                _ => "New Smart List",
            },
            IsSystem = false,
            TargetKind = kind,
            SortOrder = context.SmartLists.Count(),
            RootGroup = new SmartListConditionGroup { Mode = SmartListGroupMode.And },
        };
        context.SmartLists.Add(list);
        context.SaveChanges();
        LoadSmartList(list.Id);
    }

    [RelayCommand]
    private async Task SelectList(SmartListSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        if (summary.Id < 0 && _pluginListCommands.ContainsKey(summary.Id))
        {
            await LoadPluginListAsync(summary.Id);
        }
        else
        {
            LoadSmartList(summary.Id);
        }
    }

    /// <summary>Deep copy of a group tree with all Ids stripped, for Save/Duplicate (fresh rows every time).</summary>
    private static SmartListConditionGroup CloneGroup(SmartListConditionGroup source)
    {
        var copy = new SmartListConditionGroup { Mode = source.Mode, SortOrder = source.SortOrder };

        int order = 0;
        foreach (var condition in source.Conditions.OrderBy(c => c.SortOrder))
        {
            copy.Conditions.Add(new SmartListCondition
            {
                Field = condition.Field,
                Operator = condition.Operator,
                Not = condition.Not,
                IgnoreCase = condition.IgnoreCase,
                Value = condition.Value,
                Value2 = condition.Value2,
                CustomValueName = condition.CustomValueName,
                VirtualTagId = condition.VirtualTagId,
                SearchMode = condition.SearchMode,
                SortOrder = order++,
            });
        }

        order = 0;
        foreach (var child in source.ChildGroups.OrderBy(g => g.SortOrder))
        {
            var childCopy = CloneGroup(child);
            childCopy.SortOrder = order++;
            copy.ChildGroups.Add(childCopy);
        }

        return copy;
    }
}
