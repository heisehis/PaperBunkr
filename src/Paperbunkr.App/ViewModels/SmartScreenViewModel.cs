using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
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
    public SmartScreenViewModel()
    {
        BuiltInLists = new ObservableCollection<SmartListSummary>();
        MaintenanceLists = new ObservableCollection<SmartListSummary>();
        CustomLists = new ObservableCollection<SmartListSummary>();
        Conditions = new ObservableCollection<SmartListConditionViewModel>();
        RefreshSidebar();
    }

    // "Missing Files"/"Duplicate Candidates" render under a separate Maintenance heading in the
    // wireframe sidebar, matching the seed order in PaperbunkrDb.SeedSystemSmartLists.
    private static readonly string[] MaintenanceListNames = ["Missing Files", "Duplicate Candidates"];

    private int? _activeSmartListId;
    private SmartList? _workingList;

    public ObservableCollection<SmartListSummary> BuiltInLists { get; }
    public ObservableCollection<SmartListSummary> MaintenanceLists { get; }
    public ObservableCollection<SmartListSummary> CustomLists { get; }
    public ObservableCollection<SmartListConditionViewModel> Conditions { get; }

    [ObservableProperty]
    private string _listName = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _matchCountLabel = "0";

    [ObservableProperty]
    private bool _isReadOnly;

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

        Conditions.Clear();
        foreach (var condition in list.Conditions.OrderBy(c => c.SortOrder))
        {
            Conditions.Add(new SmartListConditionViewModel(condition, RemoveCondition, RecomputeMatchCount));
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
            var summary = new SmartListSummary
            {
                Id = list.Id,
                Name = list.Name,
                MatchCount = SmartListQueryBuilder.MatchCount(context, list),
                IsActive = list.Id == _activeSmartListId,
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

    private void RecomputeMatchCount()
    {
        if (_workingList is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        // Evaluates the in-memory (possibly unsaved) working conditions against the live library,
        // so the badge updates as the user edits, before Save persists anything.
        var transient = new SmartList { Conditions = _workingList.Conditions };
        MatchCountLabel = SmartListQueryBuilder.MatchCount(context, transient).ToString();
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
        Conditions.Add(new SmartListConditionViewModel(condition, RemoveCondition, RecomputeMatchCount));
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
