using Avalonia.Media;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="SvgMarkRenderer"/> rasterises the bundled brand / flag / rating SVGs into cached
/// bitmaps (docs/superpowers/specs/2026-08-28-brand-metadata-iconography-design.md §1 / plan Step 1).
/// A null result here means every SVG-backed mark in the app has gone blank.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SvgMarkRendererTests
{
    private const string Services = "avares://Paperbunkr.App/Assets/Marks/Services/";
    private const string Ratings = "avares://Paperbunkr.App/Assets/Marks/AgeRatings/";

    [Fact]
    public void Render_MonochromeSvg_ProducesABitmap()
    {
        var bmp = SvgMarkRenderer.Render(Services + "anilist.svg", 32);
        Assert.NotNull(bmp);
        Assert.True(bmp!.PixelSize.Width > 0 && bmp.PixelSize.Height > 0);
    }

    [Fact]
    public void Render_MultiColourSvg_ProducesABitmap()
    {
        var bmp = SvgMarkRenderer.Render(Services + "mangadex.svg", 40);
        Assert.NotNull(bmp);
        Assert.True(bmp!.PixelSize.Width > 0);
    }

    [Fact]
    public void Render_PortraitRatingBox_KeepsItsAspect()
    {
        var bmp = SvgMarkRenderer.Render(Ratings + "teen.svg", 60);
        Assert.NotNull(bmp);
        Assert.True(bmp!.PixelSize.Height > bmp.PixelSize.Width, "ESRB box should come back portrait");
    }

    [Fact]
    public void Render_Tint_ChangesOutput()
    {
        var plain = SvgMarkRenderer.Render(Services + "anilist.svg", 24);
        var tinted = SvgMarkRenderer.Render(Services + "anilist.svg", 24, Colors.Red);
        Assert.NotNull(plain);
        Assert.NotNull(tinted);
        Assert.NotSame(plain, tinted);
    }

    [Fact]
    public void Render_MissingAsset_ReturnsNull()
    {
        Assert.Null(SvgMarkRenderer.Render(Services + "does-not-exist.svg", 16));
    }
}
