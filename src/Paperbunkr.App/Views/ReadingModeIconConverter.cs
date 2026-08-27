using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>
/// Maps the reader's effective <see cref="ReadingMode"/> to a direction glyph
/// (docs/superpowers/specs/2026-08-27-reader-chrome-icon-pass-design.md §3) so the reading-mode
/// picker pill shows →, ←, or ↓ for the current flow. Static <see cref="Instance"/> +
/// <c>ConvertBack</c>-throws, mirroring <see cref="CoverImageConverter"/> - the project keeps its
/// converters here in <c>Views/</c> rather than a dedicated folder.
///
/// Key selection is split into the pure <see cref="KeyFor"/> so it's testable without a running
/// Avalonia app (the test collection bootstraps a bare <c>Application</c> with no App.axaml styles,
/// so the geometry resources aren't resolvable there) - same pure-logic-extraction pattern as
/// <c>ZoomPanMath</c> / <c>VirtualizingWrapGridMath</c>.
/// </summary>
public sealed class ReadingModeIconConverter : IValueConverter
{
    public static readonly ReadingModeIconConverter Instance = new();

    /// <summary>The six <see cref="ReadingMode"/> values collapse to three direction glyphs.</summary>
    internal static string KeyFor(ReadingMode mode) => mode switch
    {
        ReadingMode.RightToLeft or ReadingMode.HorizontalContinuousRightToLeft => "PbIconArrowLeft",
        ReadingMode.TopToBottom or ReadingMode.VerticalContinuous or ReadingMode.Webtoon => "PbIconArrowDown",
        // LeftToRight, HorizontalContinuous, and anything unrecognised fall through to the LTR glyph.
        _ => "PbIconArrowRight",
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = KeyFor(value is ReadingMode mode ? mode : ReadingMode.LeftToRight);
        try
        {
            if (Application.Current is { } app
                && app.TryGetResource(key, app.ActualThemeVariant, out var res)
                && res is Geometry geometry)
            {
                return geometry;
            }
        }
        catch (InvalidOperationException)
        {
            // Application.Current exists but is owned by another thread - happens in a plain xUnit
            // test that runs after an Avalonia-headless collection has claimed the static instance.
            // The pill degrades to an empty Path, same as the no-app case below.
        }

        // Resource not resolvable (very early startup, or a test app with no styles) - the Path just
        // renders empty rather than the pill crashing.
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
