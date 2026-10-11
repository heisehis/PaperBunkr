using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>One Details-table column in a <see cref="ListLayoutState"/>; the list's order is the column order (CE's <c>ItemViewColumnInfo</c>).</summary>
public sealed record ListLayoutColumn(IssueListSortField Field, bool Visible = true, double Width = 150);

/// <summary>
/// A text line a Library tile can show (docs/superpowers/specs/2026-10-04-list-layouts-design.md §7) - the Paperbunkr
/// counterpart of CE's <c>ComicTextElements</c>. <see cref="Title"/> is the tile's first, bold line; every other element
/// joins the second line. All but the first three share their name with an <see cref="IssueListSortField"/> and render
/// through <see cref="IssueListFieldCatalog"/>.
/// </summary>
public enum TileTextElement
{
    Title,
    /// <summary>The series name, on an issue tile (a series tile's title already is the series).</summary>
    Series,
    /// <summary>A series tile's "Comic · 6 issues" line; nothing on an issue tile.</summary>
    Summary,
    Number,
    Volume,
    Publisher,
    Imprint,
    Year,
    Writer,
    Genre,
    Format,
    Language,
    AgeRating,
    PageCount,
    Added,
    Released,
    Rating,
    ReadPercentage,
    FileSize,
    StoryArc,
}

/// <summary>
/// One Library list layout (docs/superpowers/specs/2026-10-04-list-layouts-design.md §4) - the CE <c>DisplayListConfig</c>
/// subset Paperbunkr has: Details columns, view mode and cover style, the thumbnail caption lines, the tile text elements,
/// and sort/group. Stored as JSON in <see cref="ListLayout"/> and <see cref="ListLayoutAssignment"/>.
///
/// Every field is defaulted so a blob written before a field existed still loads. A null list means "the built-in
/// behaviour": the default column set, today's two caption lines, today's two tile lines.
/// </summary>
public sealed record ListLayoutState(
    IReadOnlyList<ListLayoutColumn>? Columns = null,
    LibraryViewMode ViewMode = LibraryViewMode.PosterGrid,
    LibraryGridCoverFit CoverFit = LibraryGridCoverFit.Poster,
    IReadOnlyList<IssueListSortField>? CaptionFields = null,
    bool HideCaptions = false,
    IReadOnlyList<TileTextElement>? TileElements = null,
    IssueListSortField SortField = IssueListSortField.Added,
    SortDirection SortDirection = SortDirection.Descending,
    IssueListGroupField GroupField = IssueListGroupField.None,
    int? SortVirtualTagId = null,
    int? GroupVirtualTagId = null)
{
    /// <summary>CE allows three caption lines (<c>ThumbnailConfig.CaptionIds</c>, First/Second/Third Line).</summary>
    public const int MaxCaptionLines = 3;

    /// <summary>What a tile shows when <see cref="TileElements"/> is null: the two lines it has always had.</summary>
    public static readonly IReadOnlyList<TileTextElement> DefaultTileElements =
        new[] { TileTextElement.Title, TileTextElement.Series, TileTextElement.Summary };
}

/// <summary>JSON for the two layout tables. Same tolerant, enum-as-string serializer as <see cref="WorkspaceStateJson"/>.</summary>
public static class ListLayoutStateJson
{
    public static string Serialize(ListLayoutState state) => WorkspaceStateJson.Serialize(state);

    /// <summary>
    /// Never throws. A corrupt blob yields the all-defaults record; a column whose field is no longer column-eligible is
    /// dropped, duplicates are dropped, and a layout left with no visible column loses its column list (so the default set
    /// is used) rather than rendering an empty table.
    /// </summary>
    public static ListLayoutState Deserialize(string? json) => Normalize(WorkspaceStateJson.DeserializeListLayout(json));

    public static ListLayoutState Normalize(ListLayoutState state)
    {
        var columns = state.Columns;
        if (columns is not null)
        {
            var eligible = IssueListFieldCatalog.ColumnFields.Select(d => d.Field).ToHashSet();
            var seen = new HashSet<IssueListSortField>();
            var kept = new List<ListLayoutColumn>();
            foreach (var column in columns)
            {
                if (column is not null && eligible.Contains(column.Field) && seen.Add(column.Field))
                {
                    kept.Add(column with { Width = ClampWidth(column.Width) });
                }
            }

            columns = kept.Any(c => c.Visible) ? kept : null;
        }

        var captions = state.CaptionFields?
            .Where(f => IssueListFieldCatalog.SortFields.TryGetValue(f, out var d) && d.Display is not null)
            .Take(ListLayoutState.MaxCaptionLines)
            .ToList();

        var tiles = state.TileElements?.Where(e => Enum.IsDefined(e)).Distinct().ToList();

        return state with { Columns = columns, CaptionFields = captions, TileElements = tiles };
    }

    public const double MinColumnWidth = 24;
    public const double MaxColumnWidth = 800;

    public static double ClampWidth(double width) =>
        double.IsNaN(width) ? 150 : Math.Clamp(width, MinColumnWidth, MaxColumnWidth);
}
