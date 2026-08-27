using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Views;

/// <summary>
/// Resolves an <c>int?</c> cover-issue id into its decoded <see cref="Avalonia.Media.Imaging.Bitmap"/>
/// on demand, at bind time (docs/superpowers/specs/2026-08-22-cover-memory-virtualization-design.md) -
/// the pairing that makes <c>VirtualizingWrapPanel</c> actually save memory. A card whose container
/// isn't realized never has this converter invoked at all, so its cover is never decoded; one whose
/// container is recycled away stops being bound, so nothing outside <see cref="CoverImageCache"/>'s
/// own bounded LRU holds the decoded <c>Bitmap</c> alive afterward.
/// </summary>
public sealed class CoverImageConverter : IValueConverter
{
    public static readonly CoverImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int issueId ? CoverImageCache.Get(issueId) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Book-library counterpart of <see cref="CoverImageConverter"/>, backed by <see cref="BookCoverImageCache"/>.</summary>
public sealed class BookCoverImageConverter : IValueConverter
{
    public static readonly BookCoverImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int bookId ? BookCoverImageCache.Get(bookId) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
