using System.Collections.Generic;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="CoverWallRenderer"/> composes the blurred masthead cover-wall for the redesigned Home
/// screen (docs/superpowers/specs/2026-08-28-home-screen-redesign-design.md §2). Runs under
/// <see cref="AvaloniaTestCollection"/> since <see cref="WriteableBitmap"/> + SkiaSharp surface
/// creation need a registered platform render interface.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CoverWallRendererTests
{
    private static Bitmap SolidCover(int w, int h) =>
        new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

    [Fact]
    public void Render_ReturnsBitmapOfRequestedSize_ForAHandfulOfCovers()
    {
        var covers = new List<Bitmap> { SolidCover(60, 90), SolidCover(80, 120), SolidCover(64, 96), SolidCover(70, 100) };

        var wall = CoverWallRenderer.Render(covers, new PixelSize(800, 240));

        Assert.NotNull(wall);
        Assert.Equal(new PixelSize(800, 240), wall!.PixelSize);
    }

    [Fact]
    public void Render_ReturnsNull_WhenThereAreNoCovers()
    {
        Assert.Null(CoverWallRenderer.Render(new List<Bitmap>(), new PixelSize(800, 240)));
    }

    [Fact]
    public void Render_HandlesFewerCoversThanTheGridWouldLike()
    {
        var covers = new List<Bitmap> { SolidCover(60, 90) };

        var wall = CoverWallRenderer.Render(covers, new PixelSize(400, 200));

        Assert.NotNull(wall);
        Assert.Equal(new PixelSize(400, 200), wall!.PixelSize);
    }

    [Fact]
    public void Render_ReturnsNull_ForANonPositiveTargetSize()
    {
        var covers = new List<Bitmap> { SolidCover(60, 90) };

        Assert.Null(CoverWallRenderer.Render(covers, new PixelSize(0, 240)));
    }
}
