using System;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// Home screen's "Continue Reading — Books" row card (docs/superpowers/specs/2026-08-27-books-
/// screen-chrome-and-home-strip-design.md) - the book counterpart of
/// <see cref="HomeContinueReadingCard"/>. Progress is a rough chapter fraction (EPUB only; the PDF
/// page reader has no chapters, so <see cref="ShowProgress"/> is false there).
/// </summary>
public sealed class HomeBookCard
{
    public int BookId { get; init; }

    public BookFormat Format { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Author { get; init; }

    public bool ShowProgress { get; init; }

    /// <summary>0-1, fed straight to <c>PosterTile.ProgressFraction</c>.</summary>
    public double ProgressFraction { get; init; }

    public static HomeBookCard FromBook(Book book)
    {
        bool showProgress = book.Format == BookFormat.Epub && book.ChapterCount > 1;
        return new HomeBookCard
        {
            BookId = book.Id,
            Format = book.Format,
            Title = book.Title,
            Author = book.Author,
            ShowProgress = showProgress,
            ProgressFraction = showProgress
                ? Math.Clamp(book.LastChapterIndex / (double)(book.ChapterCount - 1), 0, 1)
                : 0,
        };
    }
}
