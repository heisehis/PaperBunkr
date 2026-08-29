namespace Paperbunkr.Data.Entities;

/// <summary>
/// Condition operators. Which subset is valid depends on the field's <see cref="SmartListDataType"/>
/// — Text: Is/IsNot/Contains/ContainsAny/ContainsAll/StartsWith/EndsWith/ListContains/RegularExpression.
/// Number: Is/IsNot/GreaterThan/LessThan/InRange. Toggle: Is/IsNot. Date:
/// Is/IsAfter/IsBefore/WithinLastDays/DateInRange. Mirrors CE's four base-matcher operator sets
/// (docs/superpowers/specs/2026-08-06-smart-lists-design.md §3), with <see cref="ListContains"/> and
/// <see cref="RegularExpression"/> added in docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md §3
/// to close the last two gaps vs CE's <c>ComicBookStringMatcher</c> (operators 6 and 7).
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

    /// <summary>
    /// CE operator 6 — the match value is one whole item of a <c>,</c>/<c>;</c>-delimited list
    /// field (Writer, Characters, Teams, ...), not a substring: "Lee" matches the writer <c>Lee</c>
    /// but not <c>Leeroy</c>. See <c>_reference/ComicRackCE/ComicRack.Engine/ComicBookStringMatcher.cs:114-118,152</c>.
    /// </summary>
    ListContains,

    /// <summary>
    /// CE operator 7 — the match value is a raw .NET regex evaluated against the field. Malformed
    /// patterns and timeouts silently match nothing rather than surfacing an error
    /// (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md §3).
    /// </summary>
    RegularExpression,
}
