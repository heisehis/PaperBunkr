using System.Drawing;
using System.Drawing.Imaging;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ArcCoverImageCache"/>'s cache-hit/invalidate paths (docs/superpowers/specs/
/// 2026-08-22-cbl-manager-arc-cover-and-synopsis-design.md) - same
/// <see cref="AvaloniaTestCollection"/> rationale as <see cref="CoverImageCacheTests"/> (Bitmap
/// construction needs a registered IPlatformRenderInterface), redirecting
/// <see cref="ArcCoverPaths.CacheDirectory"/> to a temp folder. The live-download path
/// (<see cref="ArcCoverImageCache.DownloadAndCacheAsync"/>) isn't covered here - same "no live
/// network in CI" stance as the reading-list-source adapter tests.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ArcCoverImageCacheTests : IDisposable
{
    private readonly string _originalCacheDirectory;
    private readonly string _cacheDirectory;

    public ArcCoverImageCacheTests()
    {
        _originalCacheDirectory = ArcCoverPaths.CacheDirectory;
        _cacheDirectory = Path.Combine(Path.GetTempPath(), $"paperbunkr_arccover_test_{Guid.NewGuid():N}");
        ArcCoverPaths.CacheDirectory = _cacheDirectory;
    }

    public void Dispose()
    {
        ArcCoverPaths.CacheDirectory = _originalCacheDirectory;
        try
        {
            if (Directory.Exists(_cacheDirectory)) Directory.Delete(_cacheDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static void WriteFakeCoverFile(int readingListId)
    {
        string path = ArcCoverPaths.GetCachePath(readingListId);
        using var bitmap = new Bitmap(32, 48);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.SteelBlue);
        }

        bitmap.Save(path, ImageFormat.Jpeg);
    }

    [Fact]
    public void Get_ReturnsNull_WhenNothingCached()
    {
        Assert.Null(ArcCoverImageCache.Get(readingListId: 999));
    }

    [Fact]
    public void Get_ReturnsDecodedBitmap_WhenAFileAlreadyExistsOnDisk()
    {
        WriteFakeCoverFile(101);
        var bitmap = ArcCoverImageCache.Get(101);

        Assert.NotNull(bitmap);
        Assert.Equal(32, bitmap!.PixelSize.Width);
    }

    [Fact]
    public void Get_ReturnsSameInstance_OnRepeatedLookups()
    {
        WriteFakeCoverFile(202);
        var first = ArcCoverImageCache.Get(202);
        var second = ArcCoverImageCache.Get(202);

        Assert.Same(first, second);
    }

    [Fact]
    public void Invalidate_DeletesTheOnDiskFile_AndTheInMemoryEntry()
    {
        WriteFakeCoverFile(303);
        var cached = ArcCoverImageCache.Get(303);
        Assert.NotNull(cached);

        ArcCoverImageCache.Invalidate(303);

        Assert.False(File.Exists(ArcCoverPaths.GetCachePath(303)));
        Assert.Null(ArcCoverImageCache.Get(303));
    }

    [Fact]
    public void Invalidate_NeverCached_DoesNotThrow()
    {
        var exception = Record.Exception(() => ArcCoverImageCache.Invalidate(readingListId: 888888));
        Assert.Null(exception);
    }

    // --- TrySetCustomCover (docs/superpowers/specs/2026-08-23-reading-list-tags-design.md's local
    // cover picker) - mirrors CoverThumbnailServiceTests' own TrySetCustomCover coverage. ---

    private static string CreateSourceImage()
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_arccover_source_test_{Guid.NewGuid():N}.png");
        using var bitmap = new Bitmap(64, 96);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.CornflowerBlue);
        }

        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public void TrySetCustomCover_WritesDecodableJpeg_AndUpdatesTheCache()
    {
        string imagePath = CreateSourceImage();
        try
        {
            bool result = ArcCoverImageCache.TrySetCustomCover(404, imagePath);

            Assert.True(result);
            Assert.True(File.Exists(ArcCoverPaths.GetCachePath(404)));
            Assert.NotNull(ArcCoverImageCache.Get(404));
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public void TrySetCustomCover_OverwritesWhateverWasCachedBefore_SamePhysicalSlot()
    {
        // 32x48 fake "remote" cover, unscaled (WriteFakeCoverFile saves as-is).
        WriteFakeCoverFile(505);
        var original = ArcCoverImageCache.Get(505);
        Assert.Equal(32, original!.PixelSize.Width);

        // 64x96 local pick - TrySetCustomCover scales to fit within 400px longest edge, so this
        // stays 64x96 (already under the cap), distinguishably different from the original 32x48 -
        // proves the same physical cache slot actually got overwritten, not left alone.
        string imagePath = CreateSourceImage();
        try
        {
            bool result = ArcCoverImageCache.TrySetCustomCover(505, imagePath);

            Assert.True(result);
            var updated = ArcCoverImageCache.Get(505);
            Assert.Equal(64, updated!.PixelSize.Width);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public void TrySetCustomCover_ReturnsFalse_ForUnreadableImagePath()
    {
        bool result = ArcCoverImageCache.TrySetCustomCover(606, Path.Combine(Path.GetTempPath(), $"does_not_exist_{Guid.NewGuid():N}.png"));

        Assert.False(result);
    }
}
