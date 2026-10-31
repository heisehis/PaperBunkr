using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Paperbunkr.App.Views.Reader;

/// <summary>The Aa sheet's theme swatch ring (books redesign §5.3, closing the "no active ring" gap): [the swatch's theme, the book's theme]
/// to the accent brush when they match, else transparent.</summary>
public sealed class ActiveSwatchRingConverter : IMultiValueConverter
{
    public static readonly ActiveSwatchRingConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        bool active = values.Count == 2 && values[0] is not null && Equals(values[0], values[1]);
        if (!active)
        {
            return Brushes.Transparent;
        }

        return Application.Current?.TryGetResource("PbAccentBrush", null, out object? accent) == true && accent is IBrush brush ? brush : Brushes.Orange;
    }
}

/// <summary>A book theme to its opaque page colour, for a reader panel's card (books redesign §5.3).</summary>
public sealed class ReaderPanelBackgroundConverter : IValueConverter
{
    public static readonly ReaderPanelBackgroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Paperbunkr.Data.Entities.BookTheme theme ? BookThemePanelResources.PanelBackground(theme) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Two-way "not": the sheet's "Keep toolbar visible" switch over the stored auto-hide setting (books redesign §5.2).</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    public static readonly InvertBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : value;
}
