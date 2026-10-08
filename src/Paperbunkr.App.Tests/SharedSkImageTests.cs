using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.App.Views;
using SkiaSharp;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="SkiaBitmapConverter.ShareSkImage"/>: the reader's zero-copy route from a decoded page to the Skia image it draws
/// with a colour filter (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md, 2026-10-08), and the reader's
/// automatic memory budget. The sharing reads two non-public Avalonia members, so these tests are what will say so if an
/// Avalonia upgrade moves them.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SharedSkImageTests
{
    private const int Width = 8;
    private const int Height = 6;

    /// <summary>An opaque BGRA page whose pixel (x, y) is blue = 10x, green = 20y, red = 200.</summary>
    private static Bitmap Page()
    {
        var pixels = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int o = ((y * Width) + x) * 4;
                pixels[o] = (byte)(x * 10);
                pixels[o + 1] = (byte)(y * 20);
                pixels[o + 2] = 200;
                pixels[o + 3] = 255;
            }
        }

        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, handle.AddrOfPinnedObject(), new PixelSize(Width, Height), new Vector(96, 96), Width * 4);
        }
        finally
        {
            handle.Free();
        }
    }

    private static void AssertIsThePage(SKImage image)
    {
        Assert.Equal(Width, image.Width);
        Assert.Equal(Height, image.Height);
        using var bitmap = SKBitmap.FromImage(image);
        var corner = bitmap.GetPixel(0, 0);
        var inner = bitmap.GetPixel(5, 3);
        Assert.Equal((200, 0, 0), ((int)corner.Red, (int)corner.Green, (int)corner.Blue));
        Assert.Equal((200, 60, 50), ((int)inner.Red, (int)inner.Green, (int)inner.Blue));
    }

    [Fact]
    public void ADecodedPage_IsShared_NotCopied()
    {
        using var page = Page();

        using var shared = SkiaBitmapConverter.TryShareSkImage(page);

        // Null here means Avalonia moved Bitmap.PlatformImpl or ImmutableBitmap._image: the reader still works (it falls back
        // to a copy), but every adjusted page costs a second full page of memory again. Update TryShareSkImage.
        Assert.NotNull(shared);
        AssertIsThePage(shared!);
    }

    [Fact]
    public void TheSharedImage_HasTheSamePixels_AsTheCopyItReplaces()
    {
        using var page = Page();

        using var shared = SkiaBitmapConverter.ShareSkImage(page);
        using var copied = SkiaBitmapConverter.ToSkImage(page);

        AssertIsThePage(shared);
        AssertIsThePage(copied);
    }

    [Fact]
    public void TheSharedImage_OutlivesTheBitmap_BecauseThePipelineMayDisposeThePageFirst()
    {
        var page = Page();
        using var shared = SkiaBitmapConverter.ShareSkImage(page);

        page.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        AssertIsThePage(shared);
    }

    [Fact]
    public void AWriteableBitmap_FallsBackToACopy()
    {
        using var writeable = new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        Assert.Null(SkiaBitmapConverter.TryShareSkImage(writeable));
        using var image = SkiaBitmapConverter.ShareSkImage(writeable);
        Assert.Equal(4, image.Width);
    }

    [Fact]
    public void ADisposedBitmap_IsNotShared()
    {
        var page = Page();
        page.Dispose();

        Assert.Null(SkiaBitmapConverter.TryShareSkImage(page));
    }

    [Theory]
    [InlineData(2, 128)]    // 64 MiB would be too small for a reading session: floor
    [InlineData(4, 128)]
    [InlineData(8, 256)]
    [InlineData(12, 384)]
    [InlineData(16, 512)]
    [InlineData(64, 512)]   // ceiling
    public void ReaderAutoBudget_IsRamOver32_Between128And512Mebibytes(int ramGb, long expectedMib)
    {
        const long Mib = 1024 * 1024;

        Assert.Equal(expectedMib, ReaderMemoryBudget.AutoFor(ramGb * 1024 * Mib) / Mib);
    }

    [Fact]
    public void AReaderLimitSetInPreferences_OverridesTheAutoBudget()
    {
        Assert.Equal(700L * 1024 * 1024, ReaderMemoryBudget.Resolve(700).TotalBytes);
    }
}
