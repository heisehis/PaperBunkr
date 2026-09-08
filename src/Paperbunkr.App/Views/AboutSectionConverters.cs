using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Views;

/// <summary>
/// [{string version}, {string currentVersion}] → true when they match. Backs the Changelog
/// accordion's "this is the installed version" state - both the initial expand and the "Current"
/// badge (docs/superpowers/specs/2026-09-07-about-redesign-design.md).
/// </summary>
public sealed class VersionEqualsCurrentConverter : IMultiValueConverter
{
    public static readonly VersionEqualsCurrentConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        return values.Count >= 2 && values[0] is string version && values[1] is string current && version == current;
    }
}

/// <summary>
/// A changelog entry's <c>Body</c> text → its category-tagged groups, via
/// <see cref="ChangelogBodyFormatter"/>. Thin binding adapter only - the parsing itself lives in the
/// formatter, not here.
/// </summary>
public sealed class ChangelogBodyToGroupsConverter : IValueConverter
{
    public static readonly ChangelogBodyToGroupsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string body ? ChangelogBodyFormatter.Format(body) : Array.Empty<ChangelogBodyGroup>();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
