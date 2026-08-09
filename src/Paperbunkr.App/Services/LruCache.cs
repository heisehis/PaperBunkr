using System;
using System.Collections.Generic;

namespace Paperbunkr.App.Services;

/// <summary>
/// Generic bounded cache with least-recently-used eviction - once <c>capacity</c> is exceeded,
/// adding a new entry drops whichever entry was least recently touched. Extracted out of
/// <see cref="CoverImageCache"/>, which previously cached decoded cover <c>Bitmap</c>s for the
/// entire app session with no eviction at all - real usage against a real-sized library grew that
/// unbounded (observed: 220MB at launch to 1.4GB after a few minutes of normal browsing). Not
/// thread-safe - every current caller runs on the UI thread already, matching
/// <see cref="CoverImageCache"/>'s own existing assumption.
///
/// Eviction does NOT call <see cref="IDisposable.Dispose"/> on the dropped value, even though an
/// earlier version of this class did. <see cref="CoverImageCache.Get"/> hands out the exact same
/// <c>Bitmap</c> instance it stores here, and callers (<c>SeriesCardSample</c>,
/// <c>SmartScreenViewModel</c>, <c>DetailTabsViewModel</c>, etc.) bind it straight into a
/// long-lived view-model property - the cache has no way to know whether that Bitmap is still
/// live in the visual tree when it gets evicted. Real repro: browse a large library (2000+
/// issues) until eviction kicks in, then open Smart Lists - a still-displayed <c>Image</c> gets
/// remeasured against a Bitmap this cache had already disposed, throwing
/// <c>ObjectDisposedException</c> out of <c>Image.MeasureOverride</c> and crashing the app. Only
/// dropping the cache's own strong reference (not disposing) keeps the *entry count* bounded,
/// which was the actual goal - the underlying native bitmap still gets reclaimed once nothing
/// else references it, just via GC instead of an eager (and unsafe) explicit Dispose.
/// </summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, TValue> _values = new();
    private readonly LinkedList<TKey> _recencyOrder = new(); // front = most recently used
    private readonly Dictionary<TKey, LinkedListNode<TKey>> _recencyNodes = new();

    public LruCache(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        }

        _capacity = capacity;
    }

    public int Count => _values.Count;

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (_values.TryGetValue(key, out value!))
        {
            Touch(key);
            return true;
        }

        return false;
    }

    /// <summary>Adds or replaces <paramref name="key"/>'s value and marks it most-recently-used, evicting the current least-recently-used entry first if already at capacity.</summary>
    public void Add(TKey key, TValue value)
    {
        if (_values.ContainsKey(key))
        {
            _values[key] = value;
            Touch(key);
            return;
        }

        if (_values.Count >= _capacity)
        {
            EvictLeastRecentlyUsed();
        }

        _values[key] = value;
        _recencyNodes[key] = _recencyOrder.AddFirst(key);
    }

    private void Touch(TKey key)
    {
        if (_recencyNodes.TryGetValue(key, out var node))
        {
            _recencyOrder.Remove(node);
            _recencyNodes[key] = _recencyOrder.AddFirst(key);
        }
    }

    private void EvictLeastRecentlyUsed()
    {
        var lru = _recencyOrder.Last;
        if (lru is null)
        {
            return;
        }

        _recencyOrder.RemoveLast();
        _recencyNodes.Remove(lru.Value);
        _values.Remove(lru.Value);
    }
}
