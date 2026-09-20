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

    /// <summary>
    /// Grab the best candidate without asking. Off by default: manual approval is the safe default. Packs are never auto-grabbed and a
    /// candidate must reach <see cref="AutoGrabMinScore"/>.
    /// </summary>
    public bool AutoGrab { get; set; }

    public int AutoGrabMinScore { get; set; } = 20;

    /// <summary>
    /// Import file layout, relative to <see cref="DestinationFolderPath"/>: CE-style <c>{token}</c> with <c>[optional groups]</c>. Beyond CE's own
    /// tokens it knows <c>{publisher}</c> and zero-padded numbers (<c>{number:000}</c>).
    /// </summary>
    public string RenameTemplate { get; set; } = "{publisher}/{series} ({year})/{series} #{number:000}";

    /// <summary>Write a <c>ComicInfo.xml</c> into imported archives (from ComicVine's data for the issue).</summary>
    public bool WriteComicInfo { get; set; } = true;

    /// <summary>
    /// Move the original out of the client's folder instead of copying. Off by default: the download stays put so it keeps seeding
    /// (imports always work on a copy, since repacking or tagging changes the file's bytes).
    /// </summary>
    public bool MoveOriginalOnImport { get; set; }
}
