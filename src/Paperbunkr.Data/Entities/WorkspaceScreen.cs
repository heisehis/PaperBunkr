namespace Paperbunkr.Data.Entities;

/// <summary>
/// Which browsing screen a <see cref="Workspace"/> belongs to. Workspaces are per-screen
/// (docs/superpowers/specs/2026-09-03-library-saved-workspaces-design.md) - the Library and Books
/// screens each keep their own independent list; a screen only ever loads its own rows. <see cref="Reader"/> (2026-09-25, docs/superpowers/specs/2026-09-25-comic-reader-profiles-design.md)
/// holds the comic reader's named display profiles; its <c>StateJson</c> is a <c>ReaderProfileState</c>.
/// </summary>
public enum WorkspaceScreen
{
    Library = 0,
    Books = 1,
    Reader = 2,
}
