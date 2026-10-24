namespace Paperbunkr.Data.Entities;

/// <summary>
/// One person credited on a manga-family <see cref="Series"/> in a given role (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §4).
/// The series value is the authority; saving Edit Series renders these rows into each non-overridden chapter's per-role credit text
/// (<see cref="Issue.Writer"/>, <see cref="Issue.Penciller"/>, ...), and <c>CreatorResolver</c> then re-derives the chapter's <see cref="CreatorCredit"/> index.
/// </summary>
public class SeriesCredit
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series? Series { get; set; }

    /// <summary>One of <c>MangaInheritedFields</c>' credit roles: Writer, Penciller, Inker, Colorist, Letterer, CoverArtist, Editor.</summary>
    public string Role { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Position within the role, so "A, B" keeps its order when rendered back into the chapter text.</summary>
    public int SortOrder { get; set; }
}
