using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Views;

/// <summary>
/// Loads a <see cref="Bitmap"/> directly from a file path string - for the PDF reader's Captures
/// drawer thumbnails (docs/superpowers/specs/2026-09-01-books-reader-ergonomics-and-annotations-
/// design.md §"PDF area capture"), a different case from <see cref="CoverImageConverter"/>/
/// <see cref="BookCoverImageConverter"/> (both id-keyed against a bounded LRU cache) - captures are
/// few in number per book, not thousands like covers across a whole library, so no cache is needed.
/// </summary>
public sealed class FilePathImageConverter : IValueConverter
{
    public static readonly FilePathImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
