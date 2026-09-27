namespace Paperbunkr.Data.Entities;

/// <summary>
/// One confirmed advertisement page in the ad library (docs/superpowers/specs/2026-09-21-comic-reader-page-
/// intelligence-design.md §5, pitch #9): the 64-bit difference hash of a page the user tagged Advertisement in the
/// reader. The detection scan compares other pages against these. <see cref="SourceIssueId"/> and
/// <see cref="SourcePageNumber"/> record which page seeded it, so un-tagging that page can remove the hash again
/// (a wrong tag must not keep producing proposals). Deliberately no foreign key: deleting the source issue keeps
/// the ad in the library.
/// </summary>
public class AdPageHash
{
    public int Id { get; set; }

    /// <summary>dHash bits stored as a signed <see cref="long"/> (SQLite INTEGER); compare with <c>BitOperations.PopCount(a ^ b)</c>.</summary>
    public long Hash { get; set; }

    public int? SourceIssueId { get; set; }

    /// <summary>0-based.</summary>
    public int? SourcePageNumber { get; set; }

    public DateTime CreatedAt { get; set; }
}
