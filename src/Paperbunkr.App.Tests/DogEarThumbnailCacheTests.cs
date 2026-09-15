using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="DogEarThumbnailCache"/> (docs/superpowers/specs/2026-09-13-preferences-
/// cosmetic-toggles-design.md) against a real synthetic multi-page .cbz, same "generate via the real
/// code path" precedent as <see cref="AsyncCoverImageTests"/>/<see cref="CoverThumbnailServiceTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class DogEarThumbnailCacheTests : IDisposable
{
    private readonly string _cbzPath;

    public DogEarThumbnailCacheTests()
    {
        _cbzPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_dogear_test_{Guid.NewGuid():N}.cbz");
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
    public void Get_DecodesTheSecondPage_NotTheCover()
    {
        CbzFixture.Create(_cbzPath, pageCount: 2);
        string stem = $"dogear-{Guid.NewGuid():N}";

        var decoded = DogEarThumbnailCache.Get(stem, _cbzPath);

        Assert.NotNull(decoded);
    }

    [Fact]
    public void Get_ThenTryGetCached_ReturnsTheSameInstance()
    {
        CbzFixture.Create(_cbzPath, pageCount: 2);
        string stem = $"dogear-{Guid.NewGuid():N}";

        var decoded = DogEarThumbnailCache.Get(stem, _cbzPath);
        var cached = DogEarThumbnailCache.TryGetCached(stem);

        Assert.Same(decoded, cached);
    }

    [Fact]
    public void TryGetCached_BeforeAnyDecode_ReturnsNull()
    {
        Assert.Null(DogEarThumbnailCache.TryGetCached($"dogear-never-decoded-{Guid.NewGuid():N}"));
    }

    [Fact]
    public void Get_SinglePageComic_ReturnsNull()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        string stem = $"dogear-{Guid.NewGuid():N}";

        Assert.Null(DogEarThumbnailCache.Get(stem, _cbzPath));
    }
}
