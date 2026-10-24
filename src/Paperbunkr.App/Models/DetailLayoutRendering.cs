using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// What a detail layout looks like on screen (docs/superpowers/specs/2026-10-19-detail-layouts-v2-design.md): caption lines, card
/// text, list cells, header cells, the sort comparer and the grouping. Text comes from the Library's own projections
/// (<see cref="ListLayoutText"/>, <see cref="IssueListFieldCatalog"/>) over each tile's <see cref="IssueListRow"/>, so a field reads
/// the same on a detail screen as in the Library. Shared by the comic and manga detail view models.
/// </summary>
public static class DetailLayoutRendering
{
    /// <summary>A field's text on a detail screen. The issue number keeps the "#" detail screens have always shown.</summary>
    public static string FieldText(IssueListSortField field, IssueListRow row)
    {
        string? text = ListLayoutText.Field(field, row);
        if (field == IssueListSortField.Number)
        {
            return string.IsNullOrWhiteSpace(text) ? "#?" : $"#{text}";
        }

        return text ?? string.Empty;
    }

    public static IReadOnlyList<string> CaptionLines(DetailListLayoutState state, IssueListRow? row)
    {
        if (state.HideCaptions || row is null)
        {
            return Array.Empty<string>();
        }

        return (state.CaptionFields ?? DetailListLayoutState.ComicDefault.CaptionFields!)
            .Select(field => FieldText(field, row))
            .ToList();
    }

    /// <summary>
    /// A card's headline (the Title element, when chosen) and second line (every other element, " · "-joined, the way the Library
    /// tiles join theirs). An in-progress issue whose layout shows no read percentage still gets one after the number, as cards
    /// always had.
    /// </summary>
    public static (string? Headline, string SecondLine) CardText(DetailListLayoutState state, IssueListRow? row, bool inProgress)
    {
        var elements = state.CardElements ?? DetailListLayoutState.ComicDefault.CardElements!;
        if (row is null)
        {
            return (null, string.Empty);
        }

        string? headline = elements.Contains(TileTextElement.Title) ? row.Title : null;
        var parts = new List<string>();
        foreach (var element in elements)
        {
            string? text = element switch
            {
                TileTextElement.Title or TileTextElement.Summary => null,
                TileTextElement.Series => row.SeriesName,
                TileTextElement.Number => FieldText(IssueListSortField.Number, row),
                _ => Enum.TryParse<IssueListSortField>(element.ToString(), out var field) ? ListLayoutText.Field(field, row) : null,
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text);
            }
        }

        if (inProgress && !elements.Contains(TileTextElement.ReadPercentage) && elements.Contains(TileTextElement.Number))
        {
            parts.Add($"{row.ReadPercentage:0}%");
        }

        return (headline, string.Join(" · ", parts));
    }

    /// <summary>The header cells for a column list: visible columns only, in order.</summary>
    public static IEnumerable<DetailListHeaderCell> HeaderCells(IEnumerable<DetailListColumn> columns, bool manga) =>
        columns.Where(c => c.Visible).Select(c => new DetailListHeaderCell
        {
            Field = c.Field,
            Header = DetailColumnKeys.DisplayName(c.Field, manga),
            Width = c.Width,
        });

    /// <summary>A catalog column's cell, or null for a detail-only key (the caller renders those).</summary>
    public static DetailListCell? CatalogCell(DetailListColumn column, IssueListRow? row, bool manga)
    {
        if (DetailColumnKeys.CatalogField(column.Field) is not { } field)
        {
            return null;
        }

        string text = row is null ? string.Empty : FieldText(field, row);
        return new DetailListCell(column.Field, text, column.Width, DetailListFieldKind.Text, DetailColumnKeys.DisplayName(column.Field, manga));
    }

    /// <summary>The layout's sort as a row comparison, issue number breaking ties, direction applied.</summary>
    public static Comparison<IssueListRow> Comparer(IssueListSortField field, int? virtualTagId, SortDirection direction,
        IReadOnlyList<VirtualTagDefinition> virtualTags)
    {
        Comparison<IssueListRow> primary = field == IssueListSortField.VirtualTag
            ? virtualTags.FirstOrDefault(t => t.Id == virtualTagId) is { } tag
                ? IssueListFieldCatalog.BuildVirtualTagSortDescriptor(tag).Compare
                : IssueListFieldCatalog.SortFields[IssueListSortField.Number].Compare
            : IssueListFieldCatalog.SortFields.TryGetValue(field, out var descriptor)
                ? descriptor.Compare
                : IssueListFieldCatalog.SortFields[IssueListSortField.Number].Compare;
        var number = IssueListFieldCatalog.SortFields[IssueListSortField.Number].Compare;
        int sign = direction == SortDirection.Descending ? -1 : 1;
        return (a, b) =>
        {
            int result = primary(a, b);
            return sign * (result != 0 ? result : number(a, b));
        };
    }

    /// <summary>A catalog or virtual-tag grouping; null for Run, Volume and None, which the screens handle themselves.</summary>
    public static IssueListGroupFieldDescriptor? Grouping(string groupBy, IReadOnlyList<VirtualTagDefinition> virtualTags)
    {
        if (DetailGrouping.FieldOf(groupBy) is { } field)
        {
            return IssueListFieldCatalog.GroupFields[field];
        }

        return DetailGrouping.VirtualTagOf(groupBy) is { } id && virtualTags.FirstOrDefault(t => t.Id == id) is { } tag
            ? IssueListFieldCatalog.BuildVirtualTagGroupDescriptor(tag)
            : null;
    }

    /// <summary>Cover size for a cover fit: Poster keeps the fixed portrait tile, Tiles a smaller one, Panorama the cover's own aspect at that height.</summary>
    public static (double Width, double Height) CoverSize(LibraryGridCoverFit fit, double baseWidth, double baseHeight, double aspect) => fit switch
    {
        LibraryGridCoverFit.Tiles => (Math.Round(baseWidth * 0.74), Math.Round(baseHeight * 0.74)),
        LibraryGridCoverFit.Panorama => (Math.Clamp(Math.Round(baseHeight * aspect), baseWidth * 0.6, baseWidth * 2.2), baseHeight),
        _ => (baseWidth, baseHeight),
    };
}
