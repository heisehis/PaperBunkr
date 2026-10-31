using System;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>The tabs of Book Detail's book mode (docs/superpowers/specs/2026-10-27-books-slice2b-detail-design.md §2).</summary>
public enum BookDetailTab
{
    Overview,
    Chapters,
    Notes,

    /// <summary>The reading dashboard (books redesign §4.1); offered only when the reading log has something to say.</summary>
    YourReading,
}

/// <summary>Where a chapter stands for this reader (§5).</summary>
public enum BookChapterState
{
    Unread,
    Current,
    Read,
}

public enum BookNoteKind
{
    Highlight,
    Bookmark,
    Region,
}

public enum BookNoteKindFilter
{
    All,
    Highlights,
    Bookmarks,
    Regions,
}

/// <summary>One line of the Notes tab: a highlight, a bookmark, or a captured PDF region (§6).</summary>
public sealed class BookNoteEntry
{
    public BookNoteKind Kind { get; init; }

    public int Id { get; init; }

    /// <summary>-1 for a PDF region (PDFs have pages, not chapters).</summary>
    public int ChapterIndex { get; init; }

    public string ChapterTitle { get; init; } = string.Empty;

    public string BlockId { get; init; } = string.Empty;

    public int PageIndex { get; init; }

    public string Excerpt { get; init; } = string.Empty;

    public string? Note { get; init; }

    /// <summary>Only highlights have a colour.</summary>
    public BookHighlightColor? Color { get; init; }

    public DateTime Created { get; init; }

    public bool IsHighlight => Kind == BookNoteKind.Highlight;

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public string KindLabel => Kind switch
    {
        BookNoteKind.Highlight => "Highlight",
        BookNoteKind.Bookmark => "Bookmark",
        _ => "Captured region",
    };

    public string Where => Kind == BookNoteKind.Region ? $"Page {PageIndex + 1}" : ChapterTitle;

    public string CreatedLabel => Created.ToLocalTime().ToString("MMM d, yyyy");
}

/// <summary>One stat on the reading dashboard (§4).</summary>
public sealed record BookStatTile(string Label, string Value);
