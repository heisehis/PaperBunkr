using System;
using System.Globalization;
using FluentIcons.Common;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="BoolToIconVariantConverter"/> - the nav rail's active-state-to-glyph-weight
/// mapping (docs/superpowers/specs/2026-09-08-home-navrail-visual-v2-design.md §1).
/// </summary>
public class BoolToIconVariantConverterTests
{
    [Fact]
    public void Convert_True_ReturnsFilled()
    {
        var result = BoolToIconVariantConverter.Instance.Convert(
            true, typeof(object), null, CultureInfo.InvariantCulture);
        Assert.Equal(IconVariant.Filled, result);
    }

    [Fact]
    public void Convert_False_ReturnsRegular()
    {
        var result = BoolToIconVariantConverter.Instance.Convert(
            false, typeof(object), null, CultureInfo.InvariantCulture);
        Assert.Equal(IconVariant.Regular, result);
    }

    [Fact]
    public void Convert_NonBoolInput_FallsBackToRegular()
    {
        var result = BoolToIconVariantConverter.Instance.Convert(
            "not a bool", typeof(object), null, CultureInfo.InvariantCulture);
        Assert.Equal(IconVariant.Regular, result);
    }

    [Fact]
    public void ConvertBack_Throws()
    {
        Assert.Throws<NotSupportedException>(() => BoolToIconVariantConverter.Instance.ConvertBack(
            null, typeof(object), null, CultureInfo.InvariantCulture));
    }
}
