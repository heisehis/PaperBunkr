using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>
/// Least-recently-used cache of decoded bitmaps bounded by <b>bytes</b>, for the caches that hand one shared full-thumbnail
/// <see cref="Bitmap"/> to several long-lived view models (<see cref="CoverImageCache"/>, <see cref="BookCoverImageCache"/>).
/// Replaces their 5,000-entry <see cref="LruCache{TKey,TValue}"/> (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md
/// §4.2): a count bound let the whole library's covers stay decoded at once. Like that cache, eviction only drops this cache's
/// reference and <b>never disposes</b> (a still-displayed <c>Image</c> may hold the bitmap); <see cref="NativeBitmapPressure"/>
/// makes the collector reclaim dropped ones promptly. The newest entry is always kept. Thread-safe.
/// </summary>
public sealed class BitmapByteCache<TKey> where TKey : notnull
{
    private sealed record Entry(TKey Key, Bitmap Bitmap, long Bytes);

    private readonly object _gate = new();
    private readonly long _budgetBytes;
    private readonly LinkedList<Entry> _lru = new(); // front = most recently used
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map = new();
    private long _bytes;

    public BitmapByteCache(long budgetBytes)
    {
        if (budgetBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(budgetBytes));
        }

        _budgetBytes = budgetBytes;
    }

    public int Count
    {
        get { lock (_gate) { return _map.Count; } }
    }

    public long Bytes
    {
        get { lock (_gate) { return _bytes; } }
    }

    public bool TryGetValue(TKey key, out Bitmap? bitmap)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
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

    /// <summary>Adds or replaces <paramref name="key"/>'s bitmap and marks it most recently used.</summary>
    public void Add(TKey key, Bitmap bitmap) => Add(key, bitmap, GridCoverCache.EstimateBytes(bitmap));

    /// <summary>Same as <see cref="Add(TKey,Bitmap)"/> with an explicit size, for tests that use tiny stand-in bitmaps.</summary>
    public void Add(TKey key, Bitmap bitmap, long bytes)
    {
        NativeBitmapPressure.Track(bitmap);
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
                _map.Remove(key);
                _bytes -= existing.Value.Bytes;
            }

            _map[key] = _lru.AddFirst(new Entry(key, bitmap, bytes));
            _bytes += bytes;

            // Always keep the entry just added, even if it alone exceeds the budget.
            while (_bytes > _budgetBytes && _lru.Count > 1)
            {
                DropLastLocked();
            }
        }
    }

    public bool Remove(TKey key)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue(key, out var node))
            {
                return false;
            }

            _lru.Remove(node);
            _map.Remove(key);
            _bytes -= node.Value.Bytes;
            return true;
        }
    }

    /// <summary>Drops least-recently-used entries until at most <paramref name="targetBytes"/> remain (memory-pressure trim). Never disposes.</summary>
    public void Trim(long targetBytes)
    {
        lock (_gate)
        {
            while (_bytes > Math.Max(0, targetBytes) && _lru.Count > 0)
            {
                DropLastLocked();
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

    private void DropLastLocked()
    {
        var last = _lru.Last!;
        _lru.RemoveLast();
        _map.Remove(last.Value.Key);
        _bytes -= last.Value.Bytes;
    }
}
