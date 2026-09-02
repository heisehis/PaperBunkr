using System;

namespace Paperbunkr.App.Models;

/// <summary>One row in the PDF reader's Captures drawer (docs/superpowers/specs/2026-09-01-books-reader-ergonomics-and-annotations-design.md §"PDF area capture") - mirrors <see cref="BookBookmarkSummary"/>'s shape.</summary>
public sealed class BookAnnotationImageSummary
{
    public int Id { get; init; }

    public int PageIndex { get; init; }

    public string ImagePath { get; init; } = string.Empty;

    public string? Note { get; init; }

    public DateTime CreatedTime { get; init; }
}
