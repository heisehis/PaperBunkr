namespace Paperbunkr.App.Models;

/// <summary>One segment of a <see cref="Controls.Books.ChapterRibbon"/> (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §2):
/// a chapter of a book, or a book of a series.</summary>
public sealed class RibbonSegment
{
    /// <summary>Relative width; the panel shares the ribbon's width out by weight.</summary>
    public double Weight { get; init; } = 1;

    /// <summary>0 unread, 1 read, between for the one in progress.</summary>
    public double Fill { get; init; }

    /// <summary>A missing book in a series: a dashed outline that cannot be opened.</summary>
    public bool IsGhost { get; init; }

    public bool IsCurrent { get; init; }

    /// <summary>The tooltip and screen-reader name, e.g. "Chapter 9, Nine Winters, 40% read".</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>What a click opens: a <see cref="BookChapterSummary"/> or a <see cref="BookCardSample"/>.</summary>
    public object? Tag { get; init; }

    public bool CanOpen => !IsGhost && Tag is not null;
}
