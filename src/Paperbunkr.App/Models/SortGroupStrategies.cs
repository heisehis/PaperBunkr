using System;

namespace Paperbunkr.App.Models;

/// <summary>
/// Reusable sort/group comparison-logic helpers (docs/superpowers/specs/2026-08-18-issue-list-
/// pluggable-sort-group-design.md) - the "pluggable strategies" the source doc's §39-42 calls for,
/// backing <see cref="Comparison{T}"/>/group-key delegates (the load-bearing per-field shape
/// <c>IssueListFieldCatalog</c> established and deliberately kept, per this spec's own
/// precedent-conflict resolution) rather than a generic strategy-kind indirection layer.
/// </summary>
public static class SortStrategies
{
    public static Comparison<IssueListRow> CaseInsensitiveString(Func<IssueListRow, string?> get) =>
        (a, b) => string.Compare(get(a), get(b), StringComparison.OrdinalIgnoreCase);

    public static Comparison<IssueListRow> Numeric<T>(Func<IssueListRow, T?> get) where T : struct, IComparable<T> =>
        (a, b) => Nullable.Compare(get(a), get(b));

    public static Comparison<IssueListRow> Date(Func<IssueListRow, DateTime?> get) =>
        (a, b) => Nullable.Compare(get(a), get(b));

    public static Comparison<IssueListRow> Boolean(Func<IssueListRow, bool> get) =>
        (a, b) => get(a).CompareTo(get(b));

    /// <summary>Wraps the existing <c>Issue.NumberSortKey</c> path (natural-number-aware, falls back to string sort) - same semantics as the Bulk/Issue Properties editors already use, not reinvented here.</summary>
    public static Comparison<IssueListRow> IssueNumber() =>
        (a, b) => Nullable.Compare(a.NumberSortKey, b.NumberSortKey);
}

public static class GroupStrategies
{
    public static (Func<IssueListRow, string> Key, Comparison<string> Order) Alphabetical(Func<IssueListRow, string?> get, string fallback = "Unknown") =>
        (row => string.IsNullOrWhiteSpace(get(row)) ? fallback : get(row)!,
         (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));

    public static (Func<IssueListRow, string> Key, Comparison<string> Order) NumericBucket(Func<IssueListRow, int?> get) =>
        (row => get(row)?.ToString() ?? "Unknown",
         (a, b) => a == "Unknown" || b == "Unknown"
            ? string.Compare(a, b, StringComparison.OrdinalIgnoreCase)
            : int.Parse(a).CompareTo(int.Parse(b)));

    public static (Func<IssueListRow, string> Key, Comparison<string> Order) Boolean(Func<IssueListRow, bool> get, string trueLabel, string falseLabel) =>
        (row => get(row) ? trueLabel : falseLabel,
         (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Bucket a <c>bool?</c> into 3 labeled groups, ordered Unknown &lt; No &lt; Yes - matching CE's
    /// own <c>YesNo</c> enum ordering (docs/superpowers/specs/2026-09-12-library-sort-group-axes-
    /// design.md §4), not alphabetical like <see cref="Boolean"/> above (alphabetical would misorder
    /// e.g. "Final issue"/"Not final"/"Unknown").
    /// </summary>
    public static (Func<IssueListRow, string> Key, Comparison<string> Order) TriState(
        Func<IssueListRow, bool?> get, string yesLabel, string noLabel, string unknownLabel)
    {
        int Rank(string label) => label == unknownLabel ? 0 : label == noLabel ? 1 : 2;
        return (row => get(row) switch { true => yesLabel, false => noLabel, null => unknownLabel },
                (a, b) => Rank(a).CompareTo(Rank(b)));
    }
}
