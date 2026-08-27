using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
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
        Conditions = new ObservableCollection<SmartListConditionViewModel>();
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
    public ObservableCollection<SmartListConditionViewModel> Conditions { get; }

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
        var list = context.SmartLists.Include(s => s.Conditions).FirstOrDefault(s => s.Id == smartListId);
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

        Conditions.Clear();
        foreach (var condition in list.Conditions.OrderBy(c => c.SortOrder))
        {
            Conditions.Add(new SmartListConditionViewModel(condition, RemoveCondition, RecomputeMatchCount, _virtualTagOptions));
        }

        RecomputeMatchCount();
        RefreshSidebar();
    }

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
        var all = context.SmartLists.Include(s => s.Conditions).OrderBy(s => s.SortOrder).ToList();

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
                MatchCount = SmartListQueryBuilder.MatchCount(context, list),
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
    /// null for those). <see cref="SmartListCondition"/>'s FK is <c>DeleteBehavior.Cascade</c>
    /// (confirmed in <c>PaperbunkrDbContext.OnModelCreating</c>), so its conditions go with it - no
    /// explicit removal loop needed. In real usage there's always at least the seeded built-in/
    /// maintenance lists to fall back to - unlike Reading Lists, this screen can never end up with
    /// nothing left to show. Always calls <see cref="RefreshSidebar"/> itself, even along the
    /// "fell back to another list" branch: <see cref="EnsureListLoaded"/> only refreshes the
    /// sidebar as a side effect of successfully loading *something* (via <see cref="LoadSmartList"/>) -
    /// a real bug caught by its own test, where deleting the only list left the just-deleted entry
    /// stuck showing in <see cref="CustomLists"/> because nothing was left to load.
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
        // Evaluates the in-memory (possibly unsaved) working conditions against the live library,
        // so the results/count update as the user edits, before Save persists anything. One
        // Build() call backs both Results and MatchCountLabel - MatchCount(ctx, list) is just
        // Build(ctx, list).Count, so calling it separately would evaluate the query twice.
        var transient = new SmartList { Conditions = _workingList.Conditions };
        var matched = SmartListQueryBuilder.Build(context, transient);

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
                CoverImage = CoverImageCache.Get(issue.Id, issue.FilePath, issue.FileSize),
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

    [RelayCommand]
    private void AddCondition()
    {
        if (_workingList is null || IsReadOnly)
        {
            return;
        }

        var condition = new SmartListCondition
        {
            Field = SmartListField.SeriesName,
            Operator = SmartListOperator.Is,
            Value = string.Empty,
            SortOrder = _workingList.Conditions.Count,
        };
        _workingList.Conditions.Add(condition);
        Conditions.Add(new SmartListConditionViewModel(condition, RemoveCondition, RecomputeMatchCount, _virtualTagOptions));
        RecomputeMatchCount();
    }

    private void RemoveCondition(SmartListConditionViewModel conditionViewModel)
    {
        if (_workingList is null || IsReadOnly)
        {
            return;
        }

        _workingList.Conditions.Remove(conditionViewModel.Condition);
        Conditions.Remove(conditionViewModel);
        RecomputeMatchCount();
    }

    [RelayCommand]
    private void Save()
    {
        if (_workingList is null || IsReadOnly)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var existing = context.SmartLists.Include(s => s.Conditions).First(s => s.Id == _workingList.Id);
        existing.Name = ListName;
        existing.Conditions.Clear();

        int sortOrder = 0;
        foreach (var condition in _workingList.Conditions)
        {
            existing.Conditions.Add(new SmartListCondition
            {
                Field = condition.Field,
                Operator = condition.Operator,
                Value = condition.Value,
                Value2 = condition.Value2,
                CustomValueName = condition.CustomValueName,
                VirtualTagId = condition.VirtualTagId,
                SortOrder = sortOrder++,
            });
        }

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
            Conditions = _workingList.Conditions.Select((c, i) => new SmartListCondition
            {
                Field = c.Field,
                Operator = c.Operator,
                Value = c.Value,
                Value2 = c.Value2,
                CustomValueName = c.CustomValueName,
                VirtualTagId = c.VirtualTagId,
                SortOrder = i,
            }).ToList(),
        };

        context.SmartLists.Add(clone);
        context.SaveChanges();
        LoadSmartList(clone.Id);
    }

    [RelayCommand]
    private void CreateNew()
    {
        using var context = PaperbunkrDb.CreateContext();
        var list = new SmartList { Name = "New Smart List", IsSystem = false, SortOrder = context.SmartLists.Count() };
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
}
