using Paperbunkr.App.Services.Reader;
using SkiaSharp;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="StripDecodeSession"/>: band decoding of a long-strip page. Covers the whole-image mode added for progressive JPEG
/// (2026-10-10), where any band can be read in any order, against the forward-only scanline walk used for baseline files.
/// </summary>
public class StripDecodeSessionTests
{
    private const int Width = 64;
    private const int Height = 1000;
    private const int Band = 256;

    private static readonly Action<Action> NoLock = action => action();

    /// <summary>A baseline JPEG strip whose rows change colour every 256 rows, so each band is recognisable.</summary>
    private static byte[] StripJpeg()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            var colours = new[] { SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.White };
            for (int band = 0; band * Band < Height; band++)
            {
                using var paint = new SKPaint { Color = colours[band % colours.Length] };
                canvas.DrawRect(0, band * Band, Width, Band, paint);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        return data.ToArray();
    }

    private static (int R, int G, int B) Middle(SKBitmap band)
    {
        var c = band.GetPixel(band.Width / 2, band.Height / 2);
        return (c.Red > 128 ? 1 : 0, c.Green > 128 ? 1 : 0, c.Blue > 128 ? 1 : 0);
    }

    [Fact]
    public void ABaselineJpeg_IsNotProgressive_AndGetsTheForwardOnlyWalk()
    {
        byte[] bytes = StripJpeg();

        Assert.False(StripDecodeSession.IsProgressiveJpeg(bytes));
        using var session = StripDecodeSession.TryCreate(bytes, NoLock);
        Assert.NotNull(session);
        Assert.False(session!.CanSeek);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC2, 0x00, 0x0B, 8, 0, 1, 0, 1, 1, 1, 0x11, 0 }, true)]   // APP0 then SOF2
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x0B, 8, 0, 1, 0, 1, 1, 1, 0x11, 0 }, false)]  // APP0 then SOF0
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xC1, 0x00, 0x0B, 8, 0, 1, 0, 1, 1, 1, 0x11, 0 }, false)]                                  // extended sequential
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, false)]                                                    // PNG
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02, 0xFF, 0xC2 }, false)]                                                    // scan before any frame header
    [InlineData(new byte[] { 0xFF, 0xD8 }, false)]
    [InlineData(new byte[0], false)]
    public void IsProgressiveJpeg_ReadsTheFrameHeaderMarker(byte[] bytes, bool expected)
    {
        Assert.Equal(expected, StripDecodeSession.IsProgressiveJpeg(bytes));
    }

    [Fact]
    public void AMarkerThatOnlyLooksLikeSof2InsideAnotherSegment_IsNotMistakenForOne()
    {
        // An APP1 segment whose payload contains FF C2, followed by a real baseline frame header.
        byte[] bytes = { 0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x06, 0xFF, 0xC2, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x0B, 8, 0, 1, 0, 1, 1, 1, 0x11, 0 };

        Assert.False(StripDecodeSession.IsProgressiveJpeg(bytes));
    }

    [Fact]
    public void TheWholeImageSession_ReadsBandsInAnyOrder_IncludingBackwards()
    {
        using var session = StripDecodeSession.TryCreateWhole(StripJpeg(), NoLock);
        Assert.NotNull(session);
        Assert.True(session!.CanSeek);
        Assert.Equal(Height, session.PixelSize.Height);

        using var third = session.ReadBand(2 * Band, Band, NoLock);
        using var first = session.ReadBand(0, Band, NoLock);          // behind the band just read: no new session needed
        using var second = session.ReadBand(Band, Band, NoLock);
        using var firstAgain = session.ReadBand(0, Band, NoLock);

        Assert.Equal((0, 0, 1), Middle(third!));
        Assert.Equal((1, 0, 0), Middle(first!));
        Assert.Equal((0, 1, 0), Middle(second!));
        Assert.Equal((1, 0, 0), Middle(firstAgain!));
        Assert.Equal(0, session.NextUnreadRow); // the pipeline reopens a session when a band is behind this
    }

    [Fact]
    public void TheWholeImageSession_GivesTheSamePixels_AsTheForwardOnlyWalk()
    {
        byte[] bytes = StripJpeg();
        using var whole = StripDecodeSession.TryCreateWhole(bytes, NoLock);
        using var forward = StripDecodeSession.TryCreate(bytes, NoLock);

        for (int start = 0; start < Height; start += Band)
        {
            using var a = whole!.ReadBand(start, Band, NoLock);
            using var b = forward!.ReadBand(start, Band, NoLock);
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.Equal(b!.Width, a!.Width);
            Assert.Equal(b.Height, a.Height);
            Assert.Equal(Middle(b), Middle(a));
        }
    }

    [Fact]
    public void TheWholeImageSession_ClipsTheLastBand_AndReturnsNullPastTheEnd()
    {
        using var session = StripDecodeSession.TryCreateWhole(StripJpeg(), NoLock);

        using var last = session!.ReadBand(3 * Band, Band, NoLock);
        Assert.Equal(Height - (3 * Band), last!.Height);
        Assert.Null(session.ReadBand(Height, Band, NoLock));
        Assert.Null(session.ReadBand(0, 0, NoLock));
    }

    [Fact]
    public void ADisposedWholeImageSession_ReturnsNull_NotACrash()
    {
        var session = StripDecodeSession.TryCreateWhole(StripJpeg(), NoLock);
        session!.Dispose();

        Assert.Null(session.ReadBand(0, Band, NoLock));
    }

    // StripBandPlan: the bands to decode, prefetch and keep around the ones on screen.

    [Fact]
    public void EveryBandThePlanAsksFor_IsABandItKeeps()
    {
        // The bug this guards (2026-10-10): prefetching further out than the keep range evicted each prefetched band on the
        // next scroll tick and queued it again, forever.
        for (int fringe = 0; fringe <= 6; fringe++)
        {
            for (int min = 0; min <= 5; min++)
            {
                for (int max = min; max <= min + 1; max++)
                {
                    var plan = StripBandPlan.For(min, max, fringe);

                    Assert.True(plan.KeepMin < plan.PrefetchMin || plan.PrefetchMin == 0, $"fringe {fringe}, bands {min}-{max}: keeps from {plan.KeepMin}, prefetches from {plan.PrefetchMin}");
                    Assert.True(plan.KeepMax > plan.PrefetchMax, $"fringe {fringe}, bands {min}-{max}: keeps to {plan.KeepMax}, prefetches to {plan.PrefetchMax}");
                    Assert.True(plan.PrefetchMin <= plan.DecodeMin && plan.DecodeMax <= plan.PrefetchMax);
                    Assert.True(plan.DecodeMin <= min && max <= plan.DecodeMax);
                }
            }
        }
    }

    [Fact]
    public void ThePlan_NeverKeepsMoreBandsThanTheCacheIsSizedFor()
    {
        for (int fringe = 0; fringe <= 6; fringe++)
        {
            var plan = StripBandPlan.For(10, 11, fringe); // two bands on screen, far from the top

            Assert.True(plan.KeepMax - plan.KeepMin + 1 + (2 * StripBandPlan.EdgeBands) <= StripBandPlan.MaxResidentBands);
        }

        Assert.Equal(StripBandPlan.MaxResidentBands, ReaderMemoryBudget.ResidentBands);
        Assert.Equal(12, StripBandPlan.MaxResidentBands); // 8 for the strip on screen + 2 each for one just above and one just below
    }

    [Fact]
    public void ScrollingOneBandAndBack_AsksForNothingThatWasJustLetGo()
    {
        var before = StripBandPlan.For(5, 5, 4);
        var after = StripBandPlan.For(6, 6, 4);
        var back = StripBandPlan.For(5, 5, 4);

        // Everything "back" wants was still inside what "after" kept.
        Assert.True(back.PrefetchMin >= after.KeepMin);
        Assert.True(back.PrefetchMax <= after.KeepMax);
        Assert.Equal(before, back);
    }

    [Fact]
    public void AFasterDecoder_PrefetchesFurther_ButOnlyUpToTheLimit()
    {
        Assert.Equal(7, StripBandPlan.For(5, 5, 0).PrefetchMax);  // margin 1 + prefetch 1
        Assert.Equal(7, StripBandPlan.For(5, 5, 6).PrefetchMax);  // capped at one band of prefetch
        Assert.Equal(0, StripBandPlan.For(1, 1, 6).PrefetchMin);  // never before the first band
    }

    // BandsInView: what a strip page needs given where it sits against the viewport. A 720x12288 strip (3 bands) drawn at
    // 45% is 5529.6 display units long; the viewport is 0..768.

    private const double StripDisplay = 12288 * 0.45;

    [Fact]
    public void AStripOnScreen_NeedsOnlyTheBandsTheViewportOverlaps()
    {
        // The viewport shows display rows 2000..2768 of the strip: native rows 4444..6151, all in band 1.
        var view = StripBandPlan.BandsInView(stripStart: -2000, StripDisplay, 12288, 0, 768, 4096);

        Assert.Equal((StripReach.Visible, 1, 1), view);
    }

    [Fact]
    public void AStripStraddlingABandEdge_NeedsBothBands()
    {
        // Native row 4096 is display row 1843.2: put it mid-viewport.
        var view = StripBandPlan.BandsInView(stripStart: -1500, StripDisplay, 12288, 0, 768, 4096);

        Assert.Equal((StripReach.Visible, 0, 1), view);
    }

    [Fact]
    public void AStripJustBelowTheScreen_NeedsOnlyItsFirstBand_AndOneJustAboveOnlyItsLast()
    {
        Assert.Equal((StripReach.Near, 0, 0), StripBandPlan.BandsInView(stripStart: 900, StripDisplay, 12288, 0, 768, 4096));
        Assert.Equal((StripReach.Near, 2, 2), StripBandPlan.BandsInView(stripStart: -StripDisplay - 100, StripDisplay, 12288, 0, 768, 4096));
    }

    [Fact]
    public void AStripFarFromTheScreen_NeedsNothing()
    {
        // The bug this guards (2026-10-10): an off-screen strip asked for every one of its bands, so a window of several
        // strips wanted more than the cache could hold and re-decoded them continuously.
        Assert.Equal(StripReach.Far, StripBandPlan.BandsInView(stripStart: 768 + 769, StripDisplay, 12288, 0, 768, 4096).Reach);
        Assert.Equal(StripReach.Far, StripBandPlan.BandsInView(stripStart: -StripDisplay - 769, StripDisplay, 12288, 0, 768, 4096).Reach);
        Assert.Equal(StripReach.Far, StripBandPlan.BandsInView(0, 0, 12288, 0, 768, 4096).Reach);
    }

    [Fact]
    public void TheEdgeOnlyPlan_AsksForExactlyThatBand_AndKeepsOneOfSlack()
    {
        var plan = StripBandPlan.EdgeOnly(0, 0);

        Assert.Equal((0, 0, 0, 0), (plan.DecodeMin, plan.DecodeMax, plan.PrefetchMin, plan.PrefetchMax));
        Assert.True(plan.KeepMin <= 0 && plan.KeepMax >= 0);
        Assert.True(plan.KeepMax - Math.Max(0, plan.KeepMin) + 1 <= StripBandPlan.EdgeBands);
    }

    [Fact]
    public void BytesThatDoNotDecode_GiveNoSession()
    {
        Assert.Null(StripDecodeSession.TryCreateWhole(new byte[] { 1, 2, 3, 4 }, NoLock));
        Assert.Null(StripDecodeSession.TryCreate(new byte[] { 0xFF, 0xD8, 0xFF, 0xC2, 0x00, 0x0B, 8, 0, 1, 0, 1, 1, 1, 0x11, 0 }, NoLock));
    }
}
