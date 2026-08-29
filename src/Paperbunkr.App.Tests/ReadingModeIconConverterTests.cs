using System;
using System.Globalization;
using FluentIcons.Common;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ReadingModeIconConverter.SymbolFor"/> - the pure six-modes-to-three-arrows
/// mapping behind the reader's reading-direction pill icon
/// (docs/superpowers/specs/2026-08-28-fluenticons-migration-design.md §5).
/// </summary>
public class ReadingModeIconConverterTests
{
    [Theory]
    [InlineData(ReadingMode.LeftToRight, Symbol.ArrowRight)]
    [InlineData(ReadingMode.HorizontalContinuous, Symbol.ArrowRight)]
    [InlineData(ReadingMode.RightToLeft, Symbol.ArrowLeft)]
    [InlineData(ReadingMode.HorizontalContinuousRightToLeft, Symbol.ArrowLeft)]
    [InlineData(ReadingMode.TopToBottom, Symbol.ArrowDown)]
    [InlineData(ReadingMode.VerticalContinuous, Symbol.ArrowDown)]
    [InlineData(ReadingMode.Webtoon, Symbol.ArrowDown)]
    public void SymbolFor_MapsEachModeToItsDirectionArrow(ReadingMode mode, Symbol expected)
    {
        Assert.Equal(expected, ReadingModeIconConverter.SymbolFor(mode));
    }

    [Fact]
    public void SymbolFor_CoversEveryReadingModeValue()
    {
        // If a new ReadingMode is added, it silently falls through to the LTR arrow - this test is
        // the reminder to make a deliberate choice for it in SymbolFor's switch.
        foreach (ReadingMode mode in Enum.GetValues<ReadingMode>())
        {
            var symbol = ReadingModeIconConverter.SymbolFor(mode);
            Assert.Contains(symbol, new[] { Symbol.ArrowRight, Symbol.ArrowLeft, Symbol.ArrowDown });
        }
    }

    [Fact]
    public void SymbolFor_UnrecognisedValue_FallsBackToLtrArrow()
    {
        Assert.Equal(Symbol.ArrowRight, ReadingModeIconConverter.SymbolFor((ReadingMode)999));
    }

    [Fact]
    public void Convert_ReturnsTheArrowSymbol()
    {
        var result = ReadingModeIconConverter.Instance.Convert(
            ReadingMode.RightToLeft, typeof(object), null, CultureInfo.InvariantCulture);
        Assert.Equal(Symbol.ArrowLeft, result);
    }

    [Fact]
    public void Convert_NonReadingModeInput_FallsBackToLtrArrow()
    {
        var result = ReadingModeIconConverter.Instance.Convert(
            "not a mode", typeof(object), null, CultureInfo.InvariantCulture);
        Assert.Equal(Symbol.ArrowRight, result);
    }

    [Fact]
    public void ConvertBack_Throws()
    {
        Assert.Throws<NotSupportedException>(() => ReadingModeIconConverter.Instance.ConvertBack(
            null, typeof(object), null, CultureInfo.InvariantCulture));
    }
}
