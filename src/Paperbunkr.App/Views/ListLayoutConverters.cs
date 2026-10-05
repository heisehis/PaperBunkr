using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>
/// [{IssueListSortField? field}, {IssueListRow | SeriesCardSample}] → one custom thumbnail caption line
/// (docs/superpowers/specs/2026-10-04-list-layouts-design.md §7), through the same
/// <see cref="IssueListFieldCatalog"/> <c>Display</c> projection the Details table uses. A series card shows its
/// representative issue's value.
/// </summary>
public sealed class CaptionLineConverter : IMultiValueConverter
{
    public static readonly CaptionLineConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not IssueListSortField field || ListLayoutText.RowOf(values[1]) is not { } row)
        {
            return null;
        }

        return ListLayoutText.Field(field, row);
    }
}

/// <summary>
/// [{IReadOnlyList&lt;TileTextElement&gt; elements}, {IssueListRow | SeriesCardSample}] → a tile's second line: every
/// checked element except <see cref="TileTextElement.Title"/>, the non-empty ones joined with " · ".
/// </summary>
public sealed class TileSecondLineConverter : IMultiValueConverter
{
    public static readonly TileSecondLineConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not IReadOnlyList<TileTextElement> elements)
        {
            return null;
        }

        return ListLayoutText.TileSecondLine(elements, values[1]);
    }
}

/// <summary>The text behind the two converters above, kept out of them so it can be tested without a binding.</summary>
public static class ListLayoutText
{
    public static IssueListRow? RowOf(object? item) => item switch
    {
        IssueListRow row => row,
        SeriesCardSample card => card.RepresentativeRow,
        _ => null,
    };

    public static string? Field(IssueListSortField field, IssueListRow row) =>
        IssueListFieldCatalog.SortFields.TryGetValue(field, out var descriptor) ? descriptor.Display?.Invoke(row) : null;

    public static string TileSecondLine(IReadOnlyList<TileTextElement> elements, object? item)
    {
        var parts = new List<string>();
        foreach (var element in elements)
        {
            string? text = element switch
            {
                TileTextElement.Title => null,
                // A series tile's title already is the series; an issue tile has no series summary.
                TileTextElement.Series => item is IssueListRow row ? row.SeriesName : null,
                TileTextElement.Summary => item is SeriesCardSample card ? card.Sub : null,
                _ => Enum.TryParse<IssueListSortField>(element.ToString(), out var field) && RowOf(item) is { } fieldRow
                    ? Field(field, fieldRow)
                    : null,
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text);
            }
        }

        return string.Join(" · ", parts);
    }
}
