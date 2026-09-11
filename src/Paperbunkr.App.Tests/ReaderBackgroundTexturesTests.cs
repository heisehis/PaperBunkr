using System.Linq;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ReaderBackgroundTextures"/> (docs/superpowers/specs/2026-09-10-reader-
/// backlog-batch-b-design.md Item 1). Runs under <see cref="AvaloniaTestCollection"/> since
/// <c>LoadBitmap</c> goes through <c>AssetLoader</c>/<c>Bitmap</c>, same as
/// <see cref="WindowsElevenSkinTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReaderBackgroundTexturesTests
{
    [Fact]
    public void All_HasThreeUniqueIds()
    {
        Assert.Equal(3, ReaderBackgroundTextures.All.Count);
        Assert.Equal(3, ReaderBackgroundTextures.All.Select(t => t.Id).Distinct().Count());
        Assert.Equal("neutral-dark", ReaderBackgroundTextures.All[0].Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bogus-id")]
    public void Resolve_UnknownOrMissing_FallsBackToFirstTexture(string? id)
    {
        var resolved = ReaderBackgroundTextures.Resolve(id);

        Assert.Equal(ReaderBackgroundTextures.All[0].Id, resolved.Id);
    }

    [Fact]
    public void Resolve_KnownId_ReturnsThatTexture()
    {
        var resolved = ReaderBackgroundTextures.Resolve("carbon");

        Assert.Equal("carbon", resolved.Id);
    }

    [Fact]
    public void LoadBitmap_DecodesTheAsset_AndCachesTheSameInstance()
    {
        var first = ReaderBackgroundTextures.LoadBitmap("carbon");
        var second = ReaderBackgroundTextures.LoadBitmap("carbon");

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void LoadBitmap_UnknownId_LoadsTheFallbackTexture()
    {
        var bitmap = ReaderBackgroundTextures.LoadBitmap("does-not-exist");

        Assert.Same(ReaderBackgroundTextures.LoadBitmap(ReaderBackgroundTextures.All[0].Id), bitmap);
    }
}
