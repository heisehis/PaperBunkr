using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;

namespace Paperbunkr.App.Services;

/// <summary>One row of a book's table of contents: the spine file it lives in, the element id to jump to inside it, and how much text it covers.</summary>
public sealed record BookTocEntry(int ChapterIndex, string Title, string? Fragment, string? PartTitle, long Length);

/// <summary>
/// Flattens a book's chapters into the table-of-contents rows the reader drawer and the Detail Chapters tab show. A spine file is not a chapter: a real EPUB can hold 26
/// chapters in 6 files, and its own contents page lists all 26. So each file contributes one row per entry the book's nav gives it (<see cref="BookChapter.Sections"/>), or a
/// single row for itself when the nav has nothing there (a cover, an unlisted file).
/// </summary>
public static class BookTocBuilder
{
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);

    public static IReadOnlyList<BookTocEntry> Build(IReadOnlyList<BookChapter> chapters)
    {
        var entries = new List<BookTocEntry>();
        for (int i = 0; i < chapters.Count; i++)
        {
            var chapter = chapters[i];
            var sections = chapter.Sections;
            if (sections.Count == 0)
            {
                entries.Add(new BookTocEntry(i, chapter.Title, null, chapter.PartTitle, TextLength(chapter, 0, null)));
                continue;
            }

            string html = chapter.Html ?? string.Empty;
            var offsets = sections.Select(s => s.Fragment is null ? 0 : Math.Max(0, EpubBookSource.OffsetOf(html, s.Fragment))).ToList();
            for (int s = 0; s < sections.Count; s++)
            {
                int end = s + 1 < sections.Count ? offsets[s + 1] : html.Length;
                entries.Add(new BookTocEntry(i, sections[s].Title, sections[s].Fragment, sections[s].PartTitle ?? chapter.PartTitle, TextLength(chapter, offsets[s], end)));
            }
        }

        return entries;
    }

    /// <summary>
    /// The row the reader is in: the last row of the current file whose target is at or before the current block, else that file's first row. <paramref name="currentHtml"/> is the markup
    /// the block id belongs to (with the reader's ids injected); returns -1 when the file has no row.
    /// </summary>
    public static int ActiveIndex(IReadOnlyList<BookTocEntry> entries, int chapterIndex, string? currentHtml, string? blockId)
    {
        int first = -1;
        int active = -1;
        int blockOffset = !string.IsNullOrEmpty(blockId) && currentHtml is not null ? EpubBookSource.OffsetOf(currentHtml, blockId) : -1;

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].ChapterIndex != chapterIndex)
            {
                continue;
            }

            if (first < 0)
            {
                first = i;
            }

            if (blockOffset < 0 || currentHtml is null)
            {
                continue;
            }

            int entryOffset = entries[i].Fragment is null ? 0 : EpubBookSource.OffsetOf(currentHtml, entries[i].Fragment!);
            if (entryOffset >= 0 && entryOffset <= blockOffset)
            {
                active = i;
            }
        }

        return active >= 0 ? active : first;
    }

    private static long TextLength(BookChapter chapter, int start, int? end)
    {
        if (chapter.Html is null)
        {
            return chapter.Paragraphs.Sum(p => (long)p.Text.Length);
        }

        int from = Math.Clamp(start, 0, chapter.Html.Length);
        int to = Math.Clamp(end ?? chapter.Html.Length, from, chapter.Html.Length);
        return Tags.Replace(chapter.Html.AsSpan(from, to - from).ToString(), string.Empty).Trim().Length;
    }
}
