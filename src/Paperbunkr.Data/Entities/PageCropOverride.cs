namespace Paperbunkr.Data.Entities;

/// <summary>
/// A page's own choice about auto-crop, set from the comic reader (docs/superpowers/specs/2026-09-26-comic-reader-image-quality-design.md #3). Keyed on <see cref="IssueId"/>+<see cref="PageNumber"/>
/// (0-based, like <see cref="PageReport"/>): a page has at most one row, and setting it back to <see cref="PageCropMode.Auto"/> deletes the row.
/// </summary>
public class PageCropOverride
{
    public int Id { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    public int PageNumber { get; set; }

    public PageCropMode Mode { get; set; }
}
