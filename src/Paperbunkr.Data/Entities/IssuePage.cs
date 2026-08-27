namespace Paperbunkr.Data.Entities;

/// <summary>
/// Per-page type tagging + persisted rotation override (docs/ce-feature-inventory.md §A), keyed on
/// <see cref="IssueId"/>+<see cref="PageNumber"/>. Sparse by design, same convention as <see
/// cref="IssueBookmark"/> - a page with no row is implicitly <see cref="PageType.Story"/> and
/// <c>RotationDegrees</c> 0; only pages the user actually tags/rotates get a row at all, rather than
/// one row per page in every comic.
/// </summary>
public class IssuePage
{
    public int Id { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    public int PageNumber { get; set; }

    public PageType PageType { get; set; } = PageType.Story;

    /// <summary>0/90/180/270 only - not validated here, same division of responsibility as the
    /// Reader's existing session-only <c>PageCanvas.ManualRotationDegrees</c>. Unlike that one, this
    /// value is persisted and applies every time this specific page is viewed, in every future
    /// reading session.</summary>
    public int RotationDegrees { get; set; }
}
