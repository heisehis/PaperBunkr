namespace Paperbunkr.Data.Entities;

/// <summary>How the Books reader shows a chapter (docs/superpowers/specs/2026-10-27-books-reader-paged-scroll-modes-design.md §5). Stored by name.</summary>
public enum BookReaderLayout
{
    /// <summary>One screenful at a time, each page trimmed to whole lines; every chapter starts a fresh page.</summary>
    Paged,

    /// <summary>One chapter per vertical scroll; scrolling past either end opens the neighbouring chapter.</summary>
    Scroll,
}
