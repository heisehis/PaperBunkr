using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Paperbunkr.App.Views;

/// <summary>
/// Parses a <see cref="Paperbunkr.Data.Entities.Collection.AccentColor"/> hex string
/// (<c>#RRGGBB</c>) into a brush for the sidebar dot and Detail chips
/// (docs/superpowers/specs/2026-08-27-collections-design.md). Null, blank, or unparseable falls
/// back to the app's own generic accent so an unset collection still reads as "accented", not
/// broken. Static <see cref="Instance"/> + <c>ConvertBack</c>-throws, mirroring
/// <see cref="ReadingModeIconConverter"/>.
/// </summary>
public sealed class AccentColorToBrushConverter : IValueConverter
{
    public static readonly AccentColorToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out var color))
        {
            return new SolidColorBrush(color);
        }

        return Application.Current?.TryGetResource("PbAccentTextBrush", null, out var fallback) == true && fallback is IBrush brush
            ? brush
            : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
