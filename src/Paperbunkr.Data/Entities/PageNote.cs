namespace Paperbunkr.Data.Entities;

/// <summary>
/// A reader's own note on one page of a comic (docs/superpowers/specs/2026-09-26-comic-reader-inreader-reference-design.md #7). Keyed on <see cref="IssueId"/>+<see cref="PageNumber"/> (0-based, like
/// <see cref="PageReport"/>): a page has at most one note, and saving an empty text deletes it. Independent of <see cref="IssueBookmark"/>, whose own <c>Note</c> is unaffected.
/// </summary>
public class PageNote
{
    public const int MaxTextLength = 2000;

    public int Id { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    public int PageNumber { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime CreatedTime { get; set; }

    public DateTime ModifiedTime { get; set; }
}
