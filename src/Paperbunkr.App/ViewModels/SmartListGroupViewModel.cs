using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One AND/OR group card in the rule builder (docs/superpowers/specs/2026-08-28-smartlist-engine-
/// v2-design.md §2), wrapping a real <see cref="SmartListConditionGroup"/>. Renders as a bordered
/// card with an And/Or toggle and an ordered list of rows — each row a condition
/// (<see cref="SmartListConditionViewModel"/>) or a nested group card (this type, recursively). A
/// single-group list with no nesting renders as today's flat pill list.
/// </summary>
public partial class SmartListGroupViewModel : ViewModelBase
{
    private readonly SmartListConditionGroup _group;
    private readonly SmartListTargetKind _targetKind;
    private readonly Action _onChanged;
    private readonly Action<SmartListGroupViewModel>? _onRemove;
    private readonly IReadOnlyList<VirtualTagOption> _virtualTagOptions;
    private readonly Func<bool> _isReadOnly;

    public SmartListGroupViewModel(
        SmartListConditionGroup group,
        SmartListTargetKind targetKind,
        Action onChanged,
        Func<bool> isReadOnly,
        IReadOnlyList<VirtualTagOption> virtualTagOptions,
        Action<SmartListGroupViewModel>? onRemove)
    {
        _group = group;
        _targetKind = targetKind;
        _onChanged = onChanged;
        _isReadOnly = isReadOnly;
        _virtualTagOptions = virtualTagOptions;
        _onRemove = onRemove;

        Conditions = new ObservableCollection<SmartListConditionViewModel>();
        ChildGroups = new ObservableCollection<SmartListGroupViewModel>();

        foreach (var condition in group.Conditions.OrderBy(c => c.SortOrder))
        {
            Conditions.Add(NewConditionVm(condition));
        }

        foreach (var child in group.ChildGroups.OrderBy(g => g.SortOrder))
        {
            ChildGroups.Add(NewChildVm(child));
        }
    }

    public SmartListConditionGroup Group => _group;

    public ObservableCollection<SmartListConditionViewModel> Conditions { get; }

    public ObservableCollection<SmartListGroupViewModel> ChildGroups { get; }

    public bool IsRoot => _onRemove is null;

    public bool IsReadOnly => _isReadOnly();

    public bool CanRemove => !IsRoot && !IsReadOnly;

    /// <summary>And ↔ Or. Bound to a two-state toggle at the top of the card.</summary>
    public bool IsOr
    {
        get => _group.Mode == SmartListGroupMode.Or;
        set
        {
            var mode = value ? SmartListGroupMode.Or : SmartListGroupMode.And;
            if (_group.Mode == mode)
            {
                return;
            }

            _group.Mode = mode;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ModeLabel));
            OnPropertyChanged(nameof(MatchSummary));
            _onChanged();
        }
    }

    public string ModeLabel => IsOr ? "OR" : "AND";

    public string MatchSummary => IsOr ? "Match ANY of the following:" : "Match ALL of the following:";

    [RelayCommand]
    private void ToggleMode() => IsOr = !IsOr;

    [RelayCommand]
    private void AddCondition()
    {
        if (IsReadOnly)
        {
            return;
        }

        var condition = new SmartListCondition
        {
            // SeriesName exists in both the Issue and Series catalogs; Novel has no such field, so
            // its new-condition default is NovelTitle instead (docs/superpowers/specs/2026-08-30-
            // smart-collections-design.md).
            Field = _targetKind == SmartListTargetKind.Novel ? SmartListField.NovelTitle : SmartListField.SeriesName,
            Operator = SmartListOperator.Is,
            Value = string.Empty,
            SortOrder = _group.Conditions.Count,
        };
        _group.Conditions.Add(condition);
        Conditions.Add(NewConditionVm(condition));
        _onChanged();
    }

    [RelayCommand]
    private void AddGroup()
    {
        if (IsReadOnly)
        {
            return;
        }

        var child = new SmartListConditionGroup
        {
            Mode = SmartListGroupMode.Or,
            SortOrder = _group.ChildGroups.Count,
        };
        _group.ChildGroups.Add(child);
        ChildGroups.Add(NewChildVm(child));
        _onChanged();
    }

    [RelayCommand]
    private void Remove() => _onRemove?.Invoke(this);

    private SmartListConditionViewModel NewConditionVm(SmartListCondition condition) =>
        new(condition, _targetKind, RemoveCondition, _onChanged, _virtualTagOptions);

    private SmartListGroupViewModel NewChildVm(SmartListConditionGroup child) =>
        new(child, _targetKind, _onChanged, _isReadOnly, _virtualTagOptions, RemoveChild);

    private void RemoveCondition(SmartListConditionViewModel row)
    {
        if (IsReadOnly)
        {
            return;
        }

        _group.Conditions.Remove(row.Condition);
        Conditions.Remove(row);
        _onChanged();
    }

    private void RemoveChild(SmartListGroupViewModel child)
    {
        if (IsReadOnly)
        {
            return;
        }

        _group.ChildGroups.Remove(child.Group);
        ChildGroups.Remove(child);
        _onChanged();
    }
}
