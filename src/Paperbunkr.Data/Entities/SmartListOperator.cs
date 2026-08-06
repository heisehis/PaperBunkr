namespace Paperbunkr.Data.Entities;

/// <summary>
/// Condition operators. Which subset is valid depends on the field's <see cref="SmartListDataType"/>
/// — Text: Is/IsNot/Contains/ContainsAny/ContainsAll/StartsWith/EndsWith. Number:
/// Is/IsNot/GreaterThan/LessThan/InRange. Toggle: Is/IsNot. Date:
/// Is/IsAfter/IsBefore/WithinLastDays/DateInRange. Mirrors CE's four base-matcher operator sets
/// (docs/superpowers/specs/2026-08-06-smart-lists-design.md §3).
/// </summary>
public enum SmartListOperator
{
    Is,
    IsNot,
    Contains,
    ContainsAny,
    ContainsAll,
    StartsWith,
    EndsWith,
    GreaterThan,
    LessThan,
    InRange,
    IsAfter,
    IsBefore,
    WithinLastDays,
    DateInRange,
}
