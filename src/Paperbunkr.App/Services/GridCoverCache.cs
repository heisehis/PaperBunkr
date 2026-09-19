using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>
/// The Library grid's own cover cache (docs/superpowers/specs/2026-09-19-library-scroll-smoothness-design.md
/// §3.1). Separate from <see cref="CoverImageCache"/> on purpose: that cache hands the same full-size
/// <see cref="Bitmap"/> to Home, Detail, Events and others that bind it into long-lived view-models, so it is
/// count-bounded (5,000 bitmaps, up to ~4.8 GB) and can never dispose on eviction. The grid only needs a card-sized
/// picture, so this cache holds bitmaps decoded <b>at display size</b> and is bounded by <b>bytes</b>.
///
/// Keyed by <c>(cover stem, width bucket)</c>. The bucket is the display width in device pixels rounded up to a
/// 64 px step (<see cref="BucketFor"/>), so the density slider re-decodes only when it crosses a bucket, and a
/// cover is never upscaled from its bucket. LRU by bytes: the most recently used entries survive, and the newest entry is
/// always kept even if it alone exceeds the budget. <b>Eviction only drops the cache's reference and never
/// disposes</b>: a realized <c>Image</c> may still be showing the bitmap (a disposed bitmap once crashed the app, see
/// <see cref="LruCache{TKey,TValue}"/>); the GC reclaims the native memory once nothing references it.
///
/// Thread-safe (one lock; every operation is O(1) or bounded by the number of evicted entries).
/// </summary>
internal sealed class GridCoverCache
{
    /// <summary>Process-wide instance. 300 MB is a fixed constant, not a Preference (design decision, 2026-09-19).</summary>
    public static readonly GridCoverCache Shared = new(BudgetBytes);

    public const long BudgetBytes = 300L * 1024 * 1024;

    /// <summary>Smallest and largest decode width. The on-disk thumbnails have a 400 px longest edge, so a portrait cover is ~267 px wide: decoding wider than 320 would only upscale.</summary>
    public const int MinBucket = 96;
    public const int MaxBucket = 320;
    public const int BucketStep = 64;

    private sealed record Entry((string Stem, int Bucket) Key, Bitmap Bitmap, long Bytes);

    private readonly object _gate = new();
    private readonly long _budgetBytes;
    private readonly LinkedList<Entry> _lru = new(); // front = most recently used
    private readonly Dictionary<(string Stem, int Bucket), LinkedListNode<Entry>> _map = new();
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

    /// <summary>Stores <paramref name="decoded"/> and returns the instance now cached (an existing entry wins, so two racing decodes of one cover converge on one bitmap).</summary>
    public Bitmap Add(string stem, int bucket, Bitmap decoded) => Add(stem, bucket, decoded, EstimateBytes(decoded));

    /// <summary>Same as <see cref="Add(string,int,Bitmap)"/> with an explicit size, for tests that use tiny stand-in bitmaps.</summary>
    public Bitmap Add(string stem, int bucket, Bitmap decoded, long bytes)
    {
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
                var last = _lru.Last!;
                _lru.RemoveLast();
                _map.Remove(last.Value.Key);
                _bytes -= last.Value.Bytes;
            }

            return decoded;
        }
    }

    /// <summary>Drops every bucket of one cover (its thumbnail changed or was deleted). Never disposes.</summary>
    public void Remove(string stem)
    {
        lock (_gate)
        {
            var doomed = new List<(string Stem, int Bucket)>();
            foreach (var key in _map.Keys)
            {
                if (string.Equals(key.Stem, stem, StringComparison.Ordinal))
                {
                    doomed.Add(key);
                }
            }

            foreach (var key in doomed)
            {
                var node = _map[key];
                _lru.Remove(node);
                _map.Remove(key);
                _bytes -= node.Value.Bytes;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lru.Clear();
            _map.Clear();
            _bytes = 0;
        }
    }
}
