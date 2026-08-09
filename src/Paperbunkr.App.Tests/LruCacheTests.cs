using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="LruCache{TKey,TValue}"/> directly - cheap fake values, no real
/// Bitmaps/files needed (that's <see cref="CoverImageCacheTests"/>'s job, for the one real caller
/// today). No Avalonia app context needed.
/// </summary>
public class LruCacheTests
{
    private sealed class TrackedDisposable : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }

    [Fact]
    public void Add_WithinCapacity_KeepsAllEntries()
    {
        var cache = new LruCache<int, string>(capacity: 3);

        cache.Add(1, "a");
        cache.Add(2, "b");
        cache.Add(3, "c");

        Assert.Equal(3, cache.Count);
        Assert.True(cache.TryGetValue(1, out var a) && a == "a");
        Assert.True(cache.TryGetValue(2, out var b) && b == "b");
        Assert.True(cache.TryGetValue(3, out var c) && c == "c");
    }

    [Fact]
    public void Add_ExceedingCapacity_EvictsLeastRecentlyUsed()
    {
        var cache = new LruCache<int, string>(capacity: 2);
        cache.Add(1, "a");
        cache.Add(2, "b");

        cache.Add(3, "c"); // 1 was never touched since being added - least recently used

        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGetValue(1, out _));
        Assert.True(cache.TryGetValue(2, out _));
        Assert.True(cache.TryGetValue(3, out _));
    }

    [Fact]
    public void TryGetValue_RefreshesRecency_SoTouchedEntrySurvivesLaterEviction()
    {
        var cache = new LruCache<int, string>(capacity: 2);
        cache.Add(1, "a");
        cache.Add(2, "b");

        cache.TryGetValue(1, out _); // touch 1 - now 2 is the least recently used
        cache.Add(3, "c");

        Assert.True(cache.TryGetValue(1, out _), "touched entry should survive eviction");
        Assert.False(cache.TryGetValue(2, out _), "untouched entry should be the one evicted");
        Assert.True(cache.TryGetValue(3, out _));
    }

    [Fact]
    public void Add_ExceedingCapacity_DoesNotDisposeEvictedValue()
    {
        // CoverImageCache hands out the same Bitmap instance it stores here, and callers bind it
        // straight into a long-lived view-model property - eviction can't safely assume nothing
        // else still references it. A real crash (ObjectDisposedException out of
        // Image.MeasureOverride, opening Smart Lists after Library had evicted entries still
        // shown on-screen) is what proved the old dispose-on-evict behavior unsafe.
        var cache = new LruCache<int, TrackedDisposable>(capacity: 1);
        var first = new TrackedDisposable();
        var second = new TrackedDisposable();
        cache.Add(1, first);

        cache.Add(2, second);

        Assert.False(first.IsDisposed, "evicted value may still be referenced elsewhere and must not be force-disposed");
        Assert.False(second.IsDisposed);
    }

    [Fact]
    public void Add_UpdatingExistingKey_DoesNotCountTwiceTowardCapacity()
    {
        var cache = new LruCache<int, string>(capacity: 2);
        cache.Add(1, "a");
        cache.Add(2, "b");

        cache.Add(1, "a-updated"); // same key - replace, not a third entry

        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGetValue(1, out var value) && value == "a-updated");
        Assert.True(cache.TryGetValue(2, out _));
    }
}
