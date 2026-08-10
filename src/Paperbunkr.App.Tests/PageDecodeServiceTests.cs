using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="PageDecodeService"/> against a real synthetic .cbz, same fixture/collection
/// pattern as <see cref="PageImageDecoderTests"/>. Covers the two behaviors specific to this class
/// that <see cref="PageImageDecoder"/> doesn't have: the virtualization-window memory bound (docs/
/// superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §3/§13)
/// and background-decode priority ordering.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class PageDecodeServiceTests : IDisposable
{
    private readonly string _cbzPath;

    public PageDecodeServiceTests()
    {
        _cbzPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_decode_service_test_{Guid.NewGuid():N}.cbz");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_cbzPath)) File.Delete(_cbzPath);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void TryOpen_ReturnsNull_ForMissingFile()
    {
        var service = PageDecodeService.TryOpen(Path.Combine(Path.GetTempPath(), $"paperbunkr_does_not_exist_{Guid.NewGuid():N}.cbz"));
        Assert.Null(service);
    }

    [Fact]
    public void TryOpen_ReturnsServiceWithCorrectPageCount()
    {
        CbzFixture.Create(_cbzPath, pageCount: 4);

        using var service = PageDecodeService.TryOpen(_cbzPath);

        Assert.NotNull(service);
        Assert.Equal(4, service!.PageCount);
    }

    [Fact]
    public void GetPage_DecodesRealBitmap()
    {
        CbzFixture.Create(_cbzPath, pageCount: 3);
        using var service = PageDecodeService.TryOpen(_cbzPath)!;

        var bitmap = service.GetPage(0);

        Assert.NotNull(bitmap);
        Assert.Equal(64, bitmap.PixelSize.Width);
        Assert.Equal(96, bitmap.PixelSize.Height);
    }

    [Fact]
    public void GetPage_DownsamplesToViewportWidth_WhenNarrowerThanNative()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1); // native 64x96
        using var service = PageDecodeService.TryOpen(_cbzPath)!;
        service.SetViewportWidth(32);

        var bitmap = service.GetPage(0);

        Assert.Equal(32, bitmap.PixelSize.Width);
        Assert.Equal(48, bitmap.PixelSize.Height); // 96 * (32/64)
    }

    [Fact]
    public void GetPage_DoesNotUpscale_WhenViewportWiderThanNative()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1); // native 64x96
        using var service = PageDecodeService.TryOpen(_cbzPath)!;
        service.SetViewportWidth(200);

        var bitmap = service.GetPage(0);

        Assert.Equal(64, bitmap.PixelSize.Width);
        Assert.Equal(96, bitmap.PixelSize.Height);
    }

    /// <summary>
    /// The memory-bound test called out explicitly in spec §13: opens a long synthetic issue,
    /// simulates scrolling through every page by moving the virtualization window one page at a
    /// time (matching a real continuous-scroll session), and asserts the live decoded-bitmap count
    /// never exceeds the window + prefetch-fringe bound - the concrete, checkable form of
    /// "treat decoded-bitmap count as a hard-bounded resource" (docs/onboarding.md §8).
    /// </summary>
    [Fact]
    public void SetVirtualizationWindow_KeepsDecodedBitmapCountBounded_AcrossLongSyntheticScroll()
    {
        const int pageCount = 60;
        const int radius = 2; // matches spec §3's ±2 virtualization window
        CbzFixture.Create(_cbzPath, pageCount);
        using var service = PageDecodeService.TryOpen(_cbzPath)!;

        int maxAllowed = (radius * 2 + 1) + (PageDecodeService.PrefetchRadius * 2);

        for (int center = 0; center < pageCount; center++)
        {
            int min = Math.Max(0, center - radius);
            int max = Math.Min(pageCount - 1, center + radius);
            service.SetVirtualizationWindow(min, max);

            // Simulates the render layer actually drawing every visible page, not just relying on
            // background prefetch to have caught up.
            for (int i = min; i <= max; i++)
            {
                service.GetPage(i);
            }

            Assert.True(service.DecodedPageCount <= maxAllowed,
                $"Decoded page count {service.DecodedPageCount} exceeded bound {maxAllowed} at scroll center {center}.");
        }
    }

    /// <summary>
    /// Background-channel priority ordering under contention (spec §13) - uses
    /// <see cref="PageDecodeService.OnBeforeBackgroundDecode"/> as a deterministic gate (blocking
    /// the consumer loop's very first decode until every page for this window has already been
    /// enqueued) rather than a real-time race, then asserts every high-priority window page was
    /// processed before any low-priority prefetch-fringe page.
    /// </summary>
    [Fact]
    public void BackgroundDecode_ProcessesWindowPages_BeforePrefetchFringe()
    {
        CbzFixture.Create(_cbzPath, pageCount: 10);
        using var service = PageDecodeService.TryOpen(_cbzPath)!;

        var gate = new ManualResetEventSlim(false);
        var order = new List<int>();
        var orderLock = new object();
        bool gateAlreadyHeld = false;

        service.OnBeforeBackgroundDecode = page =>
        {
            lock (orderLock)
            {
                if (!gateAlreadyHeld)
                {
                    gateAlreadyHeld = true;
                    // Released only after SetVirtualizationWindow (below) has finished enqueueing
                    // every page for this window - guarantees the ordering assertion below isn't
                    // racing the producer.
                    Monitor.Exit(orderLock);
                    gate.Wait(TimeSpan.FromSeconds(5));
                    Monitor.Enter(orderLock);
                }

                order.Add(page);
            }
        };

        // Window [4,6] -> high priority; fringe pages 3 and 7 -> low priority (PrefetchRadius = 1).
        service.SetVirtualizationWindow(4, 6);
        gate.Set();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            lock (orderLock)
            {
                if (order.Count >= 5 || DateTime.UtcNow > deadline)
                {
                    break;
                }
            }

            Thread.Sleep(10);
        }

        lock (orderLock)
        {
            Assert.Equal(5, order.Count);
            int lastHighPosition = order.FindLastIndex(p => p is >= 4 and <= 6);
            int firstLowPosition = order.FindIndex(p => p is 3 or 7);
            Assert.True(lastHighPosition < firstLowPosition,
                $"Expected all high-priority window pages before low-priority fringe pages. Order: {string.Join(",", order)}");
        }
    }
}
