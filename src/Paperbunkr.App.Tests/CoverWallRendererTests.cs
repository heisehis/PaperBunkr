using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Paperbunkr.App.Services;
using SkiaSharp;

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

    /// <summary>
    /// The concrete Windows 11 regression (docs/superpowers/specs/2026-09-08-home-navrail-visual-v2-
    /// design.md §3): windows_11's <c>surface0</c> is <c>#F3F3F3</c> (the only light skin) - before
    /// this fix, <see cref="CoverWallRenderer"/> baked a fixed near-black scrim regardless of skin,
    /// so the masthead stayed dark under a light skin. Rendering with the real skin color now
    /// produces a corner pixel meaningfully lighter than rendering with the pre-fix hardcoded
    /// default - not just "doesn't crash".
    /// </summary>
    [Fact]
    public void Render_WithWindows11BaseColor_IsVisiblyLighterThan_TheOldHardcodedDefault()
    {
        var covers = new List<Bitmap> { SolidCover(60, 90), SolidCover(80, 120) };
        var windows11Surface0 = Color.Parse("#F3F3F3");
        var lightBaseColor = new SKColor(windows11Surface0.R, windows11Surface0.G, windows11Surface0.B);

        var oldStyleRender = CoverWallRenderer.Render(covers, new PixelSize(400, 200), CoverWallRenderer.DefaultBaseColor);
        var themedRender = CoverWallRenderer.Render(covers, new PixelSize(400, 200), lightBaseColor);

        Assert.NotNull(oldStyleRender);
        Assert.NotNull(themedRender);

        double oldLuminance = CornerLuminance(oldStyleRender!);
        double themedLuminance = CornerLuminance(themedRender!);

        Assert.True(themedLuminance > oldLuminance + 20,
            $"windows_11-themed corner (luminance {themedLuminance}) should be visibly lighter than the old hardcoded-default corner (luminance {oldLuminance})");
    }

    /// <summary>
    /// General property backing the fix, across all 5 skins' actual <c>surface0</c> tones: the
    /// corner should track (get proportionally darkened, not decoupled from) whatever base color the
    /// caller passes in, rather than the render staying a constant color regardless of input - the
    /// literal bug this whole step fixes.
    /// </summary>
    [Theory]
    [InlineData("#0A0B0D")] // default
    [InlineData("#060810")] // cool_technical
    [InlineData("#000000")] // vibrant_pop / vintage_paperback (both use pure black surface0)
    [InlineData("#F3F3F3")] // windows_11
    public void Render_CornerLuminance_TracksTheGivenBaseColorsLuminance(string skinSurface0Hex)
    {
        var covers = new List<Bitmap> { SolidCover(60, 90), SolidCover(80, 120) };
        var skinColor = Color.Parse(skinSurface0Hex);
        var baseColor = new SKColor(skinColor.R, skinColor.G, skinColor.B);

        var wall = CoverWallRenderer.Render(covers, new PixelSize(400, 200), baseColor);
        Assert.NotNull(wall);

        double cornerLuminance = CornerLuminance(wall!);
        double skinLuminance = Luminance(skinColor.R, skinColor.G, skinColor.B);

        // The scrim+vignette compositing darkens the base color, so the corner won't equal the raw
        // skin color - asserting an exact value would be fragile against future compositing-math
        // tweaks. Assert it stays within a plausible darkened range of its own base luminance
        // instead, which is enough to catch a regression back to "ignores baseColor entirely".
        Assert.InRange(cornerLuminance, skinLuminance * 0.3, skinLuminance + 5);
    }

    private static double CornerLuminance(Bitmap bitmap)
    {
        using var framebuffer = ((WriteableBitmap)bitmap).Lock();
        var cornerBytes = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(framebuffer.Address, cornerBytes, 0, 4);
        // Bgra8888
        return Luminance(cornerBytes[2], cornerBytes[1], cornerBytes[0]);
    }

    private static double Luminance(byte r, byte g, byte b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;
}
