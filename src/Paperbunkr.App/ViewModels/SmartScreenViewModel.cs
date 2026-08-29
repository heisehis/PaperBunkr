using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.SmartLists;

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

    public SmartScreenViewModel(Action<int> goToSeries)
    {
        _goToSeries = goToSeries;
        BuiltInLists = new ObservableCollection<SmartListSummary>();
        MaintenanceLists = new ObservableCollection<SmartListSummary>();
        CustomLists = new ObservableCollection<SmartListSummary>();
        Results = new ObservableCollection<IssueCardSample>();
        RefreshSidebar();
    }

    // "Missing Files"/"Duplicate Candidates" render under a separate Maintenance heading in the
    // wireframe sidebar, matching the seed order in PaperbunkrDb.SeedSystemSmartLists.
    private static readonly string[] MaintenanceListNames = ["Missing Files", "Duplicate Candidates"];

    private int? _activeSmartListId;
    private SmartList? _workingList;
    private IReadOnlyList<VirtualTagOption> _virtualTagOptions = [];

    public ObservableCollection<SmartListSummary> BuiltInLists { get; }
    public ObservableCollection<SmartListSummary> MaintenanceLists { get; }
    public ObservableCollection<SmartListSummary> CustomLists { get; }

    /// <summary>The root AND/OR group of the currently-open list (spec §2). Null until a list is loaded.</summary>
    [ObservableProperty]
    private SmartListGroupViewModel? _rootGroup;

    /// <summary>
    /// The list's actual matched issues (docs/superpowers/specs/
    /// 2026-08-09-smart-lists-results-view-design.md) - previously only <see cref="MatchCountLabel"/>
    /// was kept, even though <see cref="SmartListQueryBuilder.Build"/> always computed the full set.
    /// </summary>
    public ObservableCollection<IssueCardSample> Results { get; }

    /// <summary>
    /// XAML's compiled-binding <c>!</c> negation needs a real <see langword="bool"/> - <c>Results</c>
    /// itself has no bindable <c>Count</c>-as-bool, so this exists purely for the empty-state
    /// <c>IsVisible</c> toggle, raised manually in <see cref="RecomputeMatchCount"/> since
    /// <see cref="ObservableCollection{T}"/> doesn't raise property-changed for a derived property.
    /// </summary>
    public bool HasResults => Results.Count > 0;

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
            onChanged: RecomputeMatchCount,
            isReadOnly: () => IsReadOnly,
            _virtualTagOptions,
            onRemove: null);

        // One library load feeds both the active list's Results and every list's sidebar count -
        // opening the screen used to materialize the whole library twice (once here, once in
        // RefreshSidebar) plus once more per sidebar list.
        var all = context.SmartLists.OrderBy(s => s.SortOrder).ToList();
        var trees = LoadTrees(context, all);
        var snapshot = SmartListQueryBuilder.LoadSnapshot(
            context, trees.SelectMany(t => SmartListQueryBuilder.Flatten(t.RootGroup)).ToList());

        RecomputeMatchCount(snapshot);
        RefreshSidebarCore(all, trees.ToDictionary(t => t.Id, t => SmartListQueryBuilder.Evaluate(snapshot, t).Count));
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

        // One library load for every list's count, not one full-library materialization per list -
        // the Smart screen open path was ~N of them.
        RefreshSidebarCore(all, SmartListQueryBuilder.MatchCounts(context, trees));
    }

    private void RefreshSidebarCore(List<SmartList> all, Dictionary<int, int> matchCounts)
    {
        BuiltInLists.Clear();
        MaintenanceLists.Clear();
        CustomLists.Clear();

        foreach (var list in all)
        {
            int listId = list.Id;
            var summary = new SmartListSummary
            {
                Id = list.Id,
                Name = list.Name,
                MatchCount = matchCounts.TryGetValue(list.Id, out var mc) ? mc : 0,
                IsActive = list.Id == _activeSmartListId,
                DeleteConfirm = list.IsSystem
                    ? null
                    : new TwoStepConfirm(() => DeleteSmartList(listId), idleLabel: "Delete", armedLabel: "Confirm delete?"),
            };

            if (!list.IsSystem)
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
            });
        }

        MatchCountLabel = Results.Count.ToString();
        OnPropertyChanged(nameof(HasResults));
    }

    [RelayCommand]
    private void SelectResult(IssueCardSample? issue)
    {
        if (issue is not null)
        {
            _goToSeries(issue.SeriesId);
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
            SortOrder = context.SmartLists.Count(),
            RootGroup = CloneGroup(_workingList.RootGroup),
        };

        context.SmartLists.Add(clone);
        context.SaveChanges();
        LoadSmartList(clone.Id);
    }

    [RelayCommand]
    private void CreateNew()
    {
        using var context = PaperbunkrDb.CreateContext();
        var list = new SmartList
        {
            Name = "New Smart List",
            IsSystem = false,
            SortOrder = context.SmartLists.Count(),
            RootGroup = new SmartListConditionGroup { Mode = SmartListGroupMode.And },
        };
        context.SmartLists.Add(list);
        context.SaveChanges();
        LoadSmartList(list.Id);
    }

    [RelayCommand]
    private void SelectList(SmartListSummary? summary)
    {
        if (summary is not null)
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
