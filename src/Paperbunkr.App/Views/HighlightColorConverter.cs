using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>Solid swatch colour per <see cref="BookHighlightColor"/> for the popup swatches and the Notes lists, read from the
/// <c>PbHighlight*Color</c> tokens (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §5.1). The page's own in-text fills use the
/// same colours at 55% alpha (<see cref="CssRgba"/>), so text stays readable. The fallbacks are the token values, for a host with no resources.</summary>
public sealed class HighlightColorConverter : IValueConverter
{
    public static readonly HighlightColorConverter Instance = new();

    public static Color ColorOf(BookHighlightColor color)
    {
        (string key, string fallback) = color switch
        {
            BookHighlightColor.Yellow => ("PbHighlightYellowColor", "#FFD54F"),
            BookHighlightColor.Green => ("PbHighlightGreenColor", "#81C784"),
            BookHighlightColor.Blue => ("PbHighlightBlueColor", "#64B5F6"),
            _ => ("PbHighlightPinkColor", "#F06292"),
        };
        return Application.Current?.TryGetResource(key, null, out object? found) == true && found is Color token ? token : Color.Parse(fallback);
    }

    /// <summary>"rgba(255, 213, 79, 0.55)" for the page CSS.</summary>
    public static string CssRgba(BookHighlightColor color, double alpha)
    {
        var c = ColorOf(color);
        return string.Create(CultureInfo.InvariantCulture, $"rgba({c.R}, {c.G}, {c.B}, {alpha})");
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is BookHighlightColor color ? new SolidColorBrush(ColorOf(color)) : Brushes.Gray;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
