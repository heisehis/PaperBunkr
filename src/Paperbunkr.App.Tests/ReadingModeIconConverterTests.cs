using System;
using System.Globalization;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ReadingModeIconConverter.KeyFor"/> - the pure six-modes-to-three-glyphs
/// mapping behind the reader's reading-direction pill icon
/// (docs/superpowers/specs/2026-08-27-reader-chrome-icon-pass-design.md §3). Tests the key
/// selection directly rather than the resolved <c>Geometry</c>: the test assembly bootstraps a bare
/// <c>Application</c> (see <c>TestAppBuilder</c>) with no App.axaml styles, so the geometry
/// resources aren't resolvable here - same pure-logic-extraction reasoning as
/// <c>VirtualizingWrapGridMathTests</c>.
/// </summary>
public class ReadingModeIconConverterTests
{
    [Theory]
    [InlineData(ReadingMode.LeftToRight, "PbIconArrowRight")]
    [InlineData(ReadingMode.HorizontalContinuous, "PbIconArrowRight")]
    [InlineData(ReadingMode.RightToLeft, "PbIconArrowLeft")]
    [InlineData(ReadingMode.HorizontalContinuousRightToLeft, "PbIconArrowLeft")]
    [InlineData(ReadingMode.TopToBottom, "PbIconArrowDown")]
    [InlineData(ReadingMode.VerticalContinuous, "PbIconArrowDown")]
    [InlineData(ReadingMode.Webtoon, "PbIconArrowDown")]
    public void KeyFor_MapsEachModeToItsDirectionGlyph(ReadingMode mode, string expectedKey)
    {
        Assert.Equal(expectedKey, ReadingModeIconConverter.KeyFor(mode));
    }

    [Fact]
    public void KeyFor_CoversEveryReadingModeValue()
    {
        // If a new ReadingMode is added, it silently falls through to the LTR glyph - this test is
        // the reminder to make a deliberate choice for it in KeyFor's switch.
        foreach (ReadingMode mode in Enum.GetValues<ReadingMode>())
        {
            var key = ReadingModeIconConverter.KeyFor(mode);
            Assert.Contains(key, new[] { "PbIconArrowRight", "PbIconArrowLeft", "PbIconArrowDown" });
        }
    }

    [Fact]
    public void KeyFor_UnrecognisedValue_FallsBackToLtrGlyph()
    {
        Assert.Equal("PbIconArrowRight", ReadingModeIconConverter.KeyFor((ReadingMode)999));
    }

    [Fact]
    public void Convert_WithNoRunningApp_ReturnsNullWithoutThrowing()
    {
        // No Application.Current in this plain xUnit context (no AvaloniaTestCollection) - the
        // converter must degrade to an empty Path, never crash the pill.
        var result = ReadingModeIconConverter.Instance.Convert(
            ReadingMode.RightToLeft, typeof(object), null, CultureInfo.InvariantCulture);
        Assert.Null(result);
    }

    [Fact]
    public void Convert_NonReadingModeInput_DoesNotThrow()
    {
        var result = ReadingModeIconConverter.Instance.Convert(
            "not a mode", typeof(object), null, CultureInfo.InvariantCulture);
        Assert.Null(result);
    }

    [Fact]
    public void ConvertBack_Throws()
    {
        Assert.Throws<NotSupportedException>(() => ReadingModeIconConverter.Instance.ConvertBack(
            null, typeof(object), null, CultureInfo.InvariantCulture));
    }
}
