namespace Paperbunkr.Data.Entities;

/// <summary>
/// Lifecycle of a <see cref="WantedIssue"/> (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-
/// design.md §4). "Upcoming" is deliberately not a status: it is a <see cref="Wanted"/> row whose
/// <see cref="WantedIssue.StoreDate"/> is still in the future. Stored as its string name.
/// </summary>
public enum WantedIssueStatus
{
    Wanted,
    Snatched,
    Downloading,
    Imported,
    Failed,
    Ignored,
}
