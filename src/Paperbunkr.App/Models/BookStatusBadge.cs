namespace Paperbunkr.App.Models;

/// <summary>The one corner badge a book cover may carry when it shows no progress bar (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §2).</summary>
public enum BookStatusBadge
{
    None,
    Finished,
    WantToRead,
    Abandoned,
}
