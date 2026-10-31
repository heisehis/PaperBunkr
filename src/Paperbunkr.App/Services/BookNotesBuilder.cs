using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Orders and filters Book Detail's Notes tab (docs/superpowers/specs/2026-10-27-books-slice2b-detail-design.md §6). Pure: the view model hands it
/// the stored annotations and the chapter titles.
/// </summary>
public static class BookNotesBuilder
{
    /// <summary>Book order: chapter, then the block's position in it (<c>pb-p12</c> sorts as 12; a missing or odd id sorts after real ones), then creation time. Regions follow, by page.</summary>
    public static IReadOnlyList<BookNoteEntry> Build(
        IEnumerable<BookHighlight> highlights,
        IEnumerable<BookBookmark> bookmarks,
        IEnumerable<BookAnnotationImage> regions,
        IReadOnlyList<string> chapterTitles)
    {
        string TitleFor(int chapter) =>
            chapter >= 0 && chapter < chapterTitles.Count && !string.IsNullOrWhiteSpace(chapterTitles[chapter])
                ? chapterTitles[chapter]
                : $"Chapter {chapter + 1}";

        var text = highlights.Select(h => new BookNoteEntry
        {
            Kind = BookNoteKind.Highlight, Id = h.Id, ChapterIndex = h.ChapterIndex, ChapterTitle = TitleFor(h.ChapterIndex), BlockId = h.BlockId,
            Excerpt = h.Excerpt, Note = h.Note, Color = h.Color, Created = h.CreatedTime,
        }).Concat(bookmarks.Select(b => new BookNoteEntry
        {
            Kind = BookNoteKind.Bookmark, Id = b.Id, ChapterIndex = b.ChapterIndex, ChapterTitle = TitleFor(b.ChapterIndex), BlockId = b.BlockId ?? string.Empty,
            Excerpt = b.Excerpt, Created = b.CreatedTime,
        }))
        .OrderBy(n => n.ChapterIndex)
        .ThenBy(n => BlockOrder(n.BlockId))
        .ThenBy(n => n.Created)
        .ToList();

        var pages = regions.OrderBy(r => r.PageIndex).ThenBy(r => r.CreatedTime).Select(r => new BookNoteEntry
        {
            Kind = BookNoteKind.Region, Id = r.Id, ChapterIndex = -1, PageIndex = r.PageIndex, Excerpt = r.Note ?? string.Empty, Note = r.Note, Created = r.CreatedTime,
        });

        return text.Concat(pages).ToList();
    }

    public static IReadOnlyList<BookNoteEntry> Filter(IEnumerable<BookNoteEntry> notes, BookNoteKindFilter kind, BookHighlightColor? color)
    {
        IEnumerable<BookNoteEntry> result = notes;

        result = kind switch
        {
            BookNoteKindFilter.Highlights => result.Where(n => n.Kind == BookNoteKind.Highlight),
            BookNoteKindFilter.Bookmarks => result.Where(n => n.Kind == BookNoteKind.Bookmark),
            BookNoteKindFilter.Regions => result.Where(n => n.Kind == BookNoteKind.Region),
            _ => result,
        };

        // A colour only narrows highlights; asking for one hides bookmarks and regions (they have none).
        if (color is { } wanted)
        {
            result = result.Where(n => n.Color == wanted);
        }

        return result.ToList();
    }

    private static int BlockOrder(string? blockId)
    {
        if (string.IsNullOrEmpty(blockId))
        {
            return int.MaxValue;
        }

        int dash = blockId.LastIndexOf('p');
        return dash >= 0 && int.TryParse(blockId.AsSpan(dash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : int.MaxValue - 1;
    }
}
