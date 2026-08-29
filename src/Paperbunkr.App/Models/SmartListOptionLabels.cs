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

/// <summary>Picker option for <see cref="SmartListField.VirtualTag"/> conditions — one per enabled <c>VirtualTagDefinition</c>.</summary>
public readonly record struct VirtualTagOption(int Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Secondary "search in" option for <see cref="SmartListField.AllProperties"/> conditions
/// (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md §4) — matches CE's two-dropdown
/// <c>ComicBookAllPropertiesMatcher</c> editor shape.
/// </summary>
public readonly record struct SearchModeOption(SearchMode Mode, string Label)
{
    public override string ToString() => Label;

    public static readonly IReadOnlyList<SearchModeOption> All =
    [
        new(SearchMode.All, "All properties"),
        new(SearchMode.Series, "Series"),
        new(SearchMode.Writer, "Writer"),
        new(SearchMode.Artists, "Artists"),
        new(SearchMode.Descriptive, "Descriptive"),
        new(SearchMode.File, "File"),
        new(SearchMode.Catalog, "Catalog"),
    ];
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
        [SmartListOperator.ListContains] = "list contains",
        [SmartListOperator.RegularExpression] = "matches regex",
    };

    private static readonly IReadOnlyList<SmartListOperator> TextOperators =
    [
        SmartListOperator.Is, SmartListOperator.IsNot, SmartListOperator.Contains, SmartListOperator.ContainsAny,
        SmartListOperator.ContainsAll, SmartListOperator.StartsWith, SmartListOperator.EndsWith,
        SmartListOperator.ListContains, SmartListOperator.RegularExpression,
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
