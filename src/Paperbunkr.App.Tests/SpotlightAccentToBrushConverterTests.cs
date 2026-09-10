using System;
using System.Globalization;
using Avalonia.Media;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="SpotlightAccentToBrushConverter"/> - the Home masthead's ambient-recolor
/// wash (docs/superpowers/specs/2026-09-08-home-navrail-visual-v2-design.md §4).
/// </summary>
public class SpotlightAccentToBrushConverterTests
{
    [Fact]
    public void Convert_Color_ReturnsSolidBrushAtThatColor_WithBlendOpacity()
    {
        var color = Color.FromRgb(0xC9, 0x80, 0x3F);

        var result = SpotlightAccentToBrushConverter.Instance.Convert(
            color, typeof(object), null, CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(color, brush.Color);
        Assert.Equal(0.42, brush.Opacity);
    }

    [Fact]
    public void Convert_NonColorInput_FallsBackToTransparent()
    {
        var result = SpotlightAccentToBrushConverter.Instance.Convert(
            "not a color", typeof(object), null, CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Colors.Transparent, brush.Color);
    }

    [Fact]
    public void ConvertBack_Throws()
    {
        Assert.Throws<NotSupportedException>(() => SpotlightAccentToBrushConverter.Instance.ConvertBack(
            null, typeof(object), null, CultureInfo.InvariantCulture));
    }
}
