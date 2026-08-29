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
///
/// SmartList Engine v2 (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md): the row
/// gains a NOT prefix toggle (§2), a case-sensitivity "Aa" toggle and the List Contains / Regular
/// Expression operators (§3, Text-like fields only), and — for the new All Properties field — a
/// secondary "search in" dropdown (§4).
/// </summary>
public partial class SmartListConditionViewModel : ViewModelBase
{
    private static readonly IReadOnlyList<FieldOption> AllFieldOptions = SmartListCatalog.Definitions.Values
        .OrderBy(d => d.Label)
        .Select(d => new FieldOption(d.Field, d.Label))
        .Append(new FieldOption(SmartListField.AllProperties, "All Properties"))
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

    public IReadOnlyList<SearchModeOption> SearchModeOptions => SearchModeOption.All;

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
            if (value.Field == SmartListField.AllProperties)
            {
                _condition.SearchMode ??= SearchMode.All;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(OperatorOptions));
            OnPropertyChanged(nameof(SelectedOperator));
            OnPropertyChanged(nameof(IsCustomValueField));
            OnPropertyChanged(nameof(IsVirtualTagField));
            OnPropertyChanged(nameof(IsAllPropertiesField));
            OnPropertyChanged(nameof(SelectedVirtualTag));
            OnPropertyChanged(nameof(SelectedSearchMode));
            OnPropertyChanged(nameof(ShowValue2));
            OnPropertyChanged(nameof(ShowCaseToggle));
            _onChanged();
        }
    }

    public bool IsCustomValueField => _condition.Field == SmartListField.CustomValue;

    public bool IsDuplicateField => _condition.Field == SmartListField.Duplicate;

    public bool IsVirtualTagField => _condition.Field == SmartListField.VirtualTag;

    public bool IsAllPropertiesField => _condition.Field == SmartListField.AllProperties;

    /// <summary>The condition ultimately routes through <c>SmartListQueryBuilder.EvaluateText</c> — Text fields plus the Custom Value / Virtual Tag / All Properties special fields, but not Duplicate (toggle-only).</summary>
    private bool IsTextLike =>
        IsCustomValueField || IsVirtualTagField || IsAllPropertiesField ||
        (SmartListCatalog.Definitions.TryGetValue(_condition.Field, out var def) && def.DataType == SmartListDataType.Text);

    /// <summary>The "Aa" case-sensitivity toggle is shown for Text-like fields only (spec §3).</summary>
    public bool ShowCaseToggle => IsTextLike && !IsDuplicateField;

    public IReadOnlyList<OperatorOption> OperatorOptions
    {
        get
        {
            var dataType = SmartListCatalog.Definitions.TryGetValue(_condition.Field, out var def)
                ? def.DataType
                : SmartListDataType.Toggle; // CustomValue/VirtualTag/AllProperties behave text-like but Duplicate is toggle-only
            var operators = IsCustomValueField || IsVirtualTagField || IsAllPropertiesField
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

    /// <summary>The per-condition NOT toggle (spec §2) — negates just this condition's own result inside its group.</summary>
    public bool Not
    {
        get => _condition.Not;
        set
        {
            if (_condition.Not == value)
            {
                return;
            }

            _condition.Not = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    /// <summary>Case-insensitive text matching (spec §3) — the "Aa" toggle. Default true, matching CE.</summary>
    public bool IgnoreCase
    {
        get => _condition.IgnoreCase;
        set
        {
            if (_condition.IgnoreCase == value)
            {
                return;
            }

            _condition.IgnoreCase = value;
            OnPropertyChanged();
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

    /// <summary>The secondary "search in" dropdown for an <see cref="SmartListField.AllProperties"/> condition (spec §4).</summary>
    public SearchModeOption SelectedSearchMode
    {
        get => SearchModeOption.All.FirstOrDefault(o => o.Mode == (_condition.SearchMode ?? SearchMode.All));
        set
        {
            if ((_condition.SearchMode ?? SearchMode.All) == value.Mode)
            {
                return;
            }

            _condition.SearchMode = value.Mode;
            OnPropertyChanged();
            _onChanged();
        }
    }

    [RelayCommand]
    private void Remove() => _onRemove(this);
}
