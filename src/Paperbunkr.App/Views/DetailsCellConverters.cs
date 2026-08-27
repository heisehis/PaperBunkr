using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>
/// [{IssueListSortField field}, {IssueListRow row}] → the cell text for that field, via
/// <see cref="IssueListFieldCatalog.SortFields"/>'s <c>Display</c> projection. Backs Library's
/// configurable Details table (docs/superpowers/specs/2026-08-27-library-browsing-4b-toolbar-
/// rework-design.md §8), so one column list drives both the header row and every data row.
/// </summary>
public sealed class DetailsCellConverter : IMultiValueConverter
{
    public static readonly DetailsCellConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not IssueListSortField field || values[1] is not IssueListRow row)
        {
            return null;
        }

        return IssueListFieldCatalog.SortFields.TryGetValue(field, out var descriptor)
            ? descriptor.Display?.Invoke(row)
            : null;
    }
}

/// <summary>
/// [{IssueListSortField field}, {IssueListSortField activeSort}, {SortDirection direction}] →
/// "↑" / "↓" when <c>field</c> is the active Details-table sort column, "" otherwise.
/// </summary>
public sealed class DetailsSortGlyphConverter : IMultiValueConverter
{
    public static readonly DetailsSortGlyphConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 3 || values[0] is not IssueListSortField field || values[1] is not IssueListSortField active
            || values[2] is not SortDirection direction || field != active)
        {
            return string.Empty;
        }

        return direction == SortDirection.Ascending ? " ↑" : " ↓";
    }
}
