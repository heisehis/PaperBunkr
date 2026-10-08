using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Models;

/// <summary>
/// Stable keys for Home's reorderable sections (docs/superpowers/specs/2026-09-28-home-improvements-design.md I1). Persisted in
/// <c>AppSettings.HomeSectionOrder</c>/<c>HomeHiddenSections</c>, so never rename one - add a new key instead.
/// </summary>
public static class HomeSectionKey
{
    public const string Spotlight = "spotlight";
    public const string NeedsAttention = "needsAttention";
    public const string ContinueReading = "continueReading";
    public const string RecentlyAdded = "recentlyAdded";
    public const string Collections = "collections";
    public const string BecauseYouRead = "becauseYouRead";
    public const string ReadingList = "readingList";

    /// <summary>The ranked "what to read next" list (docs/superpowers/specs/2026-10-06-smart-features-design.md §4.4).</summary>
    public const string UpNext = "upNext";

    /// <summary>Default order - Needs Attention under the spotlight, Up Next right after Continue Reading.</summary>
    public static IReadOnlyList<string> Default { get; } = new[]
    {
        Spotlight, NeedsAttention, ContinueReading, UpNext, RecentlyAdded, Collections, BecauseYouRead, ReadingList,
    };

    public static string DisplayName(string key) => key switch
    {
        Spotlight => "Spotlight",
        NeedsAttention => "Needs attention",
        ContinueReading => "Continue reading",
        UpNext => "Up next",
        RecentlyAdded => "Recently added",
        Collections => "Collections",
        BecauseYouRead => "Because you read",
        ReadingList => "Try this reading list",
        _ => key,
    };
}

/// <summary>The effective section layout: <see cref="Order"/> is every known section, <see cref="Visible"/> the ones to show.</summary>
public sealed record HomeLayoutResult(IReadOnlyList<string> Order, IReadOnlyList<string> Visible, IReadOnlySet<string> Hidden);

/// <summary>
/// Pure translation between the two persisted CSV strings and the effective Home layout (same spec, I1). Unknown keys are
/// dropped and duplicates collapse; a known key missing from a saved order is inserted at its default index (clamped), so a
/// section added in a later release shows up for people who already reordered theirs.
/// </summary>
public static class HomeLayout
{
    public static HomeLayoutResult Resolve(string? orderCsv, string? hiddenCsv)
    {
        var known = HomeSectionKey.Default;
        var order = Split(orderCsv).Where(known.Contains).Distinct().ToList();

        for (int i = 0; i < known.Count; i++)
        {
            string key = known[i];
            if (!order.Contains(key))
            {
                order.Insert(Math.Min(i, order.Count), key);
            }
        }

        var hidden = Split(hiddenCsv).Where(known.Contains).ToHashSet(StringComparer.Ordinal);
        return new HomeLayoutResult(order, order.Where(k => !hidden.Contains(k)).ToList(), hidden);
    }

    /// <summary>Writes a layout back. Returns nulls for the default state so a reset leaves AppSettings clean.</summary>
    public static (string? OrderCsv, string? HiddenCsv) Serialize(IEnumerable<string> order, IEnumerable<string> hidden)
    {
        var orderList = order.ToList();
        var hiddenList = hidden.Where(orderList.Contains).ToList();
        string? orderCsv = orderList.SequenceEqual(HomeSectionKey.Default) ? null : string.Join(",", orderList);
        string? hiddenCsv = hiddenList.Count == 0 ? null : string.Join(",", orderList.Where(hiddenList.Contains));
        return (orderCsv, hiddenCsv);
    }

    private static IEnumerable<string> Split(string? csv)
        => string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
