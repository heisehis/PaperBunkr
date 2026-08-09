using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>Display label for one picker option — used for both Field and Operator dropdowns.</summary>
public readonly record struct FieldOption(SmartListField Field, string Label)
{
    public override string ToString() => Label;
}

public readonly record struct OperatorOption(SmartListOperator Operator, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Human-readable operator labels, grouped by the data types that offer them (spec §3).</summary>
public static class SmartListOperatorLabels
{
    public static readonly IReadOnlyDictionary<SmartListOperator, string> Labels = new Dictionary<SmartListOperator, string>
    {
        [SmartListOperator.Is] = "is",
        [SmartListOperator.IsNot] = "is not",
        [SmartListOperator.Contains] = "contains",
        [SmartListOperator.ContainsAny] = "contains any of",
        [SmartListOperator.ContainsAll] = "contains all of",
        [SmartListOperator.StartsWith] = "starts with",
        [SmartListOperator.EndsWith] = "ends with",
        [SmartListOperator.GreaterThan] = "is greater than",
        [SmartListOperator.LessThan] = "is less than",
        [SmartListOperator.InRange] = "is in range",
        [SmartListOperator.IsAfter] = "is after",
        [SmartListOperator.IsBefore] = "is before",
        [SmartListOperator.WithinLastDays] = "within last (days)",
        [SmartListOperator.DateInRange] = "is in range",
    };

    private static readonly IReadOnlyList<SmartListOperator> TextOperators =
    [
        SmartListOperator.Is, SmartListOperator.IsNot, SmartListOperator.Contains, SmartListOperator.ContainsAny,
        SmartListOperator.ContainsAll, SmartListOperator.StartsWith, SmartListOperator.EndsWith,
    ];

    private static readonly IReadOnlyList<SmartListOperator> NumberOperators =
    [
        SmartListOperator.Is, SmartListOperator.IsNot, SmartListOperator.GreaterThan, SmartListOperator.LessThan,
        SmartListOperator.InRange,
    ];

    private static readonly IReadOnlyList<SmartListOperator> ToggleOperators =
    [
        SmartListOperator.Is, SmartListOperator.IsNot,
    ];

    private static readonly IReadOnlyList<SmartListOperator> DateOperators =
    [
        SmartListOperator.Is, SmartListOperator.IsAfter, SmartListOperator.IsBefore,
        SmartListOperator.WithinLastDays, SmartListOperator.DateInRange,
    ];

    public static IReadOnlyList<SmartListOperator> For(SmartListDataType dataType) => dataType switch
    {
        SmartListDataType.Text => TextOperators,
        SmartListDataType.Number => NumberOperators,
        SmartListDataType.Toggle => ToggleOperators,
        SmartListDataType.Date => DateOperators,
        _ => TextOperators,
    };
}
