using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using FluentIcons.Common;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>
/// Value converters for the Activity Center rows (docs/superpowers/specs/2026-09-03-activity-
/// center-design.md). Status/severity are always paired with a text label in the row templates -
/// these only pick the accompanying colour/icon, never convey state on their own. Static
/// <c>Instance</c> + <c>ConvertBack</c>-throws, mirroring <see cref="AccentColorToBrushConverter"/>.
/// </summary>
public sealed class ActivityStatusBrushConverter : IValueConverter
{
    public static readonly ActivityStatusBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value switch
        {
            ActivityJobStatus.Succeeded => "PbSuccessBrush",
            ActivityJobStatus.Failed => "PbDangerBrush",
            ActivityJobStatus.Cancelled => "PbTextFaintBrush",
            ActivityJobStatus.Running => "PbAccentTextBrush",
            ActivityRunStatus.Succeeded => "PbSuccessBrush",
            ActivityRunStatus.Failed => "PbDangerBrush",
            ActivityRunStatus.Cancelled => "PbTextFaintBrush",
            ActivityRunStatus.Interrupted => "PbTextFaintBrush",
            ActivityAlertSeverity.Error => "PbDangerBrush",
            ActivityAlertSeverity.Warning => "PbAccentTextBrush",
            ActivityAlertSeverity.Info => "PbAccentTextBrush",
            _ => "PbTextMutedBrush",
        };

        return Application.Current?.TryGetResource(key, null, out var brush) == true && brush is IBrush b ? b : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ActivityStatusLabelConverter : IValueConverter
{
    public static readonly ActivityStatusLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ActivityJobStatus.Queued => "Waiting",
        ActivityJobStatus.Running => "Running",
        ActivityJobStatus.Succeeded => "Done",
        ActivityJobStatus.Failed => "Failed",
        ActivityJobStatus.Cancelled => "Cancelled",
        ActivityRunStatus.Succeeded => "Done",
        ActivityRunStatus.Failed => "Failed",
        ActivityRunStatus.Cancelled => "Cancelled",
        ActivityRunStatus.Interrupted => "Interrupted",
        _ => value?.ToString() ?? "",
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ActivityKindIconConverter : IValueConverter
{
    public static readonly ActivityKindIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ActivityJobKind.LibraryScan or ActivityJobKind.BookScan => Symbol.FolderOpen,
        ActivityJobKind.GenerateCovers => Symbol.Image,
        ActivityJobKind.SyncMetadata or ActivityJobKind.TrackerFetch or ActivityJobKind.Scrape => Symbol.ArrowClockwise,
        ActivityJobKind.Import => Symbol.ArrowDownload,
        ActivityJobKind.Update => Symbol.CloudArrowUp,
        ActivityJobKind.Migration => Symbol.Layer,
        ActivityJobKind.Upkeep => Symbol.Settings,
        _ => Symbol.Info,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ActivitySeverityIconConverter : IValueConverter
{
    public static readonly ActivitySeverityIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ActivityAlertSeverity.Error => Symbol.DismissCircle,
        ActivityAlertSeverity.Warning => Symbol.Warning,
        _ => Symbol.Info,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Compact "2 min ago" / "yesterday" relative time for finished-job + history rows.</summary>
public sealed class RelativeTimeConverter : IValueConverter
{
    public static readonly RelativeTimeConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime dt)
        {
            return "";
        }

        if (dt.Kind == DateTimeKind.Unspecified)
        {
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        var delta = DateTime.UtcNow - dt.ToUniversalTime();
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalSeconds < 45)
        {
            return "just now";
        }

        if (delta.TotalMinutes < 60)
        {
            int m = (int)Math.Round(delta.TotalMinutes);
            return $"{m} min ago";
        }

        if (delta.TotalHours < 24)
        {
            int h = (int)Math.Round(delta.TotalHours);
            return $"{h} hr ago";
        }

        if (delta.TotalDays < 2)
        {
            return "yesterday";
        }

        if (delta.TotalDays < 7)
        {
            return $"{(int)delta.TotalDays} days ago";
        }

        return dt.ToLocalTime().ToString("MMM d", CultureInfo.CurrentCulture);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
