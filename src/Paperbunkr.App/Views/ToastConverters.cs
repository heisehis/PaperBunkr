using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using FluentIcons.Common;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Views;

/// <summary>
/// Value converters for <see cref="PbToastView"/> (docs/superpowers/specs/2026-09-06-feedback-
/// notification-system-design.md §5). Same shape as <c>ActivityConverters.cs</c>'s
/// status/severity converters.
/// </summary>
public sealed class ToastSeverityIconConverter : IValueConverter
{
    public static readonly ToastSeverityIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ToastSeverity.Success => Symbol.CheckmarkCircle,
        ToastSeverity.Warning => Symbol.Warning,
        ToastSeverity.Error => Symbol.DismissCircle,
        _ => Symbol.Info,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ToastSeverityBrushConverter : IValueConverter
{
    public static readonly ToastSeverityBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value switch
        {
            ToastSeverity.Success => "PbSuccessBrush",
            ToastSeverity.Warning => "PbAccentTextBrush",
            ToastSeverity.Error => "PbDangerBrush",
            _ => "PbAccentTextBrush",
        };

        return Application.Current?.TryGetResource(key, null, out var brush) == true && brush is IBrush b ? b : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
