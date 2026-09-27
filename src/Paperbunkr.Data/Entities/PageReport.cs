namespace Paperbunkr.Data.Entities;

/// <summary>
/// A "this page is bad" report raised from the comic reader (docs/superpowers/specs/2026-09-21-comic-reader-
/// page-intelligence-design.md §4, pitch #12), surfaced in Preferences → Library Health. Keyed on
/// <see cref="IssueId"/>+<see cref="PageNumber"/> (0-based, like <see cref="IssuePage.PageNumber"/>): reporting a
/// page twice updates its <see cref="Reason"/> rather than adding a row. Reports are never resolved
/// automatically - the user opens the page, tags it Deleted, or dismisses the report
/// (<see cref="Acknowledged"/>).
/// </summary>
public class PageReport
{
    public int Id { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    public int PageNumber { get; set; }

    public PageReportReason Reason { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Set by Library Health's Dismiss; cleared again when the same page is re-reported.</summary>
    public bool Acknowledged { get; set; }
}
