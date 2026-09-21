using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #9 - dominant-colour extraction and the scoped Detail accent.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CoverPaletteTests
{
    private static uint Argb(byte r, byte g, byte b, byte a = 255) => ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

    private static uint[] Fill(uint color, int count) => Enumerable.Repeat(color, count).ToArray();

    [Fact]
    public void SolidRedCover_YieldsRed()
    {
        var palette = CoverPalette.FromPixels(Fill(Argb(200, 30, 30), 576));

        var only = Assert.Single(palette);
        Assert.True(only.R > 180 && only.G < 60 && only.B < 60, $"expected red, got {only}");
    }

    [Fact]
    public void MostlyBlackAndWhiteCover_IgnoresThoseAndKeepsTheRealColour()
    {
        var pixels = Fill(Argb(0, 0, 0), 300).Concat(Fill(Argb(250, 250, 250), 200)).Concat(Fill(Argb(20, 120, 220), 76)).ToArray();

        var palette = CoverPalette.FromPixels(pixels);

        var only = Assert.Single(palette);
        Assert.True(only.B > only.R + 80, $"expected blue, got {only}");
    }

    [Fact]
    public void PureGreyscaleCover_HasNoPalette_SoCallersFallBackToTheSkinAccent()
        => Assert.Empty(CoverPalette.FromPixels(Fill(Argb(128, 128, 128), 576).Concat(Fill(Argb(40, 40, 40), 100)).ToArray()));

    [Fact]
    public void TransparentPixels_AreIgnored()
        => Assert.Empty(CoverPalette.FromPixels(Fill(Argb(255, 0, 0, a: 0), 576)));

    [Fact]
    public void TwoDistinctColours_BothAppear_MostDominantFirst_AndNeverMoreThanThree()
    {
        var pixels = Fill(Argb(220, 40, 40), 300)                 // red, dominant
            .Concat(Fill(Argb(40, 60, 220), 200))                // blue
            .Concat(Fill(Argb(40, 200, 60), 120))                // green
            .Concat(Fill(Argb(230, 200, 30), 60))                // yellow (4th - must be dropped)
            .ToArray();

        var palette = CoverPalette.FromPixels(pixels);

        Assert.Equal(CoverPalette.MaxColors, palette.Count);
        Assert.True(palette[0].R > 180, "the most dominant colour comes first");
    }

    [Fact]
    public void NearIdenticalShades_MergeIntoOneEntry()
    {
        var pixels = Fill(Argb(220, 40, 40), 300).Concat(Fill(Argb(224, 44, 40), 300)).ToArray();

        Assert.Single(CoverPalette.FromPixels(pixels));
    }

    [Fact]
    public void FromBitmap_NullOrBroken_IsEmptyNotAnException()
        => Assert.Empty(CoverPalette.FromBitmap(null));

    [Fact]
    public void FromBitmap_ReadsARealBitmap()
    {
        using var bmp = new WriteableBitmap(new PixelSize(48, 48), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bmp.Lock())
        {
            var row = new uint[48];
            System.Array.Fill(row, Argb(30, 160, 60)); // green
            for (int y = 0; y < 48; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy((int[])(object)row, 0, fb.Address + (y * fb.RowBytes), 48);
            }
        }

        var palette = CoverPalette.FromBitmap(bmp);

        var only = Assert.Single(palette);
        Assert.True(only.G > only.R + 60 && only.G > only.B + 60, $"expected green, got {only}");
    }

    [Fact]
    public void AccentScope_SetsOnlyTheScreensOwnResources_AndClearsThemAgain()
    {
        var screen = new UserControl();

        var applied = DetailAccentScope.Apply(screen, new List<Color> { Color.FromRgb(200, 30, 30) });

        Assert.Equal(Color.FromRgb(200, 30, 30), applied);
        Assert.True(screen.Resources.ContainsKey("PbAccentBrush"));
        Assert.True(screen.Resources.ContainsKey("PbAccentTextBrush"));
        Assert.True(screen.Resources.ContainsKey("PbAccentSoftBrush"));

        Assert.Null(DetailAccentScope.Apply(screen, new List<Color>()));
        Assert.False(screen.Resources.ContainsKey("PbAccentBrush"));
    }

    [Fact]
    public void AccentScope_KeepsAccentTextLegible_OnADarkBackground()
    {
        var screen = new UserControl();
        screen.Resources["PbBgColor"] = Colors.Black;

        DetailAccentScope.Apply(screen, new List<Color> { Color.FromRgb(20, 20, 90) }); // a very dark blue accent

        var text = (Color)screen.Resources["PbAccentTextColor"]!;
        Assert.True(Luminance(text) > Luminance(Color.FromRgb(20, 20, 90)), "the text variant is lightened to stay readable on black");
    }

    private static double Luminance(Color c) => (0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B);
}
