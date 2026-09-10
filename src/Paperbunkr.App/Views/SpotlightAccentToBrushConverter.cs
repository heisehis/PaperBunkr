using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Paperbunkr.App.Views;

/// <summary>
/// Turns <see cref="Paperbunkr.App.ViewModels.HomeScreenViewModel.SpotlightAccentColor"/> into a
/// translucent wash brush for the Home masthead's ambient-recolor overlay (docs/superpowers/specs/
/// 2026-09-08-home-navrail-visual-v2-design.md §4) - a solid color at ~30% opacity layered over the
/// existing scrim, so it blends toward the spotlight's cover tone without ever fully replacing the
/// active skin's own palette underneath. Static <see cref="Instance"/> + <c>ConvertBack</c>-throws,
/// mirroring <see cref="ReadingModeIconConverter"/>.
/// </summary>
public sealed class SpotlightAccentToBrushConverter : IValueConverter
{
    public static readonly SpotlightAccentToBrushConverter Instance = new();

    /// <summary>A wash, not a recolor - bumped from the design doc's original "~30%" after the first
    /// pass read as imperceptible in practice (docs/superpowers/specs/2026-09-08-home-navrail-
    /// visual-v2-design.md §4's own open question about calibration).</summary>
    private const double BlendOpacity = 0.42;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new SolidColorBrush(value is Color color ? color : Colors.Transparent, BlendOpacity);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
