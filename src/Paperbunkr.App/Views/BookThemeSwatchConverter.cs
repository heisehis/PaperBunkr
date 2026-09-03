using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>Opaque swatch color per <see cref="BookTheme"/> for <c>ReaderSettingsSheet</c>'s theme picker (docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-design.md) - <see cref="BookThemeBrushes.ContentBackground"/> directly, the same values the Font &amp; Theme sheet's swatches already used before this redesign.</summary>
public sealed class BookThemeSwatchConverter : IValueConverter
{
    public static readonly BookThemeSwatchConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is BookTheme theme ? BookThemeBrushes.ContentBackground(theme) : Brushes.Gray;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
