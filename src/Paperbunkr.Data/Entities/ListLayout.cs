namespace Paperbunkr.Data.Entities;

/// <summary>
/// A named list layout (docs/superpowers/specs/2026-10-04-list-layouts-design.md) - the CE
/// <c>ListConfiguration</c> equivalent (<c>Name</c> + a <c>DisplayListConfig</c>), per screen like
/// <see cref="Workspace"/>. Applying one copies <see cref="StateJson"/> onto the current list's
/// <see cref="ListLayoutAssignment"/>; nothing links back, so renaming or deleting a layout never changes a list.
/// </summary>
public class ListLayout
{
    public int Id { get; set; }

    /// <summary>Which screen's list this belongs to (<see cref="WorkspaceScreen.Reader"/> is never used here).</summary>
    public WorkspaceScreen Screen { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Manual display order within the screen's list, user-reorderable.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// The screen's layout record as JSON (<c>Paperbunkr.App.Models.ListLayoutState</c> for Library,
    /// <c>BooksWorkspaceState</c> for Books). Tolerant on read, like <see cref="Workspace.StateJson"/>.
    /// </summary>
    public string StateJson { get; set; } = "{}";
}
