using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Paperbunkr.App.Views;

/// <summary>
/// Maps a Reading Activity trend tile's <c>IsGood</c> bool (docs/superpowers/specs/2026-09-22-insights-
/// period-over-period-deltas-design.md) to the success/danger brush its delta arrow and percent text render
/// with. Static <see cref="Instance"/> + <c>ConvertBack</c>-throws, mirroring <see cref="AccentColorToBrushConverter"/>.
/// </summary>
public sealed class TrendBrushConverter : IValueConverter
{
    public static readonly TrendBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value is true ? "PbSuccessBrush" : "PbDangerBrush";
        return Application.Current?.TryGetResource(key, null, out var brush) == true && brush is IBrush b
            ? b
            : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
