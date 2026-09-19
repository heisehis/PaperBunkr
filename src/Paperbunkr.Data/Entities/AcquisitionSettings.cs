namespace Paperbunkr.Data.Entities;

/// <summary>
/// Non-secret acquisition settings (singleton row, <c>Id</c> always 1). Secrets - the Prowlarr API key and
/// the qBittorrent credentials - are NOT here; they live in <c>CredentialStore</c> (DPAPI-encrypted) under
/// the providers "Prowlarr" and "qBittorrent".
/// </summary>
public class AcquisitionSettings
{
    public int Id { get; set; } = 1;

    /// <summary>Master switch; off by default so nothing contacts an indexer until the user opts in.</summary>
    public bool Enabled { get; set; }

    public string ProwlarrUrl { get; set; } = string.Empty;

    public string QBittorrentUrl { get; set; } = string.Empty;

    /// <summary>Every torrent the daemon adds goes in this category, and the daemon only ever touches torrents in it.</summary>
    public string QBittorrentCategory { get; set; } = "paperbunkr-comics";

    /// <summary>Path of one of the user's library folders (<see cref="WatchedFolder"/>) that imports and new series go into.</summary>
    public string DestinationFolderPath { get; set; } = string.Empty;

    public int PollIntervalMinutes { get; set; } = 60;

    public int MinSizeMb { get; set; }

    public int MaxSizeMb { get; set; } = 500;

    /// <summary>Comma-separated release groups to prefer (score bonus). Empty = no preference.</summary>
    public string PreferredReleaseGroups { get; set; } = string.Empty;

    /// <summary>Comma-separated words that disqualify a release title.</summary>
    public string IgnoredWords { get; set; } = string.Empty;

    /// <summary>Small bonus for .cbz and small penalty for .cbr (Paperbunkr reads both and repacks to .cbz on import).</summary>
    public bool PreferCbz { get; set; } = true;
}
