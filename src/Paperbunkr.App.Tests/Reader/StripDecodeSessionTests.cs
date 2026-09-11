using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Paperbunkr.App.Services.Reader;
using SkiaSharp;

namespace Paperbunkr.App.Tests.Reader;

/// <summary>
/// Step 3 of the webtoon band-decode plan (docs/superpowers/specs/2026-09-09-reader-webtoon-strip-
/// band-decode-plan.md): <see cref="StripDecodeSession"/>, the forward-only scanline-decode wrapper
/// band decode is actually built on (SKCodec's <c>GetPixels(..., Subset)</c> - the mechanism an
/// earlier design revision assumed would work - is rejected outright by Skia's JPEG/PNG codecs;
/// real row-range decode only exists, for formats that support it at all, via
/// <c>StartScanlineDecode</c>/<c>SkipScanlines</c>/<c>GetScanlines</c>, verified against Skia's own
/// source). No pipeline dependency - exercises the class directly with a no-op lock passthrough.
///
/// PNG is deliberately not covered by the pixel-correctness tests here (design rev 5, found by
/// actually running it): Skia's PNG codec supports neither a working scanline path nor a working
/// incremental-decode-with-subset path in this build, so <see cref="StripDecodeSession.TryCreate"/>
/// is expected to return <see langword="null"/> for it - see
/// <see cref="TryCreate_Png_ReturnsNull_ScanlineDecodeUnsupported"/>.
/// </summary>
public class StripDecodeSessionTests
{
    private static void NoLock(Action action) => action();

    /// <summary>
    /// A distinctive per-row/per-column pattern (not a flat color) so a wrong-row bug in the
    /// skip/read chunking would actually change the compared pixels, not accidentally match anyway.
    /// Built via <see cref="Bitmap.LockBits"/> rather than per-pixel <see cref="Bitmap.SetPixel"/> -
    /// the strips here run into the thousands of rows, and SetPixel's per-call marshalling makes
    /// that genuinely slow. 24bpp (no alpha channel) - matches what a real comic page actually is,
    /// and sidesteps an unrelated PNG-alpha-type wrinkle unconnected to this class's own contract.
    /// </summary>
    private static byte[] EncodeStrip(int width, int height, ImageFormat format)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[data.Stride];
            for (int y = 0; y < height; y++)
            {
                byte r = (byte)(y % 256);
                byte g = (byte)((y / 256) % 256);
                for (int x = 0; x < width; x++)
                {
                    byte b = (byte)(x % 256);
                    int i = x * 3;
                    row[i] = b;     // B
                    row[i + 1] = g; // G
                    row[i + 2] = r; // R
                }
                Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), data.Stride);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        using var ms = new MemoryStream();
        bitmap.Save(ms, format);
        return ms.ToArray();
    }

    /// <summary>The same rows a full, ordinary decode of these exact bytes would produce - the ground truth <see cref="StripDecodeSession"/>'s banded output must match.</summary>
    private static SKBitmap DecodeFullyForComparison(byte[] bytes)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(bytes));
        var info = new SKImageInfo(codec!.Info.Width, codec.Info.Height, codec.Info.ColorType, codec.Info.AlphaType);
        var bitmap = new SKBitmap(info);
        codec.GetPixels(info, bitmap.GetPixels());
        return bitmap;
    }

    private static void AssertBandMatchesFullDecode(SKBitmap band, SKBitmap full, int bandStartRow)
    {
        for (int y = 0; y < band.Height; y++)
        {
            for (int x = 0; x < band.Width; x += 37) // sample, not every pixel - keeps the test fast
            {
                Assert.Equal(full.GetPixel(x, bandStartRow + y), band.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void TryCreate_Jpeg_Succeeds_AndScanlineDecodesCorrectly()
    {
        byte[] bytes = EncodeStrip(width: 200, height: 6000, ImageFormat.Jpeg);

        using var full = DecodeFullyForComparison(bytes);
        using var session = StripDecodeSession.TryCreate(bytes, NoLock);

        Assert.NotNull(session);
        Assert.Equal(200, session!.PixelSize.Width);
        Assert.Equal(6000, session.PixelSize.Height);

        using (var band0 = session.ReadBand(0, ReaderImagePipeline.BandHeight, NoLock))
        {
            Assert.NotNull(band0);
            AssertBandMatchesFullDecode(band0!, full, bandStartRow: 0);
        }

        using (var band1 = session.ReadBand(ReaderImagePipeline.BandHeight, ReaderImagePipeline.BandHeight, NoLock))
        {
            Assert.NotNull(band1);
            // The strip is shorter than 2*BandHeight, so band 1 is clipped to the remaining rows.
            Assert.Equal(6000 - ReaderImagePipeline.BandHeight, band1!.Height);
            AssertBandMatchesFullDecode(band1, full, bandStartRow: ReaderImagePipeline.BandHeight);
        }
    }

    [Fact]
    public void TryCreate_Png_ReturnsNull_ScanlineDecodeUnsupported()
    {
        // Design rev 5: SkPngCodec has no working row-range decode path in this SkiaSharp build -
        // TryCreate must report that plainly (null) rather than hand back a session that would
        // silently misbehave the first time ReadBand is called.
        byte[] bytes = EncodeStrip(width: 200, height: 6000, ImageFormat.Png);

        var session = StripDecodeSession.TryCreate(bytes, NoLock);

        Assert.Null(session);
    }

    [Fact]
    public void ReadBand_BackwardRequest_Throws()
    {
        byte[] bytes = EncodeStrip(width: 100, height: 6000, ImageFormat.Jpeg);
        using var session = StripDecodeSession.TryCreate(bytes, NoLock)!;

        using (session.ReadBand(4096, 1000, NoLock)) { }

        Assert.Throws<InvalidOperationException>(() => session.ReadBand(0, 1000, NoLock));
    }

    [Fact]
    public void ReadBand_LargeSkip_ChunksTheLockCallback_NotOneGiantAcquisition()
    {
        // Image tall enough that skipping from row 0 to row 8192 spans exactly two BandHeight-sized
        // (4096-row) chunks, plus two read chunks (1808 rows over 2, since GetScanlines is also
        // chunked) - design rev 4/5's lock-chunking requirement (docs/superpowers/specs/2026-09-09-
        // reader-webtoon-strip-band-decode-design.md §4.1: "the lock must not be held for the full
        // duration of a large skip"), now scoped to this session's own lock rather than the
        // pipeline's global one.
        byte[] bytes = EncodeStrip(width: 100, height: 10000, ImageFormat.Jpeg);
        using var session = StripDecodeSession.TryCreate(bytes, NoLock)!;

        int lockCallCount = 0;
        void CountingLock(Action action)
        {
            lockCallCount++;
            action();
        }

        using var band = session.ReadBand(8192, 1808, CountingLock);

        Assert.NotNull(band);
        // 2 (8192-row skip, chunked at 4096) + 1 (1808-row read, one chunk, under 4096) = 3.
        Assert.Equal(3, lockCallCount);
    }

    [Fact]
    public void ReadBand_PastTheStripsRealHeight_ReturnsNull()
    {
        byte[] bytes = EncodeStrip(width: 100, height: 500, ImageFormat.Jpeg);
        using var session = StripDecodeSession.TryCreate(bytes, NoLock)!;

        Assert.Null(session.ReadBand(500, 100, NoLock));
    }
}
