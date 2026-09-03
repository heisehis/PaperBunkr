using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>
/// XAML-bindable wrapper around <see cref="BookThemeBrushes.ChromeBackground"/> (docs/superpowers/
/// specs/2026-09-03-books-reader-hud-redesign-design.md) - for <c>ReaderChrome</c>/
/// <c>ReaderSettingsSheet</c>'s <c>ChromeBackground</c> binding and PDF's canvas-backdrop binding.
/// Top-level (not nested inside a wrapping class) so it's referenceable via a plain
/// <c>{x:Static views:ReaderChromeBackgroundConverter.Instance}</c> in XAML, same static-Instance-
/// converter shape <see cref="HighlightColorConverter"/> already uses in this project.
/// </summary>
public sealed class ReaderChromeBackgroundConverter : IValueConverter
{
    public static readonly ReaderChromeBackgroundConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is BookTheme theme ? BookThemeBrushes.ChromeBackground(theme) : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>XAML-bindable wrapper around <see cref="BookThemeBrushes.ChromeForeground"/> - see <see cref="ReaderChromeBackgroundConverter"/>'s doc comment.</summary>
public sealed class ReaderChromeForegroundConverter : IValueConverter
{
    public static readonly ReaderChromeForegroundConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is BookTheme theme ? BookThemeBrushes.ChromeForeground(theme) : Brushes.White;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
