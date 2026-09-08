using System;
using System.Globalization;
using Avalonia.Data.Converters;
using FluentIcons.Common;

namespace Paperbunkr.App.Views;

/// <summary>IsSchedulerPaused -> the bulk-action button's icon (docs/superpowers/specs/2026-09-08-automation-tasks-redesign-design.md).</summary>
public sealed class PauseResumeIconConverter : IValueConverter
{
    public static readonly PauseResumeIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Symbol.Play : Symbol.Pause;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>IsSchedulerPaused -> the bulk-action button's label.</summary>
public sealed class PauseResumeLabelConverter : IValueConverter
{
    public static readonly PauseResumeLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Resume all" : "Pause all";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
