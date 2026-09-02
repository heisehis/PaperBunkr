using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Paperbunkr.App.Views;

/// <summary>
/// Two small double-to-<see cref="Thickness"/> converters backing docs/superpowers/specs/
/// 2026-09-01-books-reader-ergonomics-and-annotations-design.md's paragraph-spacing and page-margin
/// settings - Avalonia has no direct way to bind a single side of a Thickness, so BookReaderScreen.axaml
/// converts the underlying double settings itself. Static <c>Instance</c> + <c>ConvertBack</c>-throws,
/// same shape as <see cref="ReadingModeIconConverter"/>.
/// </summary>
public sealed class ParagraphBottomMarginConverter : IValueConverter
{
    public static readonly ParagraphBottomMarginConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new Thickness(0, 0, 0, value is double d ? d : 10);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Left/right reading-column padding from <c>Settings.PageMargin</c>, top/bottom kept at the reader's
/// existing fixed chrome-clearance values (70 top for the toolbar, 60 bottom for the progress bar) -
/// those aren't part of the "page margin" setting, which is about the reading column's horizontal
/// width, not chrome clearance.
/// </summary>
public sealed class PageMarginConverter : IValueConverter
{
    public static readonly PageMarginConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double margin = value is double d ? d : 40;
        return new Thickness(margin, 70, margin, 60);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
