namespace Paperbunkr.Data.Entities;

/// <summary>Where a <see cref="CollectionContent"/> range came from.</summary>
public enum CollectionContentSource
{
    /// <summary>Entered by the user.</summary>
    Manual,

    /// <summary>Worked out from MangaDex's volume list: this file is volume N, and MangaDex files chapters a to b under volume N.</summary>
    MangaDexVolume,
}

/// <summary>
/// Metadata Model Phase 7 (docs/superpowers/specs/2026-10-26-you-vs-community-and-chapter-alignment-design.md Part 3): one run of chapters a
/// local file contains, for a collected edition. A manga volume file is numbered by its volume, so the chapters it holds (41-50) were invisible
/// to chapter alignment and read as missing; this records what the file holds. An issue with no rows is exactly what it was before: one file,
/// one number. Rows are advisory metadata - nothing here renames, moves or rewrites a file - and are kept per issue so deleting the issue deletes them.
/// </summary>
public class CollectionContent
{
    public int Id { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    /// <summary>The first chapter, as written ("41", "41.5").</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>The last chapter, as written. The same as <see cref="FromNumber"/> for a single chapter.</summary>
    public string ToNumber { get; set; } = string.Empty;

    /// <summary>The numeric value of <see cref="FromNumber"/>, so a range compares without parsing.</summary>
    public float FromKey { get; set; }

    /// <summary>The numeric value of <see cref="ToNumber"/>.</summary>
    public float ToKey { get; set; }

    public CollectionContentSource Source { get; set; }

    public DateTime CreatedAt { get; set; }
}
