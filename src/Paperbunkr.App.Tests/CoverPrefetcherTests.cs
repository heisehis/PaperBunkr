using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Which items <see cref="CoverPrefetcher"/> asks for, and when it drops pending prefetch (docs/superpowers/specs/
/// 2026-09-19-library-scroll-smoothness-design.md section 3.3). Pure index math, plus the queue's ClearPrefetch.
/// </summary>
public class CoverPrefetcherTests
{
    [Fact]
    public void ScrollingDown_PrefetchesTwoViewportsBeyondTheLastRealizedItem_NearestFirst()
    {
        // 6 items per row, 4 visible rows -> budget 2 * 4 * 6 = 48.
        var indexes = CoverPrefetcher.ComputeIndexes(itemCount: 1000, firstRealized: 100, lastRealized: 159, direction: 1, itemsPerRow: 6, visibleRows: 4).ToList();

        Assert.Equal(48, indexes.Count);
        Assert.Equal(160, indexes[0]);
        Assert.Equal(207, indexes[^1]);
        Assert.Equal(indexes.OrderBy(i => i), indexes);
    }

    [Fact]
    public void ScrollingUp_PrefetchesBelowTheFirstRealizedItem_NearestFirst()
    {
        var indexes = CoverPrefetcher.ComputeIndexes(1000, 100, 159, direction: -1, itemsPerRow: 6, visibleRows: 4).ToList();

        Assert.Equal(48, indexes.Count);
        Assert.Equal(99, indexes[0]);
        Assert.Equal(52, indexes[^1]);
    }

    [Fact]
    public void UnknownDirection_SplitsTheBudgetAcrossBothSides_Alternating()
    {
        var indexes = CoverPrefetcher.ComputeIndexes(1000, 100, 159, direction: 0, itemsPerRow: 6, visibleRows: 4).ToList();

        Assert.Equal(new[] { 160, 99, 161, 98 }, indexes.Take(4));
        Assert.Equal(48, indexes.Count); // 24 each side
    }

    [Fact]
    public void NeverReturnsIndexesOutsideTheListOrInsideTheRealizedRange()
    {
        var atEnd = CoverPrefetcher.ComputeIndexes(itemCount: 170, firstRealized: 100, lastRealized: 159, direction: 1, itemsPerRow: 6, visibleRows: 4).ToList();
        Assert.Equal(Enumerable.Range(160, 10), atEnd);

        var atStart = CoverPrefetcher.ComputeIndexes(itemCount: 1000, firstRealized: 3, lastRealized: 60, direction: -1, itemsPerRow: 6, visibleRows: 4).ToList();
        Assert.Equal(new[] { 2, 1, 0 }, atStart);

        Assert.All(CoverPrefetcher.ComputeIndexes(1000, 100, 159, 0, 6, 4), i => Assert.True(i is < 100 or > 159));
    }

    [Fact]
    public void EmptyOrInvalidRanges_YieldNothing()
    {
        Assert.Empty(CoverPrefetcher.ComputeIndexes(0, 0, 0, 1, 6, 4));
        Assert.Empty(CoverPrefetcher.ComputeIndexes(100, 10, 5, 1, 6, 4));
    }

    [Fact]
    public void ADirectionReversal_DropsPendingPrefetch()
    {
        Assert.True(CoverPrefetcher.ShouldDropPending(previousDirection: 1, direction: -1, previousFirst: 100, first: 90, realizedCount: 60));
        Assert.False(CoverPrefetcher.ShouldDropPending(previousDirection: 1, direction: 1, previousFirst: 100, first: 106, realizedCount: 60));
        Assert.False(CoverPrefetcher.ShouldDropPending(previousDirection: 0, direction: 1, previousFirst: -1, first: 0, realizedCount: 60));
    }

    [Fact]
    public void AJumpFartherThanTheRealizedWindow_DropsPendingPrefetch()
    {
        Assert.True(CoverPrefetcher.ShouldDropPending(1, 1, previousFirst: 0, first: 900, realizedCount: 60));
        Assert.False(CoverPrefetcher.ShouldDropPending(1, 1, previousFirst: 0, first: 30, realizedCount: 60));
    }

    [Fact]
    public void ClearPrefetch_DropsQueuedPrefetch_ButKeepsVisibleRequests()
    {
        var order = new List<string>();
        var queue = new CoverDecodeQueue((stem, _) =>
        {
            order.Add(stem);
            return null;
        }, workerCount: 0);

        queue.Request("P1", 192, CoverDecodeQueue.Priority.Prefetch, null);
        queue.Request("P2", 192, CoverDecodeQueue.Priority.Prefetch, null);
        queue.Request("V", 192, CoverDecodeQueue.Priority.Visible, _ => { });

        queue.ClearPrefetch();
        while (queue.RunOne())
        {
        }

        Assert.Equal(new[] { "V" }, order);
    }
}
