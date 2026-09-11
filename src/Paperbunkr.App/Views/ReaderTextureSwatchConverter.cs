using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.Views;

/// <summary>
/// Renders a <c>ReaderBackgroundTextures</c> swatch preview for the Preferences → Reader texture
/// picker (docs/superpowers/specs/2026-09-10-reader-backlog-batch-b-design.md Item 1) - same
/// "ignore the bound value, take the id from ConverterParameter" idiom as a literal per-swatch
/// binding, since each swatch is a fixed texture id, not a bound property. <see cref="BookThemeSwatchConverter"/>
/// is the precedent for a static-swatch-brush converter in this codebase, just keyed off the bound
/// value instead of a parameter there.
/// </summary>
public sealed class ReaderTextureSwatchConverter : IValueConverter
{
    public static readonly ReaderTextureSwatchConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string id)
        {
            return Brushes.Transparent;
        }

        try
        {
            var bitmap = ReaderBackgroundTextures.LoadBitmap(id);
            // A small filled preview swatch, not tiled - UniformToFill crops to the swatch's own
            // aspect ratio rather than tiling/stretching the whole bitmap.
            return new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill }.ToImmutable();
        }
        catch (Exception)
        {
            return Brushes.Transparent;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
