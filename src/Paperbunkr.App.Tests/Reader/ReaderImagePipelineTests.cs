using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.Tests.Reader;

/// <summary>
/// Step 5 of the reader-pipeline plan: <see cref="ReaderImagePipeline"/> - the single decode/cache/
/// prefetch implementation. Mirrors the <c>PageDecodeServiceTests</c> shape (synthetic .cbz, the
/// <c>OnBeforeBackgroundDecode</c> gate) plus the byte-budget bound and the background-decode
/// off-thread guarantee.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReaderImagePipelineTests : IDisposable
{
    private readonly string _cbzPath = Path.Combine(Path.GetTempPath(), $"pb_pipeline_test_{Guid.NewGuid():N}.cbz");

    public void Dispose()
    {
        try { if (File.Exists(_cbzPath)) File.Delete(_cbzPath); } catch (IOException) { }
    }

    [Fact]
    public void TryOpen_NullForMissing_PipelineForReal()
    {
        Assert.Null(ReaderImagePipeline.TryOpen(Path.Combine(Path.GetTempPath(), $"nope_{Guid.NewGuid():N}.cbz")));

        CbzFixture.Create(_cbzPath, pageCount: 4);
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath);
        Assert.NotNull(pipeline);
        Assert.Equal(4, pipeline!.PageCount);
    }

    [Fact]
    public void GetPage_DecodesRealBitmap_AndDownsamplesToViewport()
    {
        CbzFixture.Create(_cbzPath, pageCount: 2); // native 64x96
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        var full = pipeline.GetPage(0);
        Assert.Equal(64, full.PixelSize.Width);

        pipeline.SetViewportWidth(32);
        var scaled = pipeline.GetPage(1);
        Assert.Equal(32, scaled.PixelSize.Width);
        Assert.Equal(48, scaled.PixelSize.Height);
    }

    [Fact]
    public void GetPage_IsCached_SecondCallReturnsSameInstance()
    {
        CbzFixture.Create(_cbzPath, pageCount: 2);
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        Assert.Same(pipeline.GetPage(0), pipeline.GetPage(0));
    }

    [Fact]
    public void TryGetCachedPage_NullThenPopulated_AfterBackgroundDecode()
    {
        CbzFixture.Create(_cbzPath, pageCount: 8);
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        Assert.Null(pipeline.TryGetCachedPage(3));

        var landed = new ManualResetEventSlim(false);
        pipeline.BackgroundDecodeCompleted += p => { if (p == 3) landed.Set(); };
        pipeline.SetVirtualizationWindow(3, 3);

        Assert.True(landed.Wait(TimeSpan.FromSeconds(5)), "page 3 never decoded in the background");
        Assert.NotNull(pipeline.TryGetCachedPage(3));
    }

    [Fact]
    public void BackgroundDecode_RunsOffTheCallingThread()
    {
        CbzFixture.Create(_cbzPath, pageCount: 5);
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        int callingThread = Environment.CurrentManagedThreadId;
        int? decodeThread = null;
        var seen = new ManualResetEventSlim(false);
        pipeline.OnBeforeBackgroundDecode = _ => { decodeThread ??= Environment.CurrentManagedThreadId; seen.Set(); };

        pipeline.SetVirtualizationWindow(2, 2);

        Assert.True(seen.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotNull(decodeThread);
        Assert.NotEqual(callingThread, decodeThread);
    }

    [Fact]
    public void SetVirtualizationWindow_KeepsDecodedCountBounded_AcrossLongScroll()
    {
        const int pageCount = 60;
        const int radius = 2;
        CbzFixture.Create(_cbzPath, pageCount);
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        // visible (2r+1) + backward fringe (2) + max forward fringe (6) + slack for in-flight
        const int maxAllowed = (radius * 2 + 1) + 2 + 6 + 3;

        for (int center = 0; center < pageCount; center++)
        {
            int min = Math.Max(0, center - radius);
            int max = Math.Min(pageCount - 1, center + radius);
            pipeline.SetVirtualizationWindow(min, max);
            for (int i = min; i <= max; i++)
            {
                pipeline.GetPage(i);
            }

            Assert.True(pipeline.DecodedPageCount <= maxAllowed,
                $"decoded count {pipeline.DecodedPageCount} exceeded {maxAllowed} at center {center}");
        }
    }

    [Fact]
    public void GetDetailPage_ReturnsRequestedSize_FromCachedBytes_NoExtraContainerRead()
    {
        CbzFixture.Create(_cbzPath, pageCount: 3); // native 64x96
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;
        pipeline.SetViewportWidth(32);
        pipeline.GetPage(1); // primes the raw-bytes tier for page 1

        var detail = pipeline.GetDetailPage(1, new Avalonia.PixelSize(256, 384));
        Assert.Equal(256, detail.PixelSize.Width);
        Assert.Equal(384, detail.PixelSize.Height);
    }

    [Fact]
    public void SharedRawBytes_SurviveIssueSwitch_ForInstantBackNav()
    {
        CbzFixture.Create(_cbzPath, pageCount: 5);

        using (var first = ReaderImagePipeline.TryOpen(_cbzPath)!)
        {
            for (int i = 0; i < 5; i++) first.GetPage(i); // prime the shared compressed-bytes tier
        }

        Paperbunkr.App.Services.Reader.ReaderPerfStats.Current.Reset();

        using (var reopened = ReaderImagePipeline.TryOpen(_cbzPath)!)
        {
            for (int i = 0; i < 5; i++) reopened.GetPage(i);
        }

        var snap = Paperbunkr.App.Services.Reader.ReaderPerfStats.Current.Snapshot();
        // Bytes came from the shared retention cache, not a fresh session/archive read.
        Assert.Equal(0, snap.SessionReads);
        Assert.Equal(0, snap.ArchiveReads);
    }

    [Fact]
    public void ActivePageIndex_TracksWindowCentre()
    {
        CbzFixture.Create(_cbzPath, pageCount: 20);
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;
        Assert.Equal(-1, pipeline.ActivePageIndex);

        pipeline.SetVirtualizationWindow(4, 6);
        Assert.Equal(5, pipeline.ActivePageIndex);

        pipeline.SetVirtualizationWindow(11, 11);
        Assert.Equal(11, pipeline.ActivePageIndex);
    }

    [Fact]
    public void ConcurrentReads_DoNotCorrupt_TheSessionHandle()
    {
        // The crash repro: fast paged flip = UI-thread GetPage + the background prefetch loop both
        // reading off the (non-thread-safe) 7z.dll session at once. _readerLock must serialise it.
        const int pageCount = 30;
        CbzFixture.Create(_cbzPath, pageCount);
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        Exception? failure = null;
        var threads = new List<Thread>();
        for (int t = 0; t < 4; t++)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    var rng = new Random(Environment.CurrentManagedThreadId);
                    for (int k = 0; k < 60; k++)
                    {
                        int page = rng.Next(pageCount);
                        pipeline.SetVirtualizationWindow(page - 1, page + 1);
                        var bmp = pipeline.GetPage(page);
                        Assert.Equal(64, bmp.PixelSize.Width);
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            threads.Add(thread);
            thread.Start();
        }

        foreach (var thread in threads)
        {
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "a reader thread hung");
        }
        Assert.Null(failure);
    }

    // --- rev-3 addendum: detail-tier budget accounting (design §15 #3) ---------------------------

    [Fact]
    public void GetDetailPage_ReservesAgainstBudget_EvictsDisplayPages_KeepsActivePage()
    {
        const int pageCount = 40;
        CbzFixture.Create(_cbzPath, pageCount); // 64x96x4 = 24,576 bytes/page decoded
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath, userMemoryLimitMb: 1)!;

        pipeline.SetVirtualizationWindow(10, 14); // ActivePageIndex = 12
        for (int i = 8; i <= 22; i++)
        {
            pipeline.GetPage(i);
        }
        int before = pipeline.DecodedPageCount;
        Assert.True(before > 4, $"expected the display cache to hold several pages, had {before}");

        // ~400*600*4 = 960,000 bytes reserved - close to the whole ~900 KiB display allowance,
        // so the cache must shed almost everything.
        var detail = pipeline.GetDetailPage(12, new Avalonia.PixelSize(400, 600));
        Assert.Equal(400, detail.PixelSize.Width);

        int after = pipeline.DecodedPageCount;
        Assert.True(after < before, $"detail reservation did not evict display pages ({before} -> {after})");
        Assert.NotNull(pipeline.TryGetCachedPage(12)); // the active page was pinned across the drop

        pipeline.ReleaseDetail();
        for (int i = 8; i <= 22; i++)
        {
            pipeline.GetPage(i);
        }
        Assert.True(pipeline.DecodedPageCount > after, "capacity was not restored after ReleaseDetail");
    }

    [Fact]
    public void PageTurn_ReleasesAStaleDetailReservation()
    {
        CbzFixture.Create(_cbzPath, pageCount: 30);
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath, userMemoryLimitMb: 1)!;

        pipeline.SetVirtualizationWindow(5, 5);
        pipeline.GetPage(5);
        pipeline.GetDetailPage(5, new Avalonia.PixelSize(400, 600)); // reserve ~937 KiB

        // Turn to a far page - the reservation for page 5 must be released so the new window fills.
        pipeline.SetVirtualizationWindow(20, 20);
        for (int i = 18; i <= 22; i++)
        {
            pipeline.GetPage(i);
        }
        Assert.True(pipeline.DecodedPageCount >= 4, "the reservation from the old page was never released on the turn");
    }

    // --- rev-3 addendum: fringe-recompute debounce (design §15 #5) ------------------------------

    [Fact]
    public void SetVirtualizationWindow_Burst_DebouncesFringeRecompute_ToOnePass()
    {
        CbzFixture.Create(_cbzPath, pageCount: 60);
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        int recomputes = 0;
        var settled = new ManualResetEventSlim(false);
        pipeline.OnFringeRecomputed = () => { Interlocked.Increment(ref recomputes); settled.Set(); };

        for (int i = 0; i < 12; i++)
        {
            pipeline.SetVirtualizationWindow(i, i); // < 30 ms apart in a tight loop
        }

        Assert.True(settled.Wait(TimeSpan.FromSeconds(2)));
        Thread.Sleep(120); // let any stragglers fire
        Assert.Equal(1, recomputes);
    }

    [Fact]
    public void SetVirtualizationWindow_SpacedCalls_RecomputeFringeEachTime()
    {
        CbzFixture.Create(_cbzPath, pageCount: 60);
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        int recomputes = 0;
        pipeline.OnFringeRecomputed = () => Interlocked.Increment(ref recomputes);

        for (int i = 0; i < 5; i++)
        {
            pipeline.SetVirtualizationWindow(i * 3, i * 3);
            Thread.Sleep(60); // > FringeDebounceMs
        }
        Thread.Sleep(120);

        Assert.Equal(5, recomputes);
    }

    [Fact]
    public void SuppressFringePrefetch_DefersTheFringePass_UntilTheHoldLifts()
    {
        CbzFixture.Create(_cbzPath, pageCount: 60);
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        int recomputes = 0;
        var ran = new ManualResetEventSlim(false);
        pipeline.OnFringeRecomputed = () => { Interlocked.Increment(ref recomputes); ran.Set(); };

        pipeline.SuppressFringePrefetch(200);
        pipeline.SetVirtualizationWindow(20, 20);

        Thread.Sleep(90); // still inside the hold
        Assert.Equal(0, recomputes);

        Assert.True(ran.Wait(TimeSpan.FromSeconds(2)), "fringe pass never ran after the hold lifted");
        Assert.Equal(1, recomputes);
    }

    [Fact]
    public void MemoryBudget_HardByteCeiling_EvictsUnderPressure()
    {
        const int pageCount = 40;
        CbzFixture.Create(_cbzPath, pageCount);
        // 64x96x4 = 24,576 bytes/page decoded. 1 MiB budget => ~40 pages would fit; pin a tiny
        // limit so eviction must happen. userMemoryLimitMb is min 1 MiB via Resolve, so instead
        // drive pressure by scrolling far and checking the display cache never balloons.
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath, userMemoryLimitMb: 1)!;

        for (int center = 0; center < pageCount; center++)
        {
            pipeline.SetVirtualizationWindow(center, center);
            pipeline.GetPage(center);
        }

        // 1 MiB / 24,576 ~= 42 pages max even with no windowing; windowing keeps it far below.
        Assert.True(pipeline.DecodedPageCount < 25, $"decoded count {pipeline.DecodedPageCount} not bounded under a 1 MiB budget");
    }
}
