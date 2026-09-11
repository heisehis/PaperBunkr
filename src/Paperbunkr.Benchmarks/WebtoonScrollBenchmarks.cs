using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using BenchmarkDotNet.Attributes;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.Benchmarks;

/// <summary>
/// Band decode vs. whole-strip decode (docs/superpowers/specs/2026-09-09-reader-webtoon-strip-
/// band-decode-design.md §6/§8). Run: <c>dotnet run -c Release --project src/Paperbunkr.Benchmarks
/// -- --filter *WebtoonScrollBenchmarks*</c>. <see cref="ForwardSequentialScroll"/> vs.
/// <see cref="ReverseJumpRestart"/> sanity-checks the design's own "a session restart is bounded,
/// same order of magnitude as the strip's very first band, not pathological" claim (§4.1) with real
/// numbers rather than just the argument for it.
/// </summary>
[MemoryDiagnoser]
public class WebtoonScrollBenchmarks
{
    private string _webtoonCbz = "";

    [GlobalSetup]
    public void Setup()
    {
        // Real Skia platform - Bitmap decode needs IPlatformRenderInterface, same bootstrap
        // ReaderPipelineBenchmarks/the test assembly use.
        AppBuilder.Configure<Application>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var dir = Path.Combine(Path.GetTempPath(), "paperbunkr_bench");
        _webtoonCbz = SyntheticArchive.CreateCbz(dir, SyntheticArchive.Profile.WebtoonStrip, pageCount: 1);
    }

    private static void SettleClassification(ReaderImagePipeline pipeline)
    {
        // A fresh pipeline hasn't classified page 0 as a strip yet - one synchronous round trip so
        // every benchmark iteration measures the actual band-decode path, not one page's worth of
        // classification overhead mixed in.
        var landed = new ManualResetEventSlim(false);
        pipeline.PageSizeAvailable += (i, _) => { if (i == 0) landed.Set(); };
        pipeline.SetVirtualizationWindow(0, 0);
        landed.Wait(TimeSpan.FromSeconds(5));
        Thread.Sleep(50);
    }

    /// <summary>Baseline this whole feature exists to improve on - <c>ReaderPipelineBenchmarks.WebtoonStripDecode</c> measures the same whole-page path at a smaller (800x12000) profile; kept here too so both numbers are visible in one report without cross-referencing two classes.</summary>
    [Benchmark(Description = "Whole-strip decode (800x12000, unbanded baseline)", Baseline = true)]
    public int WholeStripDecode()
    {
        using var pipeline = ReaderImagePipeline.TryOpen(_webtoonCbz)!;
        pipeline.SetViewportWidth(800);
        return pipeline.GetPage(0).PixelSize.Height;
    }

    /// <summary>Forward-sequential band decode through the whole strip - the common case (scrolling down), and the one this feature is designed around: each SetStripBandWindow call should be cheap (SkipScanlines from wherever the session already is, not a restart).</summary>
    [Benchmark(Description = "Banded scroll, forward-sequential through whole strip")]
    public long ForwardSequentialScroll()
    {
        using var pipeline = ReaderImagePipeline.TryOpen(_webtoonCbz)!;
        pipeline.SetViewportWidth(800);
        SettleClassification(pipeline);

        int bandCount = (int)Math.Ceiling(12000.0 / ReaderImagePipeline.BandHeight);
        long bytes = 0;
        for (int b = 0; b < bandCount; b++)
        {
            var landed = new ManualResetEventSlim(false);
            pipeline.BackgroundDecodeCompleted += p => { if (p == 0) landed.Set(); };
            pipeline.SetStripBandWindow(0, b, b);
            landed.Wait(TimeSpan.FromSeconds(5));
            bytes += pipeline.DecodedBandBytes;
        }
        return bytes;
    }

    /// <summary>The expensive path (design §4.1's "backward or far-forward - bounded, non-pathological cost, not the cheap path") - jump to the last band first (forces a fresh session skipping the whole strip), then back to band 0 (forces another fresh session). Compare this benchmark's per-op time against ForwardSequentialScroll's to see the actual restart cost, not just the argument for why it's acceptable.</summary>
    [Benchmark(Description = "Banded scroll, reverse-jump session restart (last band, then band 0)")]
    public long ReverseJumpRestart()
    {
        using var pipeline = ReaderImagePipeline.TryOpen(_webtoonCbz)!;
        pipeline.SetViewportWidth(800);
        SettleClassification(pipeline);

        int lastBand = (int)Math.Ceiling(12000.0 / ReaderImagePipeline.BandHeight) - 1;

        var firstLanded = new ManualResetEventSlim(false);
        pipeline.BackgroundDecodeCompleted += p => { if (p == 0) firstLanded.Set(); };
        pipeline.SetStripBandWindow(0, lastBand, lastBand);
        firstLanded.Wait(TimeSpan.FromSeconds(5));
        long bytes = pipeline.DecodedBandBytes;

        var secondLanded = new ManualResetEventSlim(false);
        pipeline.BackgroundDecodeCompleted += p => { if (p == 0) secondLanded.Set(); };
        pipeline.SetStripBandWindow(0, 0, 0);
        secondLanded.Wait(TimeSpan.FromSeconds(5));
        bytes += pipeline.DecodedBandBytes;

        return bytes;
    }
}
