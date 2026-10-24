namespace Paperbunkr.Data.Entities;

/// <summary>
/// Screen namespace shared by <see cref="Workspace"/> and list-layout rows. Workspaces are per-screen
/// (docs/superpowers/specs/2026-09-03-library-saved-workspaces-design.md). <see cref="Reader"/> holds
/// comic reader display profiles. <see cref="ComicDetail"/> and <see cref="MangaDetail"/> are
/// list-layout-only keys and do not have Workspace rows.
/// </summary>
public enum WorkspaceScreen
{
    Library = 0,
    Books = 1,
    Reader = 2,
    ComicDetail = 3,
    MangaDetail = 4,
}
