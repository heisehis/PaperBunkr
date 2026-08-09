using System;
using System.Collections.Generic;

namespace Paperbunkr.App.Services;

/// <summary>
/// Generic bounded cache with least-recently-used eviction - once <c>capacity</c> is exceeded,
/// adding a new entry evicts (and, if <typeparamref name="TValue"/> is <see cref="IDisposable"/>,
/// disposes) whichever entry was least recently touched. Extracted out of
/// <see cref="CoverImageCache"/>, which previously cached decoded cover <c>Bitmap</c>s for the
/// entire app session with no eviction at all - real usage against a real-sized library grew that
/// unbounded (observed: 220MB at launch to 1.4GB after a few minutes of normal browsing). Not
/// thread-safe - every current caller runs on the UI thread already, matching
/// <see cref="CoverImageCache"/>'s own existing assumption.
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
        if (_values.Remove(lru.Value, out var evicted) && evicted is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
