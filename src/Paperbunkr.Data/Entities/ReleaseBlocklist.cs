namespace Paperbunkr.Data.Entities;

public enum BlocklistReason
{
    /// <summary>The user rejected a candidate.</summary>
    UserRejected,

    /// <summary>The download failed (client error, or the torrent vanished).</summary>
    DownloadFailed,

    /// <summary>The downloaded archive was corrupt.</summary>
    Corrupt,

    /// <summary>The downloaded archive needs a password.</summary>
    PasswordProtected,

    /// <summary>The archive couldn't be read or held nothing usable.</summary>
    Unreadable,
}

/// <summary>
/// A release Paperbunkr must never fetch again (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §4). Matched by
/// torrent hash when known, otherwise by release name, so the next search cycle can't pick the same bad file. Stored as its string name.
/// </summary>
public class ReleaseBlocklist
{
    public int Id { get; set; }

    /// <summary>The release title as the indexer reported it. Compared case-insensitively.</summary>
    public string ReleaseName { get; set; } = string.Empty;

    /// <summary>Lower-case hex info-hash, when the release had been added to the client.</summary>
    public string? TorrentHash { get; set; }

    public BlocklistReason Reason { get; set; }

    public string? Detail { get; set; }

    public DateTime CreatedAt { get; set; }
}
