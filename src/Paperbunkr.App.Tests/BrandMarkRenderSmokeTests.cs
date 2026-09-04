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
    [InlineData(MarkFamily.Publisher, "Vertigo", MarkKind.LetterMark)]
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
