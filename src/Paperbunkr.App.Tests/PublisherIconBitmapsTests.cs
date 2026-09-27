using Paperbunkr.App.Services;
using SkiaSharp;

namespace Paperbunkr.App.Tests;

/// <summary>Contrast-plate classifier for CE publisher rasters
/// (docs/superpowers/specs/2026-09-25-publisher-icons-and-reader-textures-design.md §A3).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class PublisherIconBitmapsTests
{
    [Fact]
    public void WholeBundledPack_DecodesAndUsesEveryPlateClass()
    {
        var uris = Avalonia.Platform.AssetLoader
            .GetAssets(new Uri("avares://Paperbunkr.App/Assets/Icons/Publishers/"), null)
            .Where(u => u.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                     || u.AbsolutePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(uris.Count >= 700, $"expected the CE pack, found {uris.Count}");

        var counts = new Dictionary<IconPlate, int>();
        foreach (Uri uri in uris)
        {
            var icon = PublisherIconBitmaps.Load(uri.ToString());
            Assert.True(icon is not null, "failed to decode " + uri);
            counts[icon!.Plate] = counts.GetValueOrDefault(icon.Plate) + 1;
        }

        // Opaque logos, dark-on-transparent (the bulk) and light-on-transparent all exist in the pack.
        Assert.True(counts.GetValueOrDefault(IconPlate.None) > 50, "opaque: " + counts.GetValueOrDefault(IconPlate.None));
        Assert.True(counts.GetValueOrDefault(IconPlate.Light) > 300, "light: " + counts.GetValueOrDefault(IconPlate.Light));
        Assert.True(counts.GetValueOrDefault(IconPlate.Dark) > 20, "dark: " + counts.GetValueOrDefault(IconPlate.Dark));
    }

    private static SKColor[] Fill(int count, SKColor c) => Enumerable.Repeat(c, count).ToArray();

    [Fact]
    public void OpaqueImage_NeedsNoPlate()
    {
        Assert.Equal(IconPlate.None, PublisherIconBitmaps.ClassifyPlate(Fill(64, new SKColor(20, 20, 20, 255))));
    }

    [Fact]
    public void TransparentDarkLogo_GetsTheLightPlate()
    {
        var px = Fill(32, SKColors.Transparent).Concat(Fill(32, new SKColor(10, 10, 10, 255))).ToArray();
        Assert.Equal(IconPlate.Light, PublisherIconBitmaps.ClassifyPlate(px));
    }

    [Fact]
    public void TransparentLightLogo_GetsTheDarkPlate()
    {
        var px = Fill(32, SKColors.Transparent).Concat(Fill(32, new SKColor(250, 250, 250, 255))).ToArray();
        Assert.Equal(IconPlate.Dark, PublisherIconBitmaps.ClassifyPlate(px));
    }

    [Fact]
    public void TransparentMidToneLogo_GetsTheLightPlate()
    {
        var px = Fill(32, SKColors.Transparent).Concat(Fill(32, new SKColor(200, 30, 30, 255))).ToArray();
        Assert.Equal(IconPlate.Light, PublisherIconBitmaps.ClassifyPlate(px));
    }

    [Fact]
    public void FullyTransparentOrEmpty_DefaultsToLight()
    {
        Assert.Equal(IconPlate.Light, PublisherIconBitmaps.ClassifyPlate(Fill(64, SKColors.Transparent)));
        Assert.Equal(IconPlate.Light, PublisherIconBitmaps.ClassifyPlate([]));
    }
}
