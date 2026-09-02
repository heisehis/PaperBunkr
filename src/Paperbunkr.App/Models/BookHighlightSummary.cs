using System;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>One row in the reflow reader's Highlights drawer (docs/superpowers/specs/2026-09-01-books-reader-ergonomics-and-annotations-design.md) - mirrors <see cref="BookBookmarkSummary"/>'s shape, a range instead of a point.</summary>
public sealed class BookHighlightSummary
{
    public int Id { get; init; }

    public int ChapterIndex { get; init; }

    public int StartOffset { get; init; }

    public int EndOffset { get; init; }

    public BookHighlightColor Color { get; init; }

    public string? Note { get; init; }

    public string ChapterTitle { get; init; } = string.Empty;

    public string Excerpt { get; init; } = string.Empty;

    public DateTime CreatedTime { get; init; }
}
