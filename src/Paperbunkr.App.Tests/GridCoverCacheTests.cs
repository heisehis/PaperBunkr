using Avalonia;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="GridCoverCache"/>: byte-budget LRU of display-size cover bitmaps (docs/superpowers/specs/
/// 2026-09-19-library-scroll-smoothness-design.md section 3.1). Bitmaps are 1x1 stand-ins; sizes are passed explicitly.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class GridCoverCacheTests
{
    private static Bitmap Tiny() => new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96));

    [Theory]
    [InlineData(150, 1.0, 192)]   // 150 px -> 3 * 64
    [InlineData(150, 1.5, 256)]   // 225 px -> 4 * 64
    [InlineData(150, 2.0, 320)]   // 300 px -> 5 * 64
    [InlineData(48, 1.0, 96)]     // tiny thumb -> floor
    [InlineData(400, 2.0, 320)]   // huge -> ceiling (source thumbs are 400 px on the long edge, about 267 wide)
    [InlineData(64, 1.0, 96)]
    [InlineData(193, 1.0, 256)]   // just over a step rounds up, never down (no upscaling from the bucket)
    public void BucketFor_RoundsUpToAStep_WithinTheClamp(double dip, double scaling, int expected)
    {
        Assert.Equal(expected, GridCoverCache.BucketFor(dip, scaling));
    }

    [Fact]
    public void BucketFor_IsStableWithinAStep_SoTheDensitySliderDoesNotRedecodeEveryTick()
    {
        Assert.Equal(GridCoverCache.BucketFor(130, 1.0), GridCoverCache.BucketFor(190, 1.0));
        Assert.NotEqual(GridCoverCache.BucketFor(190, 1.0), GridCoverCache.BucketFor(200, 1.0));
    }

    [Fact]
    public void AddThenTryGet_Hits_AndDifferentBucketsAreDifferentEntries()
    {
        var cache = new GridCoverCache(1_000);
        var a192 = Tiny();

        cache.Add("1", 192, a192, 100);

        Assert.True(cache.TryGet("1", 192, out var hit));
        Assert.Same(a192, hit);
        Assert.False(cache.TryGet("1", 256, out _));
        Assert.False(cache.TryGet("2", 192, out _));
    }

    [Fact]
    public void Add_OfAnExistingKey_KeepsTheFirstBitmap()
    {
        var cache = new GridCoverCache(1_000);
        var first = Tiny();

        cache.Add("1", 192, first, 100);
        var returned = cache.Add("1", 192, Tiny(), 100);

        Assert.Same(first, returned);
        Assert.Equal(1, cache.Count);
        Assert.Equal(100, cache.Bytes);
    }

    [Fact]
    public void ExceedingTheByteBudget_EvictsTheLeastRecentlyUsedFirst()
    {
        var cache = new GridCoverCache(300);
        cache.Add("a", 96, Tiny(), 100);
        cache.Add("b", 96, Tiny(), 100);
        cache.Add("c", 96, Tiny(), 100);

        Assert.True(cache.TryGet("a", 96, out _)); // touch a: b is now the least recently used

        cache.Add("d", 96, Tiny(), 100);

        Assert.True(cache.TryGet("a", 96, out _));
        Assert.False(cache.TryGet("b", 96, out _));
        Assert.True(cache.TryGet("c", 96, out _));
        Assert.True(cache.TryGet("d", 96, out _));
        Assert.Equal(300, cache.Bytes);
    }

    [Fact]
    public void AnEntryLargerThanTheWholeBudget_IsStillKept_AsTheNewest()
    {
        var cache = new GridCoverCache(100);
        cache.Add("small", 96, Tiny(), 60);

        cache.Add("huge", 96, Tiny(), 500);

        Assert.True(cache.TryGet("huge", 96, out _));
        Assert.False(cache.TryGet("small", 96, out _));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Eviction_NeverDisposesTheBitmap_BecauseARealizedImageMayStillShowIt()
    {
        var cache = new GridCoverCache(100);
        var evicted = Tiny();
        cache.Add("old", 96, evicted, 100);

        cache.Add("new", 96, Tiny(), 100);

        Assert.False(cache.TryGet("old", 96, out _));
        // Still fully usable: touching a disposed bitmap would throw ObjectDisposedException.
        Assert.Equal(new PixelSize(1, 1), evicted.PixelSize);
    }

    [Fact]
    public void Remove_DropsEveryBucketOfOneCover_AndFixesTheByteCount()
    {
        var cache = new GridCoverCache(10_000);
        cache.Add("7", 96, Tiny(), 100);
        cache.Add("7", 192, Tiny(), 200);
        cache.Add("8", 192, Tiny(), 300);

        cache.Remove("7");

        Assert.False(cache.TryGet("7", 96, out _));
        Assert.False(cache.TryGet("7", 192, out _));
        Assert.True(cache.TryGet("8", 192, out _));
        Assert.Equal(300, cache.Bytes);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Clear_EmptiesEverything()
    {
        var cache = new GridCoverCache(10_000);
        cache.Add("1", 96, Tiny(), 100);

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
    }

    [Fact]
    public void EstimateBytes_IsWidthTimesHeightTimesFour()
    {
        using var bitmap = new WriteableBitmap(new PixelSize(10, 20), new Vector(96, 96));

        Assert.Equal(800, GridCoverCache.EstimateBytes(bitmap));
    }

    [Fact]
    public void TheSharedBudget_IsThreeHundredMegabytes()
    {
        Assert.Equal(300L * 1024 * 1024, GridCoverCache.BudgetBytes);
    }
}
