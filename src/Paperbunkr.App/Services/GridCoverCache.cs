using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services.Performance;

namespace Paperbunkr.App.Services;

/// <summary>
/// The Library grid's own cover cache (docs/superpowers/specs/2026-09-19-library-scroll-smoothness-design.md
/// §3.1). Separate from <see cref="CoverImageCache"/> on purpose: that cache hands the same full-size
/// <see cref="Bitmap"/> to Home, Detail, Events and others that bind it into long-lived view-models, so it can never
/// dispose on eviction. The grid only needs a card-sized picture, so this cache holds bitmaps decoded
/// <b>at display size</b> and is bounded by <b>bytes</b>.
///
/// Keyed by <c>(cover stem, width bucket)</c>. The bucket is the display width in device pixels rounded up to a
/// 64 px step (<see cref="BucketFor"/>), so the density slider re-decodes only when it crosses a bucket, and a
/// cover is never upscaled from its bucket. LRU by bytes: the most recently used entries survive, and the newest entry is
/// always kept even if it alone exceeds the budget.
///
/// <b>Leases decide when an evicted bitmap is disposed</b> (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md §4.2).
/// A bitmap is a small managed object around a large native buffer, so one that is merely dropped can sit undisposed until a late
/// full collection, and a byte budget on the cache then does not bound memory. The only consumer that shows these bitmaps is
/// <c>AsyncCoverImage</c>: it takes a lease (<see cref="TryLease"/>) while an <c>Image</c> displays the bitmap and gives it back
/// (<see cref="Release"/>) when the image is re-pointed. An entry leaving the cache with no lease is disposed at once; one that is
/// still leased is disposed when its last lease is released. A displayed bitmap is therefore never disposed (a disposed bitmap
/// once crashed the app, see <see cref="LruCache{TKey,TValue}"/>). A lease that is never released (its <c>Image</c> was simply
/// dropped) costs nothing: lease state is held weakly, so the bitmap falls back to the collector as before.
///
/// Thread-safe (one lock; every operation is O(1) or bounded by the number of evicted entries). Bitmaps are disposed outside the lock.
/// </summary>
internal sealed class GridCoverCache
{
    /// <summary>Process-wide instance, sized from installed RAM (see <see cref="ImageMemoryBudget"/>): about 230 MB on an 8 GB machine.</summary>
    public static readonly GridCoverCache Shared = new(BudgetBytes);

    public static long BudgetBytes => ImageMemoryBudget.GridBytes;

    /// <summary>Smallest and largest decode width. The on-disk thumbnails have a 400 px longest edge, so a portrait cover is ~267 px wide: decoding wider than 320 would only upscale.</summary>
    public const int MinBucket = 96;
    public const int MaxBucket = 320;
    public const int BucketStep = 64;

    private sealed record Entry((string Stem, int Bucket) Key, Bitmap Bitmap, long Bytes);

    private sealed class LeaseState
    {
        public int Count;
        public bool Evicted;
    }

    private readonly object _gate = new();
    private readonly long _budgetBytes;
    private readonly LinkedList<Entry> _lru = new(); // front = most recently used
    private readonly Dictionary<(string Stem, int Bucket), LinkedListNode<Entry>> _map = new();

    // Weak on purpose: an Image that is dropped without releasing its lease must not keep an evicted bitmap alive.
    private readonly ConditionalWeakTable<Bitmap, LeaseState> _leases = new();
    private long _bytes;

    public GridCoverCache(long budgetBytes)
    {
        if (budgetBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(budgetBytes));
        }

        _budgetBytes = budgetBytes;
    }

    public long Bytes
    {
        get { lock (_gate) { return _bytes; } }
    }

    public int Count
    {
        get { lock (_gate) { return _map.Count; } }
    }

    /// <summary>Decode width bucket for a card cover of <paramref name="displayWidthDip"/> device-independent pixels at <paramref name="renderScaling"/>.</summary>
    public static int BucketFor(double displayWidthDip, double renderScaling)
    {
        double pixels = Math.Max(1, displayWidthDip) * Math.Max(1, renderScaling);
        int bucket = (int)(Math.Ceiling(pixels / BucketStep) * BucketStep);
        return Math.Clamp(bucket, MinBucket, MaxBucket);
    }

    public static long EstimateBytes(Bitmap bitmap) => (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;

    /// <summary>
    /// Looks an entry up <b>without</b> leasing it. For callers that only need to know it is cached (the prefetcher, the decoder's
    /// duplicate check): the bitmap may be disposed by a later eviction, so a caller that will display it uses <see cref="TryLease"/>.
    /// </summary>
    public bool TryGet(string stem, int bucket, out Bitmap? bitmap)
    {
        lock (_gate)
        {
            if (_map.TryGetValue((stem, bucket), out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                bitmap = node.Value.Bitmap;
                return true;
            }
        }

        bitmap = null;
        return false;
    }

    /// <summary>
    /// Looks an entry up and takes a lease on its bitmap, so it stays undisposed until <see cref="Release"/> however the cache
    /// evicts meanwhile. Every successful call must be paired with one <see cref="Release"/> of the returned bitmap.
    /// </summary>
    public bool TryLease(string stem, int bucket, out Bitmap? bitmap)
    {
        lock (_gate)
        {
            if (_map.TryGetValue((stem, bucket), out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                bitmap = node.Value.Bitmap;
                _leases.GetOrCreateValue(bitmap).Count++;
                return true;
            }
        }

        bitmap = null;
        return false;
    }

    /// <summary>Gives back one lease taken by <see cref="TryLease"/>. Disposes the bitmap if that was its last lease and the cache has already let it go.</summary>
    public void Release(Bitmap bitmap)
    {
        bool dispose = false;
        lock (_gate)
        {
            if (_leases.TryGetValue(bitmap, out var state))
            {
                state.Count = Math.Max(0, state.Count - 1);
                if (state.Count == 0)
                {
                    dispose = state.Evicted;
                    _leases.Remove(bitmap);
                }
            }
        }

        if (dispose)
        {
            DisposeQuietly(bitmap);
        }
    }

    /// <summary>Leases currently held on <paramref name="bitmap"/> (tests).</summary>
    internal int LeaseCount(Bitmap bitmap)
    {
        lock (_gate)
        {
            return _leases.TryGetValue(bitmap, out var state) ? state.Count : 0;
        }
    }

    /// <summary>Stores <paramref name="decoded"/> and returns the instance now cached (an existing entry wins, so two racing decodes of one cover converge on one bitmap; the caller still owns a losing <paramref name="decoded"/>).</summary>
    public Bitmap Add(string stem, int bucket, Bitmap decoded) => Add(stem, bucket, decoded, EstimateBytes(decoded));

    /// <summary>Same as <see cref="Add(string,int,Bitmap)"/> with an explicit size, for tests that use tiny stand-in bitmaps.</summary>
    public Bitmap Add(string stem, int bucket, Bitmap decoded, long bytes)
    {
        List<Bitmap>? doomed = null;
        lock (_gate)
        {
            var key = (stem, bucket);
            if (_map.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                return existing.Value.Bitmap;
            }

            var node = _lru.AddFirst(new Entry(key, decoded, bytes));
            _map[key] = node;
            _bytes += bytes;

            // Evict least-recently-used entries until back under budget, always keeping the entry just added.
            while (_bytes > _budgetBytes && _lru.Count > 1)
            {
                EvictLocked(_lru.Last!, ref doomed);
            }
        }

        DisposeAll(doomed);
        return decoded;
    }

    /// <summary>Drops every bucket of one cover (its thumbnail changed or was deleted).</summary>
    public void Remove(string stem)
    {
        List<Bitmap>? doomed = null;
        lock (_gate)
        {
            var matches = new List<LinkedListNode<Entry>>();
            foreach (var pair in _map)
            {
                if (string.Equals(pair.Key.Stem, stem, StringComparison.Ordinal))
                {
                    matches.Add(pair.Value);
                }
            }

            foreach (var node in matches)
            {
                EvictLocked(node, ref doomed);
            }
        }

        DisposeAll(doomed);
    }

    /// <summary>Drops least-recently-used entries until at most <paramref name="targetBytes"/> remain (memory-pressure trim).</summary>
    public void Trim(long targetBytes)
    {
        List<Bitmap>? doomed = null;
        lock (_gate)
        {
            while (_bytes > Math.Max(0, targetBytes) && _lru.Count > 0)
            {
                EvictLocked(_lru.Last!, ref doomed);
            }
        }

        DisposeAll(doomed);
    }

    public void Clear() => Trim(0);

    /// <summary>Takes one entry out of the cache: handed back for disposal (outside the lock) when nothing leases it, otherwise flagged so its last <see cref="Release"/> disposes it.</summary>
    private void EvictLocked(LinkedListNode<Entry> node, ref List<Bitmap>? doomed)
    {
        var entry = node.Value;
        _lru.Remove(node);
        _map.Remove(entry.Key);
        _bytes -= entry.Bytes;

        if (_leases.TryGetValue(entry.Bitmap, out var state) && state.Count > 0)
        {
            state.Evicted = true;
        }
        else
        {
            (doomed ??= new List<Bitmap>()).Add(entry.Bitmap);
        }
    }

    private static void DisposeAll(List<Bitmap>? doomed)
    {
        if (doomed is null)
        {
            return;
        }

        foreach (var bitmap in doomed)
        {
            DisposeQuietly(bitmap);
        }
    }

    private static void DisposeQuietly(Bitmap bitmap)
    {
        try
        {
            bitmap.Dispose();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // Already gone: nothing left to free.
        }
    }
}
