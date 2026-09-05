using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Views;

/// <summary>
/// Resolves a card's <c>CoverKey</c> (a <see cref="CoverFingerprint.Stem"/> string) into its
/// decoded <see cref="Avalonia.Media.Imaging.Bitmap"/> on demand, at bind time
/// (docs/superpowers/specs/2026-08-22-cover-memory-virtualization-design.md) - the pairing that
/// makes <c>VirtualizingWrapPanel</c> actually save memory. A card whose container isn't realized
/// never has this converter invoked at all, so its cover is never decoded; one whose container is
/// recycled away stops being bound, so nothing outside <see cref="CoverImageCache"/>'s own bounded
/// LRU holds the decoded <c>Bitmap</c> alive afterward.
///
/// The key is a fingerprint stem rather than a bare issue id (docs/superpowers/specs/2026-08-27-
/// cover-thumbnail-identity-validation-design.md) so a cover cached against a since-reassigned id
/// is not served.
/// </summary>
public sealed class CoverImageConverter : IValueConverter
{
    public static readonly CoverImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string stem ? CoverImageCache.Get(stem) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Book-library counterpart of <see cref="CoverImageConverter"/>, backed by <see cref="BookCoverImageCache"/>.</summary>
public sealed class BookCoverImageConverter : IValueConverter
{
    public static readonly BookCoverImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string stem ? BookCoverImageCache.Get(stem) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
