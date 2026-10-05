using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// The starter list layouts seeded once into a new or upgraded library (docs/superpowers/specs/2026-10-04-list-layouts-
/// design.md §15). A deliberate deviation: CE ships no layouts (<c>Settings.ListConfigurations</c> starts empty), but an
/// empty "List layouts" menu gives nothing to try. They are ordinary named layouts - rename, change or delete them freely;
/// <see cref="Services.ListLayoutService.EnsureTemplatesSeeded"/> never brings a deleted one back.
/// </summary>
public static class ListLayoutTemplates
{
    public static IReadOnlyList<(string Name, ListLayoutState State)> Library { get; } = new[]
    {
        ("Cover wall", new ListLayoutState(
            ViewMode: LibraryViewMode.PosterGrid,
            CoverFit: LibraryGridCoverFit.Poster,
            SortField: IssueListSortField.Series,
            SortDirection: SortDirection.Ascending)),

        ("Compact tiles", new ListLayoutState(
            ViewMode: LibraryViewMode.PosterGrid,
            CoverFit: LibraryGridCoverFit.Tiles,
            TileElements: new[] { TileTextElement.Title, TileTextElement.Series, TileTextElement.Summary, TileTextElement.Publisher, TileTextElement.Year },
            SortField: IssueListSortField.Series,
            SortDirection: SortDirection.Ascending)),

        // CE's own default visible Details columns (ComicBrowserControl.cs:755-848) that Paperbunkr has data for, in CE's order.
        ("ComicRack classic", Details(IssueListSortField.Series, SortDirection.Ascending,
            (IssueListSortField.Series, 200), (IssueListSortField.Number, 60), (IssueListSortField.Volume, 60),
            (IssueListSortField.Opened, 110), (IssueListSortField.Added, 110), (IssueListSortField.PageCount, 60),
            (IssueListSortField.Year, 70), (IssueListSortField.Writer, 150), (IssueListSortField.Rating, 80))),

        ("Reading progress", Details(IssueListSortField.Opened, SortDirection.Descending,
            (IssueListSortField.Series, 220), (IssueListSortField.Number, 60), (IssueListSortField.Title, 220),
            (IssueListSortField.ReadPercentage, 80), (IssueListSortField.Opened, 120), (IssueListSortField.PageCount, 60),
            (IssueListSortField.Rating, 80))),

        ("File details", Details(IssueListSortField.Added, SortDirection.Descending,
            (IssueListSortField.Series, 200), (IssueListSortField.Number, 60), (IssueListSortField.FileName, 260),
            (IssueListSortField.FileFormat, 70), (IssueListSortField.FileSize, 80), (IssueListSortField.Added, 120),
            (IssueListSortField.FileDirectory, 320))),
    };

    private static ListLayoutState Details(IssueListSortField sort, SortDirection direction, params (IssueListSortField Field, double Width)[] columns) =>
        new(
            Columns: columns.Select(c => new ListLayoutColumn(c.Field, true, c.Width)).ToList(),
            ViewMode: LibraryViewMode.DetailsTable,
            SortField: sort,
            SortDirection: direction);
}
