using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Read/write helper over <see cref="HealthFindingDismissal"/> (docs/superpowers/specs/2026-10-06-smart-features-design.md §2). One
/// dismissal model for every computed finding, so "Not a gap", "This is intended", a dismissed continuity suggestion and "Order is
/// intended" all restore the same way.
/// </summary>
public static class HealthDismissals
{
    /// <summary>A hole in a series' run of issue numbers; key = series id + the missing numbers.</summary>
    public const string CollectionGap = "gap";

    /// <summary>A metadata-consistency finding; key = series id + check name.</summary>
    public const string Consistency = "consistency";

    /// <summary>A "shares characters" continuity suggestion; key = continuity id + series id.</summary>
    public const string ContinuitySuggestion = "continuity-suggest";

    /// <summary>A reading list's order-vs-chronology check; key = list id.</summary>
    public const string ListOrder = "list-order";

    /// <summary>Every dismissed key of <paramref name="kind"/> (ordinal comparison - keys are built by code, never typed).</summary>
    public static HashSet<string> KeysFor(PaperbunkrDbContext context, string kind) =>
        context.HealthFindingDismissals.Where(d => d.Kind == kind).Select(d => d.Key).ToHashSet(StringComparer.Ordinal);

    public static List<HealthFindingDismissal> RowsFor(PaperbunkrDbContext context, string kind) =>
        context.HealthFindingDismissals.Where(d => d.Kind == kind).OrderByDescending(d => d.DismissedAt).ToList();

    public static bool IsDismissed(PaperbunkrDbContext context, string kind, string key) =>
        context.HealthFindingDismissals.Any(d => d.Kind == kind && d.Key == key);

    /// <summary>Idempotent: dismissing an already-dismissed finding changes nothing.</summary>
    public static void Dismiss(PaperbunkrDbContext context, string kind, string key, string? label = null)
    {
        if (IsDismissed(context, kind, key))
        {
            return;
        }

        context.HealthFindingDismissals.Add(new HealthFindingDismissal { Kind = kind, Key = key, Label = label, DismissedAt = DateTime.UtcNow });
        context.SaveChanges();
    }

    /// <summary>Brings a dismissed finding back; true if there was one.</summary>
    public static bool Restore(PaperbunkrDbContext context, string kind, string key)
    {
        var rows = context.HealthFindingDismissals.Where(d => d.Kind == kind && d.Key == key).ToList();
        if (rows.Count == 0)
        {
            return false;
        }

        context.HealthFindingDismissals.RemoveRange(rows);
        context.SaveChanges();
        return true;
    }
}
