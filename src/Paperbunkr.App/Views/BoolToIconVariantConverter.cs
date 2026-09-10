using System;
using System.Globalization;
using Avalonia.Data.Converters;
using FluentIcons.Common;

namespace Paperbunkr.App.Views;

/// <summary>
/// Maps a nav-rail button's own active-state bool (e.g. <c>IsLibrary</c>) to its
/// <c>fi:SymbolIcon</c>'s <see cref="IconVariant"/> (docs/superpowers/specs/
/// 2026-09-08-home-navrail-visual-v2-design.md §1) - active items render <see cref="IconVariant.Filled"/>,
/// everything else stays <see cref="IconVariant.Regular"/>. Static <see cref="Instance"/> +
/// <c>ConvertBack</c>-throws, mirroring <see cref="ReadingModeIconConverter"/>.
/// </summary>
public sealed class BoolToIconVariantConverter : IValueConverter
{
    public static readonly BoolToIconVariantConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? IconVariant.Filled : IconVariant.Regular;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
