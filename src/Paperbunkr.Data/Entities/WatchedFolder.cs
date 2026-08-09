namespace Paperbunkr.Data.Entities;

/// <summary>
/// A folder the user has registered for on-demand library scanning
/// (docs/superpowers/specs/2026-08-07-preferences-libraries-tab-design.md §2) - CE's
/// <c>WatchFolder</c> minus the live <c>FileSystemWatcher</c> flag, deferred to a follow-up (v1
/// is on-demand "Scan Now" only).
/// </summary>
public class WatchedFolder
{
    public int Id { get; set; }

    public string Path { get; set; } = string.Empty;
}
