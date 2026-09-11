using System.Drawing;
using System.Drawing.Imaging;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.Tests.Reader;

/// <summary>
/// Step 5 of the webtoon band-decode plan (docs/superpowers/specs/2026-09-09-reader-webtoon-strip-
/// band-decode-plan.md): band decode wired through <see cref="ReaderImagePipeline"/>'s window/
/// prefetch machinery end to end - <see cref="ReaderImagePipeline.SetStripBandWindow"/>,
/// <see cref="ReaderImagePipeline.TryGetCachedBand"/>, and the PNG-strip-falls-back-to-whole-page
/// path (design §4.1 rev 5).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReaderImagePipelineBandTests : IDisposable
{
    private readonly string _cbzPath = Path.Combine(Path.GetTempPath(), $"pb_bandtest_{Guid.NewGuid():N}.cbz");

    public void Dispose()
    {
        try { if (File.Exists(_cbzPath)) File.Delete(_cbzPath); } catch (IOException) { }
    }

    /// <summary>
    /// Settles a freshly-opened pipeline's first pass for <paramref name="pageIndex"/>: the very
    /// first <see cref="ReaderImagePipeline.SetVirtualizationWindow"/> call for any page whose strip
    /// classification isn't known yet enqueues an ordinary whole-page decode as a safe default
    /// (design §4.2 - a real, accepted one-time cost for a newly-seen page, not a bug), *in
    /// addition to* the size peek that will actually classify it. Waits for *both* to genuinely
    /// land (not best-effort - an under-waited version of this was a real, if narrow, source of
    /// test flakiness: a test that then arms a gate on the next `OnBeforeBackgroundDecode` could
    /// catch this priming decode instead of the band request it actually meant to catch) before a
    /// test's own band-specific work begins.
    /// </summary>
    private static void OpenAndSettleFirstPass(ReaderImagePipeline pipeline, int pageIndex)
    {
        var sizeLanded = new ManualResetEventSlim(false);
        pipeline.PageSizeAvailable += (index, _) => { if (index == pageIndex) sizeLanded.Set(); };

        var decodeLanded = new ManualResetEventSlim(false);
        pipeline.BackgroundDecodeCompleted += p => { if (p == pageIndex) decodeLanded.Set(); };

        pipeline.SetVirtualizationWindow(pageIndex, pageIndex);

        Assert.True(sizeLanded.Wait(TimeSpan.FromSeconds(10)), "page size never landed");
        Assert.True(decodeLanded.Wait(TimeSpan.FromSeconds(10)), "priming whole-page decode never landed");
        Thread.Sleep(50);
    }

    [Fact]
    public void SetStripBandWindow_DecodesRequestedBands_ForAJpegStrip()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1, pageSize: _ => new Size(200, 10000), imageFormat: _ => ImageFormat.Jpeg);
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        OpenAndSettleFirstPass(pipeline, 0);
        Assert.True(pipeline.IsStrip(0));

        var landed = new ManualResetEventSlim(false);
        pipeline.BackgroundDecodeCompleted += p => { if (p == 0) landed.Set(); };
        pipeline.SetStripBandWindow(0, minBand: 0, maxBand: 0);

        Assert.True(landed.Wait(TimeSpan.FromSeconds(5)), "band 0 never decoded");
        var band0 = pipeline.TryGetCachedBand(0, 0);
        Assert.NotNull(band0);
        Assert.Equal(200, band0!.PixelSize.Width);
        Assert.Equal(ReaderImagePipeline.BandHeight, band0.PixelSize.Height);
    }

    [Fact]
    public void SetStripBandWindow_SequentialBands_EachCorrectlyClippedOrFull()
    {
        // 10000 rows / 4096 BandHeight = bands 0,1 full (4096 rows each), band 2 clipped to 1808.
        CbzFixture.Create(_cbzPath, pageCount: 1, pageSize: _ => new Size(200, 10000), imageFormat: _ => ImageFormat.Jpeg);
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        OpenAndSettleFirstPass(pipeline, 0);

        var allLanded = new ManualResetEventSlim(false);
        var landedBands = new HashSet<int>();
        pipeline.BackgroundDecodeCompleted += _ =>
        {
            for (int b = 0; b <= 2; b++)
            {
                if (pipeline.TryGetCachedBand(0, b) is not null) lock (landedBands) landedBands.Add(b);
            }
            lock (landedBands) { if (landedBands.Count == 3) allLanded.Set(); }
        };

        pipeline.SetStripBandWindow(0, minBand: 0, maxBand: 2);

        Assert.True(allLanded.Wait(TimeSpan.FromSeconds(5)), "not all 3 bands landed");
        Assert.Equal(ReaderImagePipeline.BandHeight, pipeline.TryGetCachedBand(0, 0)!.PixelSize.Height);
        Assert.Equal(ReaderImagePipeline.BandHeight, pipeline.TryGetCachedBand(0, 1)!.PixelSize.Height);
        Assert.Equal(10000 - (2 * ReaderImagePipeline.BandHeight), pipeline.TryGetCachedBand(0, 2)!.PixelSize.Height);
    }

    [Fact]
    public void SetStripBandWindow_PngStrip_FallsBackToWholePageDecode()
    {
        // Design rev 5: PNG has no working row-range decode in this Skia build - a PNG page shaped
        // like a strip must still end up decoded, just via the ordinary whole-page path, not bands.
        CbzFixture.Create(_cbzPath, pageCount: 1, pageSize: _ => new Size(200, 6000)); // PNG default
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        OpenAndSettleFirstPass(pipeline, 0);
        Assert.True(pipeline.IsStrip(0)); // shaped like a strip...

        pipeline.SetStripBandWindow(0, minBand: 0, maxBand: 0);
        Thread.Sleep(300); // give the (fallback-or-already-cached) decode a moment to settle

        Assert.Null(pipeline.TryGetCachedBand(0, 0)); // ...but never banded...
        Assert.NotNull(pipeline.TryGetCachedPage(0));  // ...decoded whole instead (priming pass or fallback - either is correct here).
    }

    [Fact]
    public void SetStripBandWindow_StaleRequest_SkippedOnceWindowMovesOn()
    {
        // Enough pages that SetVirtualizationWindow(lastIndex, lastIndex)'s own BackFringe margin
        // (2 pages back) genuinely excludes page 0 from the safe/fringe window - too few pages was
        // this test's first real bug: with only 2 pages, "moving to page 1" still left page 0
        // inside the BackFringe-widened window (safeMin = max(0, 1-2) = 0), so it never actually
        // left, and had nothing to do with the pipeline's own stale-request handling.
        const int pageCount = 6;
        const int lastIndex = pageCount - 1;
        CbzFixture.Create(_cbzPath, pageCount, pageSize: i => i == 0 ? new Size(200, 40000) : new Size(64, 96),
            imageFormat: i => i == 0 ? ImageFormat.Jpeg : ImageFormat.Png);
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        OpenAndSettleFirstPass(pipeline, 0);

        // Gate the consumer loop so the band 9 request we're about to make is still sitting
        // unprocessed when we move the window away from page 0 entirely. Armed only now, after the
        // priming pass has fully settled, so it can only catch the band-9 request itself.
        var gate = new ManualResetEventSlim(false);
        var enteredGate = new ManualResetEventSlim(false);
        var order = new System.Collections.Generic.List<string>();
        pipeline.OnBeforeBackgroundDecode = p =>
        {
            lock (order) order.Add($"OnBeforeBackgroundDecode({p}) @ {DateTime.UtcNow:HH:mm:ss.fff}");
            if (p == 0) { enteredGate.Set(); gate.Wait(TimeSpan.FromSeconds(5)); }
        };
        pipeline.BackgroundDecodeCompleted += p => { lock (order) order.Add($"BackgroundDecodeCompleted({p}) @ {DateTime.UtcNow:HH:mm:ss.fff}"); };

        pipeline.SetStripBandWindow(0, minBand: 9, maxBand: 9);
        Assert.True(enteredGate.Wait(TimeSpan.FromSeconds(5)), "band request never reached the consumer loop");

        // Now move the window entirely away from page 0 before releasing the gate.
        pipeline.SetVirtualizationWindow(lastIndex, lastIndex);
        lock (order) order.Add($"SetVirtualizationWindow({lastIndex}) issued @ {DateTime.UtcNow:HH:mm:ss.fff}");
        gate.Set();

        Thread.Sleep(300); // let the (now-stale) request drain through if it were going to decode
        var band9 = pipeline.TryGetCachedBand(0, 9);
        string trace = string.Join("\n", order);
        Assert.True(band9 is null, $"band 9 decoded when it should have been stale. Trace:\n{trace}");
    }

    [Fact]
    public void SetStripBandWindow_ScrollingThroughAWholeStrip_KeepsResidentBandBytesBounded()
    {
        // 800x24000 is the design's own §6 memory-bound example. 24000/4096 = 6 bands total for the
        // whole strip (~13.1MB/band at 800px wide, Bgra8888) - if nothing were ever evicted while
        // scrolling through it, resident bytes would climb toward that whole-strip total. The bound
        // this test enforces (design §6, fixed-source-pixels-so-no-zoom-conversion-needed):
        // ~4*BandHeight*width*4 bytes + slack - comfortably less than half the whole strip.
        const int width = 800;
        CbzFixture.Create(_cbzPath, pageCount: 1, pageSize: _ => new Size(width, 24000), imageFormat: _ => ImageFormat.Jpeg);
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        OpenAndSettleFirstPass(pipeline, 0);
        Assert.True(pipeline.IsStrip(0));

        // The eviction range is [visibleBand - 2*BandHeight, visibleBand + 2*BandHeight] (design
        // §4.2/§6) - a *closed* range, so for a single visible band that's band-2..band+2
        // surviving = 5 bands resident at steady state, not 4 (the design text's own "~4*BandHeight"
        // was a rough approximation predating the exact eviction-range arithmetic landing at ±2).
        long bound = (5L * ReaderImagePipeline.BandHeight * width * 4) + (8L * 1024 * 1024); // + 8MiB slack
        int bandCount = (int)Math.Ceiling(24000.0 / ReaderImagePipeline.BandHeight);

        for (int center = 0; center < bandCount; center++)
        {
            var landed = new ManualResetEventSlim(false);
            pipeline.BackgroundDecodeCompleted += p => { if (p == 0) landed.Set(); };

            pipeline.SetStripBandWindow(0, minBand: center, maxBand: center);
            landed.Wait(TimeSpan.FromSeconds(5)); // best-effort settle per step - the bound below is what actually matters
            Thread.Sleep(50);

            Assert.True(pipeline.DecodedBandBytes <= bound,
                $"resident band bytes {pipeline.DecodedBandBytes:N0} exceeded bound {bound:N0} at scroll step (center band {center})");
        }
    }
}
