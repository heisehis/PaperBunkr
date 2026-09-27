namespace Paperbunkr.Data.Entities;

/// <summary>Why a reader flagged a page as bad (docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-design.md §4).</summary>
public enum PageReportReason
{
    Corrupt,
    Blank,
    LowRes,
    Other,
}
