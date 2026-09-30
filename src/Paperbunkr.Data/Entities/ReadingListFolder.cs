namespace Paperbunkr.Data.Entities;

/// <summary>
/// A folder in the Reading Lists sidebar (docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-design.md §1). Mirrors
/// ComicRack CE's <c>ComicListItemFolder</c>: folders nest to any depth, remember whether they are collapsed and carry optional notes
/// (<see cref="Description"/>). Deliberately without CE's combine mode - a folder shows an overview of its lists, not a merged book list.
/// Structure changes go through <c>ReadingListFolders</c>.
/// </summary>
public class ReadingListFolder
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The containing folder, or null at the top level.</summary>
    public int? ParentFolderId { get; set; }

    public ReadingListFolder? ParentFolder { get; set; }

    /// <summary>Order among the folders that share <see cref="ParentFolderId"/> (folders always come before lists).</summary>
    public int SortOrder { get; set; }

    public bool IsCollapsed { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
