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
}
