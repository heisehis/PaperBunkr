using System;
using System.Collections.Generic;
using System.Linq;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;

namespace Paperbunkr.App.Services.Reader;

/// <summary>
/// One reading unit: the slice of a file's injected markup that one contents entry covers (docs/superpowers/specs/2026-10-27-books-reader-
/// paged-scroll-modes-design.md §4). <see cref="Start"/>/<see cref="End"/> are offsets into that file's injected HTML, so block ids, and
/// everything stored against them, are the file's own. <see cref="Fragment"/> is the element id the unit opens with (null for a file's first
/// unit), which doubles as the position that lands on the unit's start.
/// </summary>
public sealed record BookUnit(int FileIndex, int Start, int End, string Title, string? Fragment)
{
    public string? Slice(string? injectedFileHtml) =>
        injectedFileHtml is null ? null : injectedFileHtml.Substring(Math.Min(Start, injectedFileHtml.Length), Math.Max(0, Math.Min(End, injectedFileHtml.Length) - Start));
}

/// <summary>Every unit of a book in reading order, plus which unit each contents row opens.</summary>
public sealed class BookUnitSet
{
    public BookUnitSet(IReadOnlyList<BookUnit> units, IReadOnlyList<int> rowToUnit)
    {
        Units = units;
        RowToUnit = rowToUnit;
    }

    public IReadOnlyList<BookUnit> Units { get; }

    /// <summary>Index = contents row (<see cref="BookTocBuilder.Build"/> order), value = index into <see cref="Units"/>.</summary>
    public IReadOnlyList<int> RowToUnit { get; }

    /// <summary>The unit holding a block of a file: the last of that file's units starting at or before the block; its first unit when the block is unknown.</summary>
    public int UnitOf(int fileIndex, string? injectedFileHtml, string? blockId)
    {
        int first = -1;
        int found = -1;
        int offset = !string.IsNullOrEmpty(blockId) && injectedFileHtml is not null ? EpubBookSource.OffsetOf(injectedFileHtml, blockId) : -1;
        for (int i = 0; i < Units.Count; i++)
        {
            if (Units[i].FileIndex != fileIndex)
            {
                continue;
            }

            if (first < 0)
            {
                first = i;
            }

            if (offset >= 0 && Units[i].Start <= offset)
            {
                found = i;
            }
        }

        return found >= 0 ? found : Math.Max(0, first);
    }

    /// <summary>The first contents row that opens <paramref name="unitIndex"/> (the row the drawer marks active); -1 when none does.</summary>
    public int RowOf(int unitIndex)
    {
        for (int row = 0; row < RowToUnit.Count; row++)
        {
            if (RowToUnit[row] == unitIndex)
            {
                return row;
            }
        }

        return -1;
    }
}

/// <summary>Cuts each file at its contents entries' anchors. Content before the first anchor joins the first unit; an anchor that is missing from the markup does not cut, so its row opens the unit it falls in.</summary>
public static class BookUnitSlicer
{
    /// <param name="rows">The book's contents rows, in reading order (<see cref="BookTocBuilder.Build"/>).</param>
    /// <param name="injectedFiles">Each file's markup with the reader's block ids already injected; null for a file without markup.</param>
    public static BookUnitSet Build(IReadOnlyList<BookTocEntry> rows, IReadOnlyList<string?> injectedFiles)
    {
        var units = new List<BookUnit>();
        var rowToUnit = new int[rows.Count];

        for (int file = 0; file < injectedFiles.Count; file++)
        {
            string? html = injectedFiles[file];
            int length = html?.Length ?? 0;
            var fileRows = Enumerable.Range(0, rows.Count).Where(r => rows[r].ChapterIndex == file).ToList();

            // Cut points: 0 for the first row (its preamble joins it), then each later row's element start, kept only when it moves forward.
            var cuts = new List<(int Start, string Title, string? Fragment)>();
            var rowCut = new Dictionary<int, int>();
            foreach (int r in fileRows)
            {
                int start = cuts.Count == 0 ? 0 : ElementStart(html, rows[r].Fragment);
                if (cuts.Count == 0 || start > cuts[^1].Start)
                {
                    cuts.Add((start, rows[r].Title, cuts.Count == 0 ? null : rows[r].Fragment));
                }

                rowCut[r] = cuts.Count - 1;
            }

            if (cuts.Count == 0)
            {
                cuts.Add((0, $"Chapter {file + 1}", null));
            }

            int firstUnit = units.Count;
            for (int c = 0; c < cuts.Count; c++)
            {
                int end = c + 1 < cuts.Count ? cuts[c + 1].Start : length;
                units.Add(new BookUnit(file, cuts[c].Start, end, cuts[c].Title, cuts[c].Fragment));
            }

            foreach (var (row, cut) in rowCut)
            {
                rowToUnit[row] = firstUnit + cut;
            }
        }

        return new BookUnitSet(units, rowToUnit);
    }

    /// <summary>Where the tag carrying <paramref name="fragment"/> opens; -1 when the anchor is not in the markup (so it does not cut).</summary>
    private static int ElementStart(string? html, string? fragment)
    {
        if (html is null || fragment is null)
        {
            return -1;
        }

        int at = EpubBookSource.OffsetOf(html, fragment);
        return at < 0 ? -1 : Math.Max(0, html.LastIndexOf('<', at));
    }
}
