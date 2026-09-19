using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>
/// The Library grid's cover decode scheduler (docs/superpowers/specs/2026-09-19-library-scroll-smoothness-design.md
/// §3.2), modelled on ComicRack's <c>ImagePool</c> queue (<c>_reference/ComicRackCE</c>: newest-first, bounded, worker
/// threads at low priority). It replaces one unprioritized, uncancellable <c>Task.Run</c> per cache miss, which under a fast
/// fling queued dozens of decodes for covers already scrolled past ahead of the covers actually on screen.
///
/// <list type="bullet">
///   <item><b>Newest-first.</b> The most recently requested visible cover decodes first; re-requesting a queued key moves it
///   to the front (it is on screen again).</item>
///   <item><b>Bounded.</b> At most <c>visibleCapacity</c> queued visible requests; the oldest overflow is dropped and its waiters
///   are told <c>null</c> (they belong to containers that were recycled long ago).</item>
///   <item><b>Dequeue on recycle.</b> A container that is recycled before its decode starts cancels its <see cref="Ticket"/>; a
///   queued request with no waiters left is removed. CE only drops overflow; here the recycle moment is known exactly. A decode
///   already running cannot be cancelled, but its result still lands in the cache, so scrolling back finds it.</item>
///   <item><b>Two priorities.</b> <see cref="Priority.Visible"/> always runs before <see cref="Priority.Prefetch"/>
///   (nearest-first order given by the caller); prefetch requests have no waiters and only fill the cache.</item>
///   <item><b>Dedup.</b> One decode per <c>(stem, bucket)</c> however many containers ask.</item>
/// </list>
///
/// Worker threads run at below-normal priority. Callbacks run on the worker thread; UI work must be posted by the
/// callback itself.
/// </summary>
internal sealed class CoverDecodeQueue : IDisposable
{
    public enum Priority
    {
        Visible,
        Prefetch,
    }

    /// <summary>A caller's claim on one request. <see cref="Cancel"/> withdraws it (and dequeues the request when nobody else waits).</summary>
    public sealed class Ticket
    {
        private readonly CoverDecodeQueue _owner;

        internal Ticket(CoverDecodeQueue owner, Action<Bitmap?> callback)
        {
            _owner = owner;
            Callback = callback;
        }

        internal Action<Bitmap?> Callback { get; }

        internal Job? Job { get; set; }

        internal bool Cancelled { get; set; }

        public void Cancel() => _owner.Cancel(this);
    }

    internal sealed class Job
    {
        public required (string Stem, int Bucket) Key { get; init; }
        public List<Ticket> Waiters { get; } = new();
        public LinkedListNode<Job>? Node { get; set; }
        public bool IsVisible { get; set; }
        public bool Running { get; set; }
        public long EnqueuedAt { get; init; } = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    private readonly object _gate = new();
    private readonly Func<string, int, Bitmap?> _decode;
    private readonly int _visibleCapacity;
    private readonly int _prefetchCapacity;
    private readonly LinkedList<Job> _visible = new(); // front = newest = next to run
    private readonly LinkedList<Job> _prefetch = new(); // front = nearest = next to run
    private readonly Dictionary<(string Stem, int Bucket), Job> _pending = new();
    private readonly List<Thread> _workers = new();
    private bool _shutdown;

    public CoverDecodeQueue(Func<string, int, Bitmap?> decode, int workerCount, int visibleCapacity = 256, int prefetchCapacity = 512)
    {
        _decode = decode;
        _visibleCapacity = visibleCapacity;
        _prefetchCapacity = prefetchCapacity;

        for (int i = 0; i < workerCount; i++)
        {
            var thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = $"CoverDecode-{i}",
                Priority = ThreadPriority.BelowNormal,
            };
            _workers.Add(thread);
            thread.Start();
        }
    }

    /// <summary>Process-wide queue over <see cref="GridCoverDecoder"/>: <c>2 x cores</c> workers clamped to 4..8, at below-normal priority so the UI thread always wins. More workers than cores helps when a decode waits on a slow disk (measured: 3 workers popped in far more than the unbounded pool it replaced under a 25 ms simulated read), and costs nothing when decodes are CPU-bound because the threads then just share the cores below the UI thread.</summary>
    public static readonly CoverDecodeQueue Shared = new(GridCoverDecoder.DecodeAndCache, Math.Clamp(Environment.ProcessorCount * 2, 4, 8));

    /// <summary>Requests dropped because the visible queue overflowed (oldest first).</summary>
    public int OverflowDropped { get; private set; }

    /// <summary>Requests removed because every waiter recycled away before the decode started.</summary>
    public int Dequeued { get; private set; }

    public int QueuedCount
    {
        get { lock (_gate) { return _visible.Count + _prefetch.Count; } }
    }

    public int VisibleQueuedCount
    {
        get { lock (_gate) { return _visible.Count; } }
    }

    /// <summary>
    /// Queues (or joins) a decode of <paramref name="stem"/> at <paramref name="bucket"/>. <paramref name="onDone"/> receives the
    /// decoded bitmap (already stored in <see cref="GridCoverCache"/>), or <see langword="null"/> when the decode failed or the
    /// request was dropped; it is <see langword="null"/>-able for prefetch, which only fills the cache. Returns the ticket to cancel,
    /// or <see langword="null"/> when no callback was given.
    /// </summary>
    public Ticket? Request(string stem, int bucket, Priority priority, Action<Bitmap?>? onDone)
    {
        var dropped = new List<Ticket>();
        Ticket? ticket = onDone is null ? null : new Ticket(this, onDone);
        var key = (stem, bucket);

        lock (_gate)
        {
            if (_pending.TryGetValue(key, out var existing))
            {
                if (ticket is not null)
                {
                    existing.Waiters.Add(ticket);
                    ticket.Job = existing;
                }

                if (!existing.Running && priority == Priority.Visible)
                {
                    // On screen (again): newest-first, and out of the prefetch lane if it was only prefetched.
                    existing.Node!.List!.Remove(existing.Node);
                    existing.IsVisible = true;
                    existing.Node = _visible.AddFirst(existing);
                }

                return ticket;
            }

            var job = new Job { Key = key, IsVisible = priority == Priority.Visible };
            if (ticket is not null)
            {
                job.Waiters.Add(ticket);
                ticket.Job = job;
            }

            job.Node = priority == Priority.Visible ? _visible.AddFirst(job) : _prefetch.AddLast(job);
            _pending[key] = job;

            if (_visible.Count > _visibleCapacity)
            {
                DropLast(_visible, dropped);
            }

            if (_prefetch.Count > _prefetchCapacity)
            {
                DropLast(_prefetch, dropped);
            }

            Monitor.Pulse(_gate);
        }

        foreach (var lost in dropped)
        {
            SafeInvoke(lost.Callback, null);
        }

        return ticket;
    }

    /// <summary>Drops every queued prefetch request (the scroll turned around or jumped). Visible requests and running decodes are untouched.</summary>
    public void ClearPrefetch()
    {
        lock (_gate)
        {
            foreach (var job in _prefetch)
            {
                _pending.Remove(job.Key);
            }

            _prefetch.Clear();
        }
    }

    private void DropLast(LinkedList<Job> list, List<Ticket> dropped)
    {
        var last = list.Last!;
        list.RemoveLast();
        _pending.Remove(last.Value.Key);
        OverflowDropped++;
        foreach (var waiter in last.Value.Waiters.Where(w => !w.Cancelled))
        {
            dropped.Add(waiter);
        }
    }

    private void Cancel(Ticket ticket)
    {
        lock (_gate)
        {
            if (ticket.Cancelled)
            {
                return;
            }

            ticket.Cancelled = true;
            var job = ticket.Job;
            if (job is null)
            {
                return;
            }

            job.Waiters.Remove(ticket);
            if (!job.Running && job.IsVisible && job.Waiters.Count == 0 && job.Node is { List: { } list })
            {
                list.Remove(job.Node);
                job.Node = null;
                _pending.Remove(job.Key);
                Dequeued++;
            }
        }
    }

    private Job? TakeNext()
    {
        var node = _visible.First ?? _prefetch.First;
        if (node is null)
        {
            return null;
        }

        node.List!.Remove(node);
        var job = node.Value;
        job.Node = null;
        job.Running = true;
        return job;
    }

    private void WorkerLoop()
    {
        while (true)
        {
            Job? job;
            lock (_gate)
            {
                while (!_shutdown && _visible.Count == 0 && _prefetch.Count == 0)
                {
                    Monitor.Wait(_gate);
                }

                if (_shutdown)
                {
                    return;
                }

                job = TakeNext();
            }

            if (job is not null)
            {
                Process(job);
            }
        }
    }

    /// <summary>Runs the next queued job on the calling thread (tests use a queue with no workers to step deterministically).</summary>
    internal bool RunOne()
    {
        Job? job;
        lock (_gate)
        {
            job = TakeNext();
        }

        if (job is null)
        {
            return false;
        }

        Process(job);
        return true;
    }

    private void Process(Job job)
    {
        Bitmap? result = null;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            CoverPipelineStats.DecodeStarted();
            result = _decode(job.Key.Stem, job.Key.Bucket);
        }
        catch
        {
            // A cover that fails to decode just stays on its gradient placeholder.
        }

        long finished = System.Diagnostics.Stopwatch.GetTimestamp();
        CoverPipelineStats.DecodeTimed(
            (long)System.Diagnostics.Stopwatch.GetElapsedTime(started, finished).TotalMicroseconds,
            (long)System.Diagnostics.Stopwatch.GetElapsedTime(job.EnqueuedAt, started).TotalMicroseconds);

        Ticket[] waiters;
        lock (_gate)
        {
            _pending.Remove(job.Key);
            waiters = job.Waiters.Where(w => !w.Cancelled).ToArray();
            job.Waiters.Clear();
        }

        foreach (var waiter in waiters)
        {
            SafeInvoke(waiter.Callback, result);
        }
    }

    private static void SafeInvoke(Action<Bitmap?> callback, Bitmap? bitmap)
    {
        try
        {
            callback(bitmap);
        }
        catch
        {
            // A waiter's own failure must not kill a worker thread.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _shutdown = true;
            Monitor.PulseAll(_gate);
        }
    }
}
