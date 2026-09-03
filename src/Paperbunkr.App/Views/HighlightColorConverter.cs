using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>Solid (non-translucent) swatch color per <see cref="BookHighlightColor"/>, for the Highlights drawer's list rows and the color-palette popup - a different (opaque) rendering than the reading pane's own translucent in-text highlight fills (<c>BookReaderScreen.axaml.cs</c>'s <c>HighlightScript</c>, CSS classes <c>pb-color-*</c>), which need to sit under readable text.</summary>
public sealed class HighlightColorConverter : IValueConverter
{
    public static readonly HighlightColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is BookHighlightColor color
            ? new SolidColorBrush(color switch
            {
                BookHighlightColor.Yellow => Color.Parse("#FFD54F"),
                BookHighlightColor.Green => Color.Parse("#81C784"),
                BookHighlightColor.Blue => Color.Parse("#64B5F6"),
                BookHighlightColor.Pink => Color.Parse("#F06292"),
                _ => Colors.Gray,
            })
            : Brushes.Gray;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
