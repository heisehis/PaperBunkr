using Paperbunkr.App.Models;

namespace Paperbunkr.App.Tests;

/// <summary>The bundled hero icons (docs/superpowers/specs/2026-11-06-collection-icons-and-slim-banner-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class HeroGlyphsTests
{
    [Fact]
    public void TheSet_Has24UniqueIcons()
    {
        Assert.Equal(24, HeroGlyphs.All.Count);
        Assert.Equal(24, HeroGlyphs.All.Select(g => g.Slug).Distinct().Count());
        Assert.Equal(24, HeroGlyphs.All.Select(g => g.Label).Distinct().Count());
    }

    [Fact]
    public void EveryPath_ParsesToAShapeOnTheCanvas()
    {
        foreach (var glyph in HeroGlyphs.All)
        {
            var bounds = glyph.Geometry.Bounds;
            Assert.True(bounds.Width > 100 && bounds.Height > 100, $"{glyph.Slug} is too small: {bounds}");
            // The view scales each shape to its own bounds (Stretch.Uniform); only a shape far off the 512 canvas would mean bad data.
            Assert.True(bounds.X > -64 && bounds.Y > -64 && bounds.Right < HeroGlyphs.CanvasSize + 64 && bounds.Bottom < HeroGlyphs.CanvasSize + 64, $"{glyph.Slug} is off the canvas: {bounds}");
        }
    }

    [Fact]
    public void EmblemLookalikes_AreNotInTheSet()
    {
        string[] excluded = { "american-shield", "bat-mask", "spider-mask", "masked-spider", "iron-mask", "lightning-mask", "guy-fawkes-mask", "thor-hammer" };
        Assert.DoesNotContain(HeroGlyphs.All, g => excluded.Contains(g.Slug));
    }
}
