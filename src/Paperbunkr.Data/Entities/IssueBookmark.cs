namespace Paperbunkr.Data.Entities;

/// <summary>
/// A manual bookmark within an <see cref="Issue"/> (docs/superpowers/specs/2026-08-17-metadata-
/// model-phase1-canonical-metadata-design.md §9). Mirrors <see cref="BookBookmark"/>'s shape,
/// simplified to a page number instead of a character offset - Issues are page-paginated, unlike
/// <see cref="Book"/>'s reflowable text. <c>BookmarkCount</c> is always computed as
/// <c>Bookmarks.Count</c>, never stored (CE's own equivalent is likewise computed, from labeled
/// pages rather than a stored count).
/// </summary>
public class IssueBookmark
{
    public int Id { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    public int PageNumber { get; set; }

    public string? Label { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedTime { get; set; }
}
