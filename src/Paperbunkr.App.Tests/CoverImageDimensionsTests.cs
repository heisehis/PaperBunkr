using Paperbunkr.App.Services;
using SdBitmap = System.Drawing.Bitmap;
using SdImageFormat = System.Drawing.Imaging.ImageFormat;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="CoverImageDimensions"/> - the header-only JPEG size reader the cover
/// aspect-ratio backfill uses to sweep thousands of cached covers without a full decode.
/// </summary>
public class CoverImageDimensionsTests
{
    private static string WriteJpeg(int width, int height)
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_jpegdims_{Guid.NewGuid():N}.jpg");
        using var bitmap = new SdBitmap(width, height);
        bitmap.Save(path, SdImageFormat.Jpeg);
        return path;
    }

    [Theory]
    [InlineData(400, 600)]
    [InlineData(613, 251)]
    [InlineData(1, 1)]
    public void TryRead_ReturnsTheRealPixelDimensions(int width, int height)
    {
        string path = WriteJpeg(width, height);
        try
        {
            Assert.True(CoverImageDimensions.TryRead(path, out int w, out int h));
            Assert.Equal(width, w);
            Assert.Equal(height, h);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public void TryRead_ReturnsFalse_ForAMissingFile()
    {
        Assert.False(CoverImageDimensions.TryRead(
            Path.Combine(Path.GetTempPath(), $"nope_{Guid.NewGuid():N}.jpg"), out int w, out int h));
        Assert.Equal(0, w);
        Assert.Equal(0, h);
    }

    [Fact]
    public void TryRead_ReturnsFalse_ForANonJpegFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_notjpeg_{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        try
        {
            Assert.False(CoverImageDimensions.TryRead(path, out _, out _));
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
