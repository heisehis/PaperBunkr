using System;
using System.Collections.Generic;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Services;

/// <summary>
/// Warms <see cref="GridCoverCache"/> for the cards a scroll is about to reveal (docs/superpowers/specs/
/// 2026-09-19-library-scroll-smoothness-design.md §3.3). When the panel's realized row range changes it reports the range
/// and the scroll direction; this asks <see cref="CoverDecodeQueue"/> to decode the next ~<see cref="ViewportsAhead"/>
/// viewports of covers <b>beyond</b> the realized range at <see cref="CoverDecodeQueue.Priority.Prefetch"/>. Prefetch only fills the
/// cache: it never realizes a container, and visible requests always run before it. A direction reversal or a jump drops
/// the pending prefetch so the queue does not decode covers the user has turned away from.
/// </summary>
internal static class CoverPrefetcher
{
    /// <summary>How far ahead of the scroll direction to decode, in viewports of cards.</summary>
    public const int ViewportsAhead = 2;

    /// <summary>
    /// Item indexes to prefetch, nearest first. <paramref name="direction"/> &gt; 0 = scrolling toward higher indexes, &lt; 0 = toward
    /// lower, 0 = unknown (first range, or no movement): half the budget goes each way, alternating so the nearest cards on both sides
    /// come first. Never returns an index inside the realized range or outside <c>[0, itemCount)</c>.
    /// </summary>
    public static IEnumerable<int> ComputeIndexes(int itemCount, int firstRealized, int lastRealized, int direction, int itemsPerRow, int visibleRows)
    {
        if (itemCount <= 0 || lastRealized < firstRealized)
        {
            yield break;
        }

        int budget = Math.Max(1, ViewportsAhead * Math.Max(1, visibleRows) * Math.Max(1, itemsPerRow));

        if (direction > 0)
        {
            for (int i = 1; i <= budget && lastRealized + i < itemCount; i++)
            {
                yield return lastRealized + i;
            }

            yield break;
        }

        if (direction < 0)
        {
            for (int i = 1; i <= budget && firstRealized - i >= 0; i++)
            {
                yield return firstRealized - i;
            }

            yield break;
        }

        int half = Math.Max(1, budget / 2);
        for (int i = 1; i <= half; i++)
        {
            if (lastRealized + i < itemCount)
            {
                yield return lastRealized + i;
            }

            if (firstRealized - i >= 0)
            {
                yield return firstRealized - i;
            }
        }
    }

    /// <summary>True when the scroll turned around or jumped far enough that pending prefetch requests are no longer useful.</summary>
    public static bool ShouldDropPending(int previousDirection, int direction, int previousFirst, int first, int realizedCount)
    {
        if (direction != 0 && previousDirection != 0 && direction != previousDirection)
        {
            return true;
        }

        // A jump (A-Z index, scrollbar drag): the new range does not overlap or touch the old one.
        return previousFirst >= 0 && Math.Abs(first - previousFirst) > Math.Max(1, realizedCount);
    }

    /// <summary>Queues prefetch decodes for the given items that are not already cached.</summary>
    public static int Prefetch(IReadOnlyList<object?> items, IEnumerable<int> indexes, double decodeWidthDip, double renderScaling)
    {
        if (CoverPipelineStats.ForceLegacyCoverPath)
        {
            return 0; // harness comparison run: the legacy path has no prefetch
        }

        int bucket = GridCoverCache.BucketFor(decodeWidthDip, renderScaling);
        int queued = 0;
        foreach (int index in indexes)
        {
            if (index < 0 || index >= items.Count || items[index] is not ICoverKeyProvider { CoverKey: { } stem })
            {
                continue;
            }

            if (GridCoverCache.Shared.TryGet(stem, bucket, out _))
            {
                continue;
            }

            CoverDecodeQueue.Shared.Request(stem, bucket, CoverDecodeQueue.Priority.Prefetch, null);
            queued++;
        }

        return queued;
    }
}
