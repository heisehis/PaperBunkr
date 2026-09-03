namespace Paperbunkr.Data.Entities;

/// <summary>
/// A named, switchable snapshot of everything one browsing screen already auto-persists to
/// <see cref="AppSettings"/> (docs/superpowers/specs/2026-09-03-library-saved-workspaces-design.md) -
/// the CE <c>DisplayWorkspace</c> equivalent, scoped to the "Views setup" group only (no reader
/// display / window-state capture) and made per-screen rather than global.
///
/// A plain EF table rather than living on the <see cref="AppSettings"/> singleton row: this is a
/// growable, user-ordered list, not a fixed set of named toggles - same rationale as
/// <see cref="KeyBinding"/>.
/// </summary>
public class Workspace
{
    public int Id { get; set; }

    /// <summary>Which screen's list this belongs to. A screen only loads its own rows.</summary>
    public WorkspaceScreen Screen { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Manual display order within the screen's list, user-reorderable. Built-ins occupy 0..n first.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// A seeded starter (<c>WorkspaceService.EnsureBuiltInsSeeded</c>). Read-only in the UI and
    /// guarded in the service: apply or duplicate, never rename / edit / delete.
    /// </summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// <c>System.Text.Json</c> object holding exactly the <see cref="AppSettings"/> fields this
    /// screen persists (<c>Paperbunkr.App.Models.LibraryWorkspaceState</c> /
    /// <c>BooksWorkspaceState</c>). Tolerant on read: unknown keys ignored, missing keys fall back
    /// to the app default for that field - same posture as <see cref="AppSettings.LibraryRecentSearches"/>.
    /// </summary>
    public string StateJson { get; set; } = "{}";
}
