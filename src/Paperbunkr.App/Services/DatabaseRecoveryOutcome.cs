namespace Paperbunkr.App.Services;

/// <summary>
/// What the user chose in <see cref="Views.DatabaseRecoveryWindow"/> after a failed
/// <see cref="DatabaseIntegrityService.CheckIntegrity"/> at startup (docs/superpowers/specs/
/// 2026-08-29-db-corruption-safeguards-design.md §3).
/// </summary>
public enum DatabaseRecoveryOutcome
{
    /// <summary>Overwrite the live db with a chosen backup, then relaunch.</summary>
    Restore,

    /// <summary>Rename the corrupt file aside and proceed as a brand-new install.</summary>
    StartFresh,

    /// <summary>Exit without changing anything, so the user can investigate manually.</summary>
    Quit,
}
