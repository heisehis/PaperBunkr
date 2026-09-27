using Paperbunkr.App.Controls;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="BrandMark"/> resolves each family to sane computed outputs without throwing
/// (docs/superpowers/specs/2026-08-28-brand-metadata-iconography-design.md / plan Step 5). The
/// headless test app has no App.axaml styles, so this checks the control's resolver-driven state
/// (kind, image, label) rather than a full layout pass - which is what actually drives the template.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BrandMarkRenderSmokeTests
{
    private static BrandMark Make(MarkFamily family, string value) =>
        new() { Family = family, Value = value, ShowText = true, MarkSize = 16 };

    [Theory]
    [InlineData(MarkFamily.Service, "AniList", MarkKind.SvgAsset)]
    [InlineData(MarkFamily.Service, "ReadThingsRight", MarkKind.LetterMark)]
    [InlineData(MarkFamily.Publisher, "Marvel", MarkKind.SvgAsset)]
    [InlineData(MarkFamily.Publisher, "DSTLRY", MarkKind.LetterMark)]
    [InlineData(MarkFamily.Publisher, "Vertigo", MarkKind.Raster)]
    [InlineData(MarkFamily.Publisher, "Nobody's Press", MarkKind.Text)]
    [InlineData(MarkFamily.Format, "Trade Paperback", MarkKind.SvgAsset)]
    [InlineData(MarkFamily.Format, "Annual", MarkKind.SvgAsset)]
    [InlineData(MarkFamily.Format, "EPUB", MarkKind.Glyph)]
    [InlineData(MarkFamily.AgeRating, "Teen", MarkKind.SvgAsset)]
    [InlineData(MarkFamily.AgeRating, "MA15+", MarkKind.LetterMark)]
    [InlineData(MarkFamily.Language, "ja", MarkKind.Flag)]
    [InlineData(MarkFamily.Language, "eo", MarkKind.Text)]
    [InlineData(MarkFamily.ReadingStatus, "Reading", MarkKind.Glyph)]
    [InlineData(MarkFamily.ReadingStatus, "Completed", MarkKind.Glyph)]
    [InlineData(MarkFamily.ReadingStatus, "Unknown", MarkKind.None)]
    [InlineData(MarkFamily.ScanGroup, "TCB Scans", MarkKind.Glyph)]
    public void EachFamily_ResolvesToTheExpectedKind(MarkFamily family, string value, MarkKind expected)
    {
        var mark = Make(family, value);
        Assert.Equal(expected, mark.ResolvedKind);

        switch (expected)
        {
            case MarkKind.SvgAsset or MarkKind.Flag:
                Assert.True(mark.IsImage);
                Assert.NotNull(mark.ImageSource);
                break;
            case MarkKind.Raster:
                Assert.True(mark.IsRaster);
                Assert.False(mark.IsImage);
                Assert.NotNull(mark.ImageSource);
                Assert.True(mark.ShowLabel);
                break;
            case MarkKind.LetterMark:
                Assert.True(mark.IsChip);
                Assert.False(string.IsNullOrWhiteSpace(mark.Label));
                break;
            case MarkKind.Glyph:
                Assert.True(mark.IsGlyph);
                break;
            case MarkKind.Text:
                Assert.True(mark.IsPlainText);
                Assert.False(string.IsNullOrWhiteSpace(mark.Label));
                break;
        }
    }

    [Fact]
    public void Publisher_YearAndMonth_SwitchTheEraLogo()
    {
        var mark = Make(MarkFamily.Publisher, "DC Comics");
        Assert.Equal(MarkKind.SvgAsset, mark.ResolvedKind);          // no year: the curated SVG

        mark.Year = 1990;
        Assert.Equal(MarkKind.Raster, mark.ResolvedKind);
        var era1990 = mark.ImageSource;
        Assert.NotNull(era1990);

        mark.Year = 2024;
        mark.Month = 12;
        Assert.Equal(MarkKind.Raster, mark.ResolvedKind);
        Assert.NotSame(era1990, mark.ImageSource);                    // the December-2024 logo, not the 1990 one

        mark.Year = null;
        Assert.Equal(MarkKind.SvgAsset, mark.ResolvedKind);
    }

    [Fact]
    public void MaxMarkWidth_ShrinksAWideLogo_ButNotASquareOne()
    {
        // Uncapped: every logo is drawn MarkSize tall (the behaviour everywhere except cover tiles).
        var wide = new BrandMark { Family = MarkFamily.Publisher, Value = "Titan Comics", MarkSize = 24 };
        Assert.Equal(24, wide.MarkHeight);

        wide.MaxMarkWidth = 48;
        Assert.InRange(wide.MarkHeight, 1, 23.9);
        var size = wide.ImageSource!.Size;
        Assert.True(wide.MarkHeight * size.Width / size.Height <= 48.001, "drawn width must respect the cap");

        // A near-square logo (DC 2012 raster) fits inside 48 at full height.
        var square = new BrandMark { Family = MarkFamily.Publisher, Value = "DC Comics", Year = 2012, MarkSize = 22, MaxMarkWidth = 48 };
        Assert.Equal(22, square.MarkHeight, 3);
    }

    [Theory]
    [InlineData(24, 48, 100, 100, 24)]   // square: unchanged
    [InlineData(24, 48, 300, 100, 16)]   // 3:1 -> 48 wide needs 16 tall
    [InlineData(24, double.PositiveInfinity, 300, 100, 24)]
    [InlineData(24, 48, 0, 0, 24)]       // unmeasurable image -> MarkSize
    public void FitHeight_ClampsByAspectRatio(double markSize, double maxWidth, double w, double h, double expected)
    {
        var image = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(Math.Max(1, (int)w), Math.Max(1, (int)h)));
        double actual = w == 0 ? BrandMark.FitHeight(null, markSize, maxWidth) : BrandMark.FitHeight(image, markSize, maxWidth);
        Assert.Equal(expected, actual, 3);
    }

    [Fact]
    public void EmptyValue_RendersNothing()
    {
        var mark = Make(MarkFamily.Publisher, "");
        Assert.Equal(MarkKind.None, mark.ResolvedKind);
        Assert.False(mark.IsImage || mark.IsChip || mark.IsGlyph || mark.IsPlainText);
    }

    [Fact]
    public void ReadingStatus_Reading_IsGlyphWithLabelAndColour()
    {
        var mark = Make(MarkFamily.ReadingStatus, "Reading");
        Assert.True(mark.IsGlyph);
        Assert.Equal("Reading", mark.Label);
        Assert.True(mark.ShowLabel);
        Assert.NotNull(mark.GlyphBrush); // per-status #hex colour, not the inherited brush default path only
    }

    [Fact]
    public void ScanGroup_IsGlyphWithGroupName()
    {
        var mark = Make(MarkFamily.ScanGroup, "  TCB Scans  ");
        Assert.True(mark.IsGlyph);
        Assert.Equal("TCB Scans", mark.Label);
    }
}
