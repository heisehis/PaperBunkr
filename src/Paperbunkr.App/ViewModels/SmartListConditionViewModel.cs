using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One editable row in the rule builder, wrapping a real <see cref="SmartListCondition"/>. Edits
/// write straight through to the wrapped entity (an in-memory edit buffer until Save persists it —
/// see <see cref="SmartScreenViewModel"/>) and notify the parent so the live match count recomputes.
/// </summary>
public partial class SmartListConditionViewModel : ViewModelBase
{
    private static readonly IReadOnlyList<FieldOption> AllFieldOptions = SmartListCatalog.Definitions.Values
        .OrderBy(d => d.Label)
        .Select(d => new FieldOption(d.Field, d.Label))
        .Append(new FieldOption(SmartListField.CustomValue, "Custom Value"))
        .Append(new FieldOption(SmartListField.Duplicate, "Duplicate"))
        .Append(new FieldOption(SmartListField.VirtualTag, "Virtual Tag"))
        .ToList();

    private readonly SmartListCondition _condition;
    private readonly Action<SmartListConditionViewModel> _onRemove;
    private readonly Action _onChanged;

    public SmartListConditionViewModel(
        SmartListCondition condition,
        Action<SmartListConditionViewModel> onRemove,
        Action onChanged,
        IReadOnlyList<VirtualTagOption>? virtualTagOptions = null)
    {
        _condition = condition;
        _onRemove = onRemove;
        _onChanged = onChanged;
        VirtualTagOptions = virtualTagOptions ?? Array.Empty<VirtualTagOption>();
    }

    public SmartListCondition Condition => _condition;

    public IReadOnlyList<FieldOption> FieldOptions => AllFieldOptions;

    /// <summary>Enabled <c>VirtualTagDefinition</c>s available to pick from — supplied by <see cref="SmartScreenViewModel"/>, which owns the DB context, rather than this row querying the database itself.</summary>
    public IReadOnlyList<VirtualTagOption> VirtualTagOptions { get; }

    public FieldOption SelectedField
    {
        get => AllFieldOptions.FirstOrDefault(f => f.Field == _condition.Field);
        set
        {
            if (_condition.Field == value.Field)
            {
                return;
            }

            _condition.Field = value.Field;
            _condition.Operator = OperatorOptions.FirstOrDefault().Operator;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OperatorOptions));
            OnPropertyChanged(nameof(SelectedOperator));
            OnPropertyChanged(nameof(IsCustomValueField));
            OnPropertyChanged(nameof(IsVirtualTagField));
            OnPropertyChanged(nameof(SelectedVirtualTag));
            OnPropertyChanged(nameof(ShowValue2));
            _onChanged();
        }
    }

    public bool IsCustomValueField => _condition.Field == SmartListField.CustomValue;

    public bool IsDuplicateField => _condition.Field == SmartListField.Duplicate;

    public bool IsVirtualTagField => _condition.Field == SmartListField.VirtualTag;

    public IReadOnlyList<OperatorOption> OperatorOptions
    {
        get
        {
            var dataType = SmartListCatalog.Definitions.TryGetValue(_condition.Field, out var def)
                ? def.DataType
                : SmartListDataType.Toggle; // CustomValue/VirtualTag behave text-like but Duplicate is toggle-only
            var operators = IsCustomValueField || IsVirtualTagField
                ? SmartListOperatorLabels.For(SmartListDataType.Text)
                : IsDuplicateField
                    ? SmartListOperatorLabels.For(SmartListDataType.Toggle)
                    : SmartListOperatorLabels.For(dataType);
            return operators.Select(op => new OperatorOption(op, SmartListOperatorLabels.Labels[op])).ToList();
        }
    }

    public OperatorOption SelectedOperator
    {
        get => OperatorOptions.FirstOrDefault(o => o.Operator == _condition.Operator);
        set
        {
            if (_condition.Operator == value.Operator)
            {
                return;
            }

            _condition.Operator = value.Operator;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowValue2));
            _onChanged();
        }
    }

    public bool ShowValue2 => _condition.Operator is SmartListOperator.InRange or SmartListOperator.DateInRange;

    public string Value
    {
        get => _condition.Value;
        set
        {
            if (_condition.Value == value)
            {
                return;
            }

            _condition.Value = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public string? Value2
    {
        get => _condition.Value2;
        set
        {
            _condition.Value2 = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public string? CustomValueName
    {
        get => _condition.CustomValueName;
        set
        {
            _condition.CustomValueName = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public VirtualTagOption? SelectedVirtualTag
    {
        get => _condition.VirtualTagId is int id ? VirtualTagOptions.FirstOrDefault(t => t.Id == id) : null;
        set
        {
            int? id = value?.Id;
            if (_condition.VirtualTagId == id)
            {
                return;
            }

            _condition.VirtualTagId = id;
            OnPropertyChanged();
            _onChanged();
        }
    }

    [RelayCommand]
    private void Remove() => _onRemove(this);
}
