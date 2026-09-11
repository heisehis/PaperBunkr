namespace Paperbunkr.Data.Entities;

/// <summary>
/// Per-page type tagging + persisted rotation + spread-position override (docs/ce-feature-inventory.md
/// §A; docs/superpowers/specs/2026-09-10-reader-backlog-batch-b-design.md Item 2), keyed on
/// <see cref="IssueId"/>+<see cref="PageNumber"/>. Sparse by design, same convention as <see
/// cref="IssueBookmark"/> - a page with no row is implicitly <see cref="PageType.Story"/>,
/// <c>RotationDegrees</c> 0 and <see cref="PageSpreadPosition.Default"/>; only pages the user
/// actually overrides get a row at all, rather than one row per page in every comic.
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

    /// <summary>
    /// Manual double-page-spread phase override (docs/superpowers/specs/2026-09-10-reader-backlog-
    /// batch-b-design.md Item 2). Nullable: <c>null</c> is equivalent to
    /// <see cref="PageSpreadPosition.Default"/> - avoids a DB-level default / sentinel on this
    /// column's <c>ALTER TABLE</c> against a possibly-populated table.
    /// </summary>
    public PageSpreadPosition? SpreadPosition { get; set; }
}
