using Avalonia.Media.Imaging;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="CoverDecodeQueue"/>: newest-first, bounded, dequeue-on-recycle, two priorities, dedup (docs/superpowers/specs/
/// 2026-09-19-library-scroll-smoothness-design.md section 3.2). Uses a queue with no worker threads and steps it with
/// <c>RunOne()</c> so the order is exact; one test at the end runs a real worker.
/// </summary>
public class CoverDecodeQueueTests
{
    private static (CoverDecodeQueue Queue, List<string> Order) NewQueue(int visibleCapacity = 256, int prefetchCapacity = 512)
    {
        var order = new List<string>();
        var queue = new CoverDecodeQueue((stem, bucket) =>
        {
            order.Add(stem);
            return null;
        }, workerCount: 0, visibleCapacity, prefetchCapacity);
        return (queue, order);
    }

    private static void Drain(CoverDecodeQueue queue)
    {
        while (queue.RunOne())
        {
        }
    }

    [Fact]
    public void Visible_RunsNewestFirst()
    {
        var (queue, order) = NewQueue();

        queue.Request("A", 192, CoverDecodeQueue.Priority.Visible, _ => { });
        queue.Request("B", 192, CoverDecodeQueue.Priority.Visible, _ => { });
        queue.Request("C", 192, CoverDecodeQueue.Priority.Visible, _ => { });
        Drain(queue);

        Assert.Equal(new[] { "C", "B", "A" }, order);
    }

    [Fact]
    public void ReRequestingAQueuedKey_MovesItToTheFront()
    {
        var (queue, order) = NewQueue();

        queue.Request("A", 192, CoverDecodeQueue.Priority.Visible, _ => { });
        queue.Request("B", 192, CoverDecodeQueue.Priority.Visible, _ => { });
        queue.Request("C", 192, CoverDecodeQueue.Priority.Visible, _ => { });
        queue.Request("A", 192, CoverDecodeQueue.Priority.Visible, _ => { }); // scrolled back to A
        Drain(queue);

        Assert.Equal(new[] { "A", "C", "B" }, order);
    }

    [Fact]
    public void Visible_AlwaysRunsBeforePrefetch_AndPrefetchKeepsItsGivenOrder()
    {
        var (queue, order) = NewQueue();

        queue.Request("P1", 192, CoverDecodeQueue.Priority.Prefetch, null);
        queue.Request("P2", 192, CoverDecodeQueue.Priority.Prefetch, null);
        queue.Request("V", 192, CoverDecodeQueue.Priority.Visible, _ => { });
        Drain(queue);

        Assert.Equal(new[] { "V", "P1", "P2" }, order);
    }

    [Fact]
    public void ARequestForAKeyAlreadyPrefetched_IsPromotedToVisible()
    {
        var (queue, order) = NewQueue();

        queue.Request("P1", 192, CoverDecodeQueue.Priority.Prefetch, null);
        queue.Request("P2", 192, CoverDecodeQueue.Priority.Prefetch, null);
        queue.Request("P2", 192, CoverDecodeQueue.Priority.Visible, _ => { }); // P2 scrolled into view
        Drain(queue);

        Assert.Equal(new[] { "P2", "P1" }, order);
    }

    [Fact]
    public void TheVisibleQueueIsBounded_TheOldestOverflowIsDropped_AndItsWaitersGetNull()
    {
        var (queue, order) = NewQueue(visibleCapacity: 3);
        var results = new Dictionary<string, string>();
        foreach (string stem in new[] { "1", "2", "3", "4", "5" })
        {
            string captured = stem;
            queue.Request(captured, 192, CoverDecodeQueue.Priority.Visible, bitmap => results[captured] = bitmap is null ? "null" : "bitmap");
        }

        Assert.Equal(3, queue.VisibleQueuedCount);
        Assert.Equal(2, queue.OverflowDropped);
        Assert.Equal("null", results["1"]); // dropped: told immediately
        Assert.Equal("null", results["2"]);

        Drain(queue);

        Assert.Equal(new[] { "5", "4", "3" }, order);
    }

    [Fact]
    public void CancellingTheOnlyWaiterOfAQueuedRequest_RemovesItFromTheQueue()
    {
        var (queue, order) = NewQueue();

        queue.Request("A", 192, CoverDecodeQueue.Priority.Visible, _ => { });
        var b = queue.Request("B", 192, CoverDecodeQueue.Priority.Visible, _ => { });
        b!.Cancel(); // the container showing B was recycled before its decode started
        Drain(queue);

        Assert.Equal(new[] { "A" }, order);
        Assert.Equal(1, queue.Dequeued);
    }

    [Fact]
    public void ASharedRequest_SurvivesUntilEveryWaiterHasCancelled()
    {
        var (queue, order) = NewQueue();
        int delivered = 0;

        var first = queue.Request("A", 192, CoverDecodeQueue.Priority.Visible, _ => delivered++);
        var second = queue.Request("A", 192, CoverDecodeQueue.Priority.Visible, _ => delivered++);

        first!.Cancel();
        Assert.Equal(1, queue.VisibleQueuedCount);

        Drain(queue);

        Assert.Equal(new[] { "A" }, order);
        Assert.Equal(1, delivered); // only the waiter that did not cancel
        Assert.NotNull(second);
    }

    [Fact]
    public void CancellingEveryWaiter_DropsTheRequest()
    {
        var (queue, order) = NewQueue();
        var first = queue.Request("A", 192, CoverDecodeQueue.Priority.Visible, _ => { });
        var second = queue.Request("A", 192, CoverDecodeQueue.Priority.Visible, _ => { });

        first!.Cancel();
        second!.Cancel();
        Drain(queue);

        Assert.Empty(order);
        Assert.Equal(1, queue.Dequeued);
    }

    [Fact]
    public void TheSameKeyRequestedTwice_DecodesOnce_AndBothWaitersAreTold()
    {
        var (queue, order) = NewQueue();
        int delivered = 0;

        queue.Request("A", 192, CoverDecodeQueue.Priority.Visible, _ => delivered++);
        queue.Request("A", 192, CoverDecodeQueue.Priority.Visible, _ => delivered++);
        Drain(queue);

        Assert.Equal(new[] { "A" }, order);
        Assert.Equal(2, delivered);
    }

    [Fact]
    public void DifferentBucketsOfOneCover_AreSeparateDecodes()
    {
        var (queue, order) = NewQueue();

        queue.Request("A", 192, CoverDecodeQueue.Priority.Visible, _ => { });
        queue.Request("A", 256, CoverDecodeQueue.Priority.Visible, _ => { });
        Drain(queue);

        Assert.Equal(2, order.Count);
    }

    [Fact]
    public void ADecodeThatThrows_IsSwallowed_AndWaitersGetNull()
    {
        var queue = new CoverDecodeQueue((_, _) => throw new InvalidOperationException("bad jpeg"), workerCount: 0);
        bool called = false;
        bool gotBitmap = true;

        queue.Request("A", 192, CoverDecodeQueue.Priority.Visible, bitmap =>
        {
            called = true;
            gotBitmap = bitmap is not null;
        });
        Drain(queue);

        Assert.True(called);
        Assert.False(gotBitmap);
    }

    [Fact]
    public void AWorkerThread_DecodesAndDelivers()
    {
        using var queue = new CoverDecodeQueue((stem, _) => null, workerCount: 1);
        using var done = new ManualResetEventSlim();
        int workerThread = -1;

        queue.Request("A", 192, CoverDecodeQueue.Priority.Visible, _ =>
        {
            workerThread = Environment.CurrentManagedThreadId;
            done.Set();
        });

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotEqual(Environment.CurrentManagedThreadId, workerThread);
    }
}
