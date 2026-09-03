using System;
using System.Globalization;
using Avalonia.Media;
using Paperbunkr.App.Models;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="BookThemeBrushes"/> (the single per-<see cref="BookTheme"/> brush table both
/// <c>BookReaderSettings.Background</c>/<c>Foreground</c> and the reader chrome's translucent tint
/// delegate to) and its <see cref="ReaderChromeTint"/> XAML converter wrappers
/// (docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-design.md).
/// </summary>
public class ReaderChromeTintTests
{
    // HighContrast/OledBlack/Light route through Avalonia's static Brushes.* (ImmutableSolidColorBrush)
    // while the others are constructed SolidColorBrush instances - both implement ISolidColorBrush, so
    // assert against that interface rather than either concrete type.
    private static Color ColorOf(IBrush brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [Theory]
    [InlineData(BookTheme.Light)]
    [InlineData(BookTheme.Dark)]
    [InlineData(BookTheme.Sepia)]
    [InlineData(BookTheme.OledBlack)]
    [InlineData(BookTheme.HighContrast)]
    [InlineData(BookTheme.MatchAppSkin)]
    public void ChromeBackground_IsDeterministic(BookTheme theme)
    {
        var first = ColorOf(BookThemeBrushes.ChromeBackground(theme));
        var second = ColorOf(BookThemeBrushes.ChromeBackground(theme));
        Assert.Equal(first, second);
    }

    [Fact]
    public void ChromeBackground_MatchAppSkin_MatchesThePreRedesignHardcodedChromeColor()
    {
        // The reader chrome bars were hardcoded to #CC14161B before this redesign - MatchAppSkin
        // (the default BookReaderSettings.Theme) must keep that exact look, not just something close.
        Assert.Equal(Color.Parse("#CC14161B"), ColorOf(BookThemeBrushes.ChromeBackground(BookTheme.MatchAppSkin)));
    }

    [Fact]
    public void ChromeBackground_IsTranslucentForEveryTheme()
    {
        foreach (BookTheme theme in Enum.GetValues<BookTheme>())
        {
            byte alpha = ColorOf(BookThemeBrushes.ChromeBackground(theme)).A;
            Assert.True(alpha < 255, $"{theme}'s chrome background should be translucent, was fully opaque (A={alpha}).");
        }
    }

    [Theory]
    [InlineData(BookTheme.Light)]
    [InlineData(BookTheme.Dark)]
    [InlineData(BookTheme.Sepia)]
    [InlineData(BookTheme.OledBlack)]
    [InlineData(BookTheme.HighContrast)]
    [InlineData(BookTheme.MatchAppSkin)]
    public void ChromeForeground_MatchesContentForeground(BookTheme theme)
    {
        // Deliberate per the design: chrome text reuses the theme's own already-contrast-checked
        // content foreground rather than a second, parallel color table.
        Assert.Equal(ColorOf(BookThemeBrushes.ContentForeground(theme)), ColorOf(BookThemeBrushes.ChromeForeground(theme)));
    }

    [Fact]
    public void ContentBackground_CoversEveryThemeValue()
    {
        foreach (BookTheme theme in Enum.GetValues<BookTheme>())
        {
            // Throws (unhandled switch arm) if a theme was ever added without a deliberate choice here.
            _ = BookThemeBrushes.ContentBackground(theme);
            _ = BookThemeBrushes.ContentForeground(theme);
            _ = BookThemeBrushes.ChromeBackground(theme);
        }
    }

    [Fact]
    public void BackgroundConverter_Convert_ReturnsChromeBackgroundForTheTheme()
    {
        var result = ReaderChromeBackgroundConverter.Instance.Convert(
            BookTheme.Sepia, typeof(object), null, CultureInfo.InvariantCulture);
        Assert.Equal(ColorOf(BookThemeBrushes.ChromeBackground(BookTheme.Sepia)), ColorOf(Assert.IsAssignableFrom<IBrush>(result)));
    }

    [Fact]
    public void BackgroundConverter_NonThemeInput_FallsBackToTransparent()
    {
        var result = ReaderChromeBackgroundConverter.Instance.Convert(
            "not a theme", typeof(object), null, CultureInfo.InvariantCulture);
        Assert.Equal(Brushes.Transparent, result);
    }

    [Fact]
    public void ForegroundConverter_Convert_ReturnsChromeForegroundForTheTheme()
    {
        var result = ReaderChromeForegroundConverter.Instance.Convert(
            BookTheme.Dark, typeof(object), null, CultureInfo.InvariantCulture);
        Assert.Equal(ColorOf(BookThemeBrushes.ChromeForeground(BookTheme.Dark)), ColorOf(Assert.IsAssignableFrom<IBrush>(result)));
    }

    [Fact]
    public void ConvertBack_Throws()
    {
        Assert.Throws<NotSupportedException>(() => ReaderChromeBackgroundConverter.Instance.ConvertBack(
            null, typeof(object), null, CultureInfo.InvariantCulture));
        Assert.Throws<NotSupportedException>(() => ReaderChromeForegroundConverter.Instance.ConvertBack(
            null, typeof(object), null, CultureInfo.InvariantCulture));
    }
}
