using Avalonia;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Performance;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The memory budget, the byte-bounded cover cache, the pressure trimmer and the memory readout
/// (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md sections 4.1 and 4.2).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class PerformanceMemoryTests
{
    private const long Mb = 1024 * 1024;
    private const long Gb = 1024 * Mb;

    private static Bitmap Tiny() => new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96));

    [Theory]
    [InlineData(2, 150)]    // 5% would be 102 MB: raised to the floor
    [InlineData(4, 204)]
    [InlineData(8, 409)]
    [InlineData(16, 500)]   // 5% would be 819 MB: capped
    [InlineData(32, 500)]
    public void ImageBudget_IsFivePercentOfRam_ClampedBetween150And500Megabytes(int ramGb, long expectedMb)
    {
        Assert.Equal(expectedMb, ImageMemoryBudget.TotalFor(ramGb * Gb) / Mb);
    }

    [Fact]
    public void ImageBudget_OfNoRamReading_FallsBackToTheFloor()
    {
        Assert.Equal(ImageMemoryBudget.MinBytes, ImageMemoryBudget.TotalFor(0));
    }

    [Fact]
    public void ImageBudget_SharesAddUpToTheWhole()
    {
        Assert.Equal(1.0, ImageMemoryBudget.GridShare + ImageMemoryBudget.ComicShare + ImageMemoryBudget.BookShare, 6);
        Assert.InRange(ImageMemoryBudget.TotalBytes, ImageMemoryBudget.MinBytes, ImageMemoryBudget.MaxBytes);
    }

    [Fact]
    public void SystemMemory_ReportsARealMachine()
    {
        Assert.True(SystemMemory.TotalPhysicalBytes > 512 * Mb);
        Assert.InRange(SystemMemory.LoadPercent, 0, 100);
    }

    [Fact]
    public void ByteCache_EvictsLeastRecentlyUsed_WhenOverBudget()
    {
        var cache = new BitmapByteCache<string>(300);
        cache.Add("a", Tiny(), 100);
        cache.Add("b", Tiny(), 100);
        cache.Add("c", Tiny(), 100);
        Assert.True(cache.TryGetValue("a", out _)); // b is now the least recently used

        cache.Add("d", Tiny(), 100);

        Assert.True(cache.TryGetValue("a", out _));
        Assert.False(cache.TryGetValue("b", out _));
        Assert.Equal(3, cache.Count);
        Assert.Equal(300, cache.Bytes);
    }

    [Fact]
    public void ByteCache_NeverDisposesOnEviction_BecauseAViewModelMayStillHoldTheBitmap()
    {
        var cache = new BitmapByteCache<string>(100);
        var evicted = Tiny();
        cache.Add("old", evicted, 100);

        cache.Add("new", Tiny(), 100);

        Assert.False(cache.TryGetValue("old", out _));
        Assert.Equal(new PixelSize(1, 1), evicted.PixelSize);
    }

    [Fact]
    public void ByteCache_KeepsAnEntryLargerThanTheBudget_AsTheNewest()
    {
        var cache = new BitmapByteCache<string>(100);
        cache.Add("small", Tiny(), 60);

        cache.Add("huge", Tiny(), 500);

        Assert.True(cache.TryGetValue("huge", out _));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void ByteCache_ReplacingAKey_FixesTheByteCount()
    {
        var cache = new BitmapByteCache<string>(1_000);
        cache.Add("a", Tiny(), 100);
        var replacement = Tiny();

        cache.Add("a", replacement, 250);

        Assert.True(cache.TryGetValue("a", out var hit));
        Assert.Same(replacement, hit);
        Assert.Equal(250, cache.Bytes);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void ByteCache_RemoveTrimAndClear_KeepTheByteCountRight()
    {
        var cache = new BitmapByteCache<string>(1_000);
        cache.Add("a", Tiny(), 100);
        cache.Add("b", Tiny(), 100);
        cache.Add("c", Tiny(), 100);

        Assert.True(cache.Remove("b"));
        Assert.False(cache.Remove("b"));
        Assert.Equal(200, cache.Bytes);

        cache.Trim(100);
        Assert.Equal(100, cache.Bytes);
        Assert.True(cache.TryGetValue("c", out _));

        cache.Clear();
        Assert.Equal(0, cache.Bytes);
        Assert.Equal(0, cache.Count);
    }

    /// <summary>A trimmer whose clock, memory reading and background runner the test controls.</summary>
    private sealed class Rig
    {
        public TimeSpan Now = TimeSpan.FromMinutes(1);
        public int Load = 50;
        public int Trims;
        public int Collections;
        public long PrivateBytes = 1_000;
        public long PrivateBytesAfterTrim = 1_000;
        public readonly MemoryPressureTrimmer Trimmer;

        public Rig()
        {
            Trimmer = new MemoryPressureTrimmer(
                () => Load,
                () => Now,
                () =>
                {
                    Trims++;
                    PrivateBytes = PrivateBytesAfterTrim;
                },
                () => PrivateBytes,
                () => Collections++,
                work => work());
        }

        public void CheckAfter(TimeSpan elapsed)
        {
            Now += elapsed;
            Trimmer.Check();
        }
    }

    [Fact]
    public void Trimmer_DoesNothing_WhileTheSystemHasMemoryToSpare()
    {
        var rig = new Rig { Load = 60 };

        for (int i = 0; i < 5; i++)
        {
            rig.CheckAfter(MemoryPressureTrimmer.CheckInterval);
        }

        Assert.Equal(0, rig.Trims);
    }

    [Fact]
    public void Trimmer_NeedsTwoHighReadingsInARow_SoOneSpikeDoesNotFlushTheCaches()
    {
        var rig = new Rig { Load = 90 };
        rig.CheckAfter(MemoryPressureTrimmer.CheckInterval);
        Assert.Equal(0, rig.Trims);

        rig.Load = 50;
        rig.CheckAfter(MemoryPressureTrimmer.CheckInterval);
        rig.Load = 90;
        rig.CheckAfter(MemoryPressureTrimmer.CheckInterval);
        Assert.Equal(0, rig.Trims);

        rig.CheckAfter(MemoryPressureTrimmer.CheckInterval);
        Assert.Equal(1, rig.Trims);
        Assert.Equal(1, rig.Trimmer.TrimCount);
    }

    [Fact]
    public void Trimmer_IgnoresChecksInsideTheInterval()
    {
        var rig = new Rig { Load = 95 };
        rig.CheckAfter(MemoryPressureTrimmer.CheckInterval);

        for (int i = 0; i < 20; i++)
        {
            rig.CheckAfter(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(0, rig.Trims);
    }

    [Fact]
    public void Trimmer_DoesNotTrimAgain_UntilTheTrimIntervalHasPassed()
    {
        var rig = new Rig { Load = 95 };
        rig.CheckAfter(MemoryPressureTrimmer.CheckInterval);
        rig.CheckAfter(MemoryPressureTrimmer.CheckInterval);
        Assert.Equal(1, rig.Trims);

        rig.CheckAfter(MemoryPressureTrimmer.CheckInterval);
        rig.CheckAfter(MemoryPressureTrimmer.CheckInterval);
        Assert.Equal(1, rig.Trims);

        rig.CheckAfter(MemoryPressureTrimmer.TrimInterval);
        rig.CheckAfter(MemoryPressureTrimmer.CheckInterval);
        Assert.Equal(2, rig.Trims);
    }

    [Fact]
    public void Trimmer_RequestsACollection_OnlyWhenTheTrimFreedNothing_AndAtMostOncePerInterval()
    {
        var freed = new Rig { Load = 95, PrivateBytesAfterTrim = 500 };
        freed.CheckAfter(MemoryPressureTrimmer.CheckInterval);
        freed.CheckAfter(MemoryPressureTrimmer.CheckInterval);
        Assert.Equal(1, freed.Trims);
        Assert.Equal(0, freed.Collections);

        var stuck = new Rig { Load = 95 };
        stuck.CheckAfter(MemoryPressureTrimmer.CheckInterval);
        stuck.CheckAfter(MemoryPressureTrimmer.CheckInterval);
        Assert.Equal(1, stuck.Collections);

        stuck.CheckAfter(MemoryPressureTrimmer.TrimInterval);
        stuck.CheckAfter(MemoryPressureTrimmer.CheckInterval);
        Assert.Equal(2, stuck.Trims);
        Assert.Equal(1, stuck.Collections); // still inside the two-minute collection interval
    }

    [Fact]
    public void Snapshot_ReadsThreeSeparateMemoryFigures_AndDescribesThem()
    {
        var snapshot = PerformanceSnapshot.Capture();

        Assert.True(snapshot.ManagedBytes > 0);
        Assert.True(snapshot.PrivateBytes > 0);
        Assert.True(snapshot.WorkingSetBytes > 0);
        Assert.Equal(Math.Max(0, snapshot.PrivateBytes - snapshot.ManagedBytes), snapshot.NativeEstimateBytes);
        Assert.True(snapshot.Threads > 0);

        string text = snapshot.Describe();
        foreach (string label in new[] { "Private", "Managed heap", "Native (est.)", "System memory", "Cover caches", "Threads", "Heavy jobs" })
        {
            Assert.Contains(label, text);
        }
    }
}
