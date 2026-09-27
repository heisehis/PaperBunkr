namespace Paperbunkr.Data.Entities;

/// <summary>
/// The cached dHash of one scanned page (docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-design.md
/// §5). <see cref="ContentStamp"/> is the file's size and modified time when the page was hashed: the scan re-hashes
/// only when it changed, so later runs touch only changed files.
/// </summary>
public class PageHash
{
    public int Id { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    /// <summary>0-based.</summary>
    public int PageNumber { get; set; }

    public long Hash { get; set; }

    public string ContentStamp { get; set; } = string.Empty;
}
