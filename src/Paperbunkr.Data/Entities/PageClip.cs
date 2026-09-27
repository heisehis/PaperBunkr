namespace Paperbunkr.Data.Entities;

/// <summary>
/// A rectangle clipped out of a comic page (docs/superpowers/specs/2026-09-26-comic-reader-inreader-reference-design.md #7): the PNG lives under <c>%AppData%\Paperbunkr\annotations\clips\{issueId}\</c>,
/// this row remembers where it came from. The rectangle is in fractions (0-1) of the displayed page. Several clips per page are allowed.
/// </summary>
public class PageClip
{
    public const int MaxCaptionLength = 200;

    public int Id { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    public int PageNumber { get; set; }

    public double RectX { get; set; }

    public double RectY { get; set; }

    public double RectWidth { get; set; }

    public double RectHeight { get; set; }

    /// <summary>Full path of the saved PNG.</summary>
    public string ImagePath { get; set; } = string.Empty;

    public string? Caption { get; set; }

    public DateTime CreatedTime { get; set; }
}
