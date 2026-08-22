using System;

namespace Paperbunkr.App.Models;

/// <summary>
/// Reusable sort/group comparison-logic helpers (docs/superpowers/specs/2026-08-18-issue-list-
/// pluggable-sort-group-design.md) - the "pluggable strategies" the source doc's §39-42 calls for,
/// backing <see cref="Comparison{T}"/>/group-key delegates (the load-bearing per-field shape
/// <c>LibraryFieldCatalog</c> already established and deliberately kept, per this spec's own
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
}
