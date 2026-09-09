using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using BenchmarkDotNet.Attributes;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.Benchmarks;

/// <summary>
/// Baseline measurements for the reader pipeline (docs/superpowers/specs/2026-09-08-reader-decode-
/// cache-prefetch-pipeline-design.md §12.1). Run: <c>dotnet run -c Release --project
/// src/Paperbunkr.Benchmarks -- --filter *ReaderPipelineBenchmarks*</c>. Record the first run's
/// numbers as the committed baseline any later change is compared against.
/// </summary>
[MemoryDiagnoser]
public class ReaderPipelineBenchmarks
{
    private string _mangaCbz = "";
    private string _webtoonCbz = "";

    [GlobalSetup]
    public void Setup()
    {
        // Real Skia platform - Bitmap decode needs IPlatformRenderInterface, same bootstrap the
        // test assembly uses.
        AppBuilder.Configure<Application>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var dir = Path.Combine(Path.GetTempPath(), "paperbunkr_bench");
        _mangaCbz = SyntheticArchive.CreateCbz(dir, SyntheticArchive.Profile.Manga, pageCount: 180);
        _webtoonCbz = SyntheticArchive.CreateCbz(dir, SyntheticArchive.Profile.WebtoonStrip, pageCount: 20);
    }

    [Benchmark(Description = "Cold open -> first page decoded (manga, 180p)")]
    public int ColdOpenFirstPage()
    {
        using var pipeline = ReaderImagePipeline.TryOpen(_mangaCbz)!;
        pipeline.SetViewportWidth(1200);
        return pipeline.GetPage(0).PixelSize.Width;
    }

    [Benchmark(Description = "Sequential flip x40 (manga, warm session)")]
    [Arguments(40)]
    public long SequentialFlip(int count)
    {
        using var pipeline = ReaderImagePipeline.TryOpen(_mangaCbz)!;
        pipeline.SetViewportWidth(1200);
        long acc = 0;
        for (int i = 0; i < count; i++)
        {
            pipeline.SetVirtualizationWindow(i - 1, i + 1);
            acc += pipeline.GetPage(i).PixelSize.Height;
        }
        return acc;
    }

    [Benchmark(Description = "Random seek x40 (manga)")]
    [Arguments(40)]
    public long RandomSeek(int count)
    {
        using var pipeline = ReaderImagePipeline.TryOpen(_mangaCbz)!;
        pipeline.SetViewportWidth(1200);
        var rng = new Random(99);
        long acc = 0;
        for (int i = 0; i < count; i++)
        {
            int page = rng.Next(0, pipeline.PageCount);
            pipeline.SetVirtualizationWindow(page - 1, page + 1);
            acc += pipeline.GetPage(page).PixelSize.Height;
        }
        return acc;
    }

    [Benchmark(Description = "Webtoon strip decode x10 (800x12000)")]
    public long WebtoonStripDecode()
    {
        using var pipeline = ReaderImagePipeline.TryOpen(_webtoonCbz)!;
        pipeline.SetViewportWidth(800);
        long acc = 0;
        for (int i = 0; i < 10; i++)
        {
            acc += pipeline.GetPage(i).PixelSize.Height;
        }
        return acc;
    }
}
