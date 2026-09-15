using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Paperbunkr.App.Views;

/// <summary>
/// Sibling to <see cref="AccentColorToBrushConverter"/> for the sidebar active-row background tint
/// (docs/superpowers/specs/2026-09-14-library-visual-redesign-design.md §6) - same
/// <see cref="Paperbunkr.Data.Entities.Collection.AccentColor"/> hex string, parsed the same way,
/// but reduced to a fixed ~18% alpha rather than full opacity. A raw full-opacity fill would fight
/// text legibility against an arbitrary user-picked color; this matches the alpha byte this
/// codebase's own <c>PbAccentSoftColor</c> (<c>#29...</c>, ~16%) / <c>PbBadgeSoftColor</c>
/// (<c>#2E...</c>, ~18%) tokens already use for exactly this "tinted surface, not a solid fill"
/// purpose. The left accent border stays full opacity via <see cref="AccentColorToBrushConverter"/>
/// unchanged - a thin 3px bar isn't a text-bearing surface, so full saturation there is fine.
/// </summary>
public sealed class AccentColorToBackgroundTintConverter : IValueConverter
{
    public static readonly AccentColorToBackgroundTintConverter Instance = new();

    private const byte TintAlpha = 0x2E;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out var color))
        {
            return new SolidColorBrush(Color.FromArgb(TintAlpha, color.R, color.G, color.B));
        }

        return Application.Current?.TryGetResource("PbAccentSoftBrush", null, out var fallback) == true && fallback is IBrush brush
            ? brush
            : Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
