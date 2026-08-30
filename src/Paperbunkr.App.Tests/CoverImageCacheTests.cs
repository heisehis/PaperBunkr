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

    private static CoverThumbnailService Service() => new();

    [Fact]
    public void Get_ReturnsSameInstance_OnRepeatedLookups()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        Service().TryGenerateThumbnail(issueId: 101, _cbzPath, fileSize: 5);
        string stem = CoverFingerprint.Stem(101, _cbzPath, 5);

        var first = CoverImageCache.Get(stem);
        var second = CoverImageCache.Get(stem);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void Get_ReturnsNull_ForMissingThumbnail()
    {
        Assert.Null(CoverImageCache.Get("999999-deadbeef"));
    }

    // --- Thread-split API backing AsyncCoverImage: decode must be doable off-thread without
    // touching the (UI-thread-only) LruCache, and storing must be race-safe. Re-keyed on the
    // fingerprint stem, same as Get - see docs/superpowers/specs/2026-08-27-cover-thumbnail-
    // identity-validation-design.md. ---

    [Fact]
    public void TryGetCached_Misses_WithoutDecodingFromDisk()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        Service().TryGenerateThumbnail(issueId: 401, _cbzPath, fileSize: 1);
        string stem = CoverFingerprint.Stem(401, _cbzPath, 1);
        // File exists on disk, but nothing has pulled it into memory yet.
        Assert.False(CoverImageCache.TryGetCached(stem, out var bitmap));
        Assert.Null(bitmap);
    }

    [Fact]
    public void DecodeFromDisk_ReturnsBitmap_WithoutPopulatingTheCache()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        Service().TryGenerateThumbnail(issueId: 402, _cbzPath, fileSize: 1);
        string stem = CoverFingerprint.Stem(402, _cbzPath, 1);

        var decoded = CoverImageCache.DecodeFromDisk(stem);

        Assert.NotNull(decoded);
        Assert.False(CoverImageCache.TryGetCached(stem, out _));
    }

    [Fact]
    public void DecodeFromDisk_ReturnsNull_ForMissingFile()
    {
        Assert.Null(CoverImageCache.DecodeFromDisk("777777-deadbeef"));
    }

    [Fact]
    public void StoreIfAbsent_KeepsTheFirstInstance_WhenTwoDecodesRace()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        Service().TryGenerateThumbnail(issueId: 403, _cbzPath, fileSize: 1);
        string stem = CoverFingerprint.Stem(403, _cbzPath, 1);

        var first = CoverImageCache.DecodeFromDisk(stem)!;
        var second = CoverImageCache.DecodeFromDisk(stem)!;

        var winner = CoverImageCache.StoreIfAbsent(stem, first);
        var loser = CoverImageCache.StoreIfAbsent(stem, second);

        Assert.Same(first, winner);
        Assert.Same(first, loser);
        Assert.Same(first, CoverImageCache.Get(stem));
    }

    [Fact]
    public void Get_ReturnsNull_WhenOnlyAMismatchedStemFileExistsForThatId()
    {
        // The real bug: a rebuild reassigns id 101 to a different comic. The old cover file
        // (101-{oldfp}.jpg) is still on disk, but the id's *current* fingerprint doesn't match it.
        CbzFixture.Create(_cbzPath, pageCount: 1);
        Service().TryGenerateThumbnail(issueId: 101, _cbzPath, fileSize: 100);
        Assert.NotNull(CoverImageCache.Get(CoverFingerprint.Stem(101, _cbzPath, 100)));

        // Same id, different file identity -> different stem -> nothing to serve.
        Assert.Null(CoverImageCache.Get(CoverFingerprint.Stem(101, "C:/somewhere/else.cbz", 100)));
        Assert.Null(CoverImageCache.Get(CoverFingerprint.Stem(101, _cbzPath, 200)));
    }

    // --- Invalidate (real bug found 2026-08-19: a stale on-disk thumbnail from a since-deleted
    // Issue can get "adopted" by a different Issue that lands on the same numeric Id after a
    // library reset/re-migration, and this cache's own file-exists check then serves it forever) ---

    [Fact]
    public void Invalidate_DeletesEveryOnDiskFileForThatId()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        Service().TryGenerateThumbnail(issueId: 202, _cbzPath, fileSize: 1);
        // A stray extra sibling that a normal regenerate would have swept, planted directly to
        // prove Invalidate clears *all* of an id's files, not just its current fingerprint.
        File.WriteAllBytes(CoverThumbnailPaths.GetCachePath("202-0badc0de"), new byte[] { 1 });
        Assert.Equal(2, CoverThumbnailPaths.EnumerateForIssue(202).Count());

        CoverImageCache.Invalidate(202);

        Assert.Empty(CoverThumbnailPaths.EnumerateForIssue(202));
    }

    [Fact]
    public void Invalidate_ClearsTheInMemoryEntry_SoGetStopsServingItAfterTheFileIsGone()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        Service().TryGenerateThumbnail(issueId: 303, _cbzPath, fileSize: 7);
        string stem = CoverFingerprint.Stem(303, _cbzPath, 7);
        Assert.NotNull(CoverImageCache.Get(stem));

        CoverImageCache.Invalidate(303);

        Assert.Null(CoverImageCache.Get(stem));
    }

    [Fact]
    public void Invalidate_NeverCached_DoesNotThrow()
    {
        Assert.Null(Record.Exception(() => CoverImageCache.Invalidate(issueId: 888888)));
    }
}
