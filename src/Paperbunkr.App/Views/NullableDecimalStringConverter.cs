using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Paperbunkr.App.Views;

/// <summary>
/// Bridges a <c>string</c> ViewModel property (the metadata editors buffer every field as text -
/// docs/superpowers/specs/2026-08-07-issue-properties-editor-design.md §3) to
/// <see cref="Avalonia.Controls.NumericUpDown.Value"/>, which is <c>decimal?</c>. Empty / whitespace /
/// unparseable text maps to <c>null</c> (a blank field); <c>null</c> maps back to an empty string.
/// Invariant parsing/formatting, integer display. Static <c>Instance</c>, same shape as the other
/// converters in this folder.
/// </summary>
public sealed class NullableDecimalStringConverter : IValueConverter
{
    public static readonly NullableDecimalStringConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d)
            ? d
            : (decimal?)null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            decimal d => decimal.Truncate(d).ToString(CultureInfo.InvariantCulture),
            _ => string.Empty,
        };
}
