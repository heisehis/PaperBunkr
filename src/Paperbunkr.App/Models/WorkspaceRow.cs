namespace Paperbunkr.App.Models;

/// <summary>
/// One row in a screen's workspace dropdown (docs/superpowers/specs/2026-09-03-library-saved-
/// workspaces-design.md). Flat projection of a <c>Paperbunkr.Data.Entities.Workspace</c> plus
/// whether it's the currently-applied one - rebuilt wholesale on every dropdown open / apply, so
/// a plain immutable record is enough.
/// </summary>
public sealed record WorkspaceRow(int Id, string Name, bool IsBuiltIn, bool IsActive);
