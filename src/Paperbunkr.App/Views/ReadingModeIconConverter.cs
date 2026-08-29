using System;
using System.Globalization;
using Avalonia.Data.Converters;
using FluentIcons.Common;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>
/// Maps the reader's effective <see cref="ReadingMode"/> to a direction glyph
/// (docs/superpowers/specs/2026-08-28-fluenticons-migration-design.md §5) so the reading-mode
/// picker pill shows a right / left / down arrow for the current flow. Static <see cref="Instance"/>
/// + <c>ConvertBack</c>-throws, mirroring <see cref="CoverImageConverter"/>.
///
/// Returns a <see cref="Symbol"/> that a <c>fi:SymbolIcon</c>'s <c>Symbol</c> binds to directly -
/// no resource lookup, so <see cref="SymbolFor"/> is a pure switch testable without a running
/// Avalonia app.
/// </summary>
public sealed class ReadingModeIconConverter : IValueConverter
{
    public static readonly ReadingModeIconConverter Instance = new();

    /// <summary>The six <see cref="ReadingMode"/> values collapse to three direction arrows.</summary>
    internal static Symbol SymbolFor(ReadingMode mode) => mode switch
    {
        ReadingMode.RightToLeft or ReadingMode.HorizontalContinuousRightToLeft => Symbol.ArrowLeft,
        ReadingMode.TopToBottom or ReadingMode.VerticalContinuous or ReadingMode.Webtoon => Symbol.ArrowDown,
        // LeftToRight, HorizontalContinuous, and anything unrecognised fall through to the LTR arrow.
        _ => Symbol.ArrowRight,
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        SymbolFor(value is ReadingMode mode ? mode : ReadingMode.LeftToRight);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
