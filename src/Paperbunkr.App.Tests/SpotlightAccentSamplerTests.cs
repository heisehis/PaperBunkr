using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="SpotlightAccentSampler"/> feeds the Home masthead's ambient-recolor feature
/// (docs/superpowers/specs/2026-09-08-home-navrail-visual-v2-design.md §2/§4). Runs under
/// <see cref="AvaloniaTestCollection"/> for the same reason <see cref="CoverWallRendererTests"/>
/// does - <see cref="WriteableBitmap"/> + SkiaSharp surface creation need a registered platform
/// render interface.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SpotlightAccentSamplerTests
{
    /// <summary>
    /// Unlike <see cref="CoverWallRendererTests.SolidCover"/> (blank pixels, fine for size-only
    /// assertions), this actually writes a known BGRA fill into the framebuffer so
    /// <see cref="SpotlightAccentSampler.Sample"/>'s color output can be asserted against a real
    /// expected value.
    /// </summary>
    private static Bitmap SolidColorBitmap(int width, int height, Color color)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var framebuffer = bitmap.Lock();

        var row = new byte[framebuffer.RowBytes];
        for (int x = 0; x < width; x++)
        {
            int offset = x * 4;
            row[offset + 0] = color.B;
            row[offset + 1] = color.G;
            row[offset + 2] = color.R;
            row[offset + 3] = color.A;
        }

        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(row, 0, framebuffer.Address + y * framebuffer.RowBytes, row.Length);
        }

        return bitmap;
    }

    [Fact]
    public void Sample_SolidColorBitmap_ReturnsThatColor()
    {
        var color = Color.FromRgb(0x40, 0x90, 0xC0);
        var bitmap = SolidColorBitmap(40, 40, color);

        var sampled = SpotlightAccentSampler.Sample(bitmap);

        // A 1x1 downsample of a perfectly solid source should round-trip near-exactly; a small
        // tolerance covers premultiplied-alpha/color-space rounding through the Skia draw.
        Assert.InRange(sampled.R, color.R - 3, color.R + 3);
        Assert.InRange(sampled.G, color.G - 3, color.G + 3);
        Assert.InRange(sampled.B, color.B - 3, color.B + 3);
    }

    [Fact]
    public void Sample_DifferentSolidColors_ProduceDifferentOutput()
    {
        var warm = SolidColorBitmap(40, 40, Color.FromRgb(0xC9, 0x80, 0x3F));
        var cool = SolidColorBitmap(40, 40, Color.FromRgb(0x38, 0x5A, 0x8D));

        var warmSampled = SpotlightAccentSampler.Sample(warm);
        var coolSampled = SpotlightAccentSampler.Sample(cool);

        Assert.NotEqual(warmSampled, coolSampled);
    }

    [Fact]
    public void FallbackColor_IsANeutralMidGray()
    {
        // Documents the contract Step 4/BuildMastheadBackdrop relies on for a missing/degenerate
        // spotlight cover: a color that won't skew the scrim warm or cool on any of the 5 skins.
        Assert.Equal(Color.FromRgb(0x80, 0x80, 0x80), SpotlightAccentSampler.FallbackColor);
    }
}
