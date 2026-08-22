using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="CoverImageCache"/>. Runs under <see cref="AvaloniaTestCollection"/> for
/// the same reason as <see cref="CoverThumbnailServiceTests"/> - Bitmap construction needs a
/// registered IPlatformRenderInterface. Redirects <see cref="CoverThumbnailPaths.ThumbnailDirectory"/>
/// to a temp folder so tests never touch the real per-user thumbnail cache.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CoverImageCacheTests : IDisposable
{
    private readonly string _originalThumbnailDirectory;
    private readonly string _thumbnailDirectory;
    private readonly string _cbzPath;

    public CoverImageCacheTests()
    {
        _originalThumbnailDirectory = CoverThumbnailPaths.ThumbnailDirectory;
        _thumbnailDirectory = Path.Combine(Path.GetTempPath(), $"paperbunkr_cache_test_{Guid.NewGuid():N}");
        CoverThumbnailPaths.ThumbnailDirectory = _thumbnailDirectory;

        _cbzPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_cache_cbz_{Guid.NewGuid():N}.cbz");
    }

    public void Dispose()
    {
        CoverThumbnailPaths.ThumbnailDirectory = _originalThumbnailDirectory;

        try
        {
            if (File.Exists(_cbzPath)) File.Delete(_cbzPath);
            if (Directory.Exists(_thumbnailDirectory)) Directory.Delete(_thumbnailDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Get_ReturnsSameInstance_OnRepeatedLookups()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        new CoverThumbnailService().TryGenerateThumbnail(issueId: 101, _cbzPath);

        var first = CoverImageCache.Get(101);
        var second = CoverImageCache.Get(101);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void Get_ReturnsNull_ForMissingThumbnail()
    {
        var result = CoverImageCache.Get(issueId: 999999);

        Assert.Null(result);
    }

    // --- Invalidate (real bug found 2026-08-19: a stale on-disk thumbnail from a since-deleted
    // Issue can get "adopted" by a different Issue that lands on the same numeric Id after a
    // library reset/re-migration, and this cache's own file-exists check then serves it forever) ---

    [Fact]
    public void Invalidate_DeletesTheOnDiskFile()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        new CoverThumbnailService().TryGenerateThumbnail(issueId: 202, _cbzPath);
        string path = CoverThumbnailPaths.GetCachePath(202);
        Assert.True(File.Exists(path));

        CoverImageCache.Invalidate(202);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Invalidate_ClearsTheInMemoryEntry_SoGetStopsServingItAfterTheFileIsGone()
    {
        // The exact real-world bug this fixes: without dropping the in-memory LruCache entry too,
        // Get() would keep handing back the stale Bitmap object forever even after its on-disk file
        // was deleted - the cache-hit path (LruCacheTryGetValue) never re-checks the filesystem.
        CbzFixture.Create(_cbzPath, pageCount: 1);
        new CoverThumbnailService().TryGenerateThumbnail(issueId: 303, _cbzPath);
        var stale = CoverImageCache.Get(303);
        Assert.NotNull(stale);

        CoverImageCache.Invalidate(303);

        Assert.Null(CoverImageCache.Get(303));
    }

    [Fact]
    public void Invalidate_NeverCached_DoesNotThrow()
    {
        var exception = Record.Exception(() => CoverImageCache.Invalidate(issueId: 888888));

        Assert.Null(exception);
    }
}
