using System;
using System.Collections.Generic;

namespace Paperbunkr.App.Services;

/// <summary>
/// Splits a reading list into its group-label runs (docs/superpowers/specs/2026-09-28-reading-lists-build-from-events-design.md §1).
/// Only <em>consecutive</em> items with the same label share a group; a label that comes back later starts a new run with the same header.
/// A plain <c>GroupBy</c> used to pull every item with a label into one block, which reordered an interleaved list on screen and made
/// its numbering stop matching the real order. Null and empty labels are the same (ungrouped) run.
/// </summary>
public static class ReadingListGrouping
{
    public static IReadOnlyList<(string Label, IReadOnlyList<T> Items)> Runs<T>(IEnumerable<T> itemsInOrder, Func<T, string?> label)
    {
        var runs = new List<(string, IReadOnlyList<T>)>();
        List<T>? current = null;
        string? currentLabel = null;
        foreach (var item in itemsInOrder)
        {
            string key = label(item) ?? string.Empty;
            if (current is null || !string.Equals(key, currentLabel, StringComparison.Ordinal))
            {
                current = new List<T>();
                currentLabel = key;
                runs.Add((key, current));
            }

            current.Add(item);
        }

        return runs;
    }
}
