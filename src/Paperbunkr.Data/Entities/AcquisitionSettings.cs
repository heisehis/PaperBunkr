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

    /// <summary>When the weekly pull list was last fetched (UTC); the daemon refetches it about twice a day.</summary>
    public DateTime? PullListRefreshedAt { get; set; }

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
    /// Import file layout, relative to <see cref="DestinationFolderPath"/>, in ComicRack CE's template syntax (<c>{token}</c>, <c>{number:000}</c>,
    /// <c>[optional groups]</c>, <c>\</c> escapes - ported from CE's <c>ExtendedStringFormater</c>). Beyond CE's tokens it adds <c>{publisher}</c> and
    /// <c>{volumeyear}</c> (the series' start year, so a series stays in one folder; <c>{year}</c> is the issue's own year, as in CE).
    /// </summary>
    public string RenameTemplate { get; set; } = DefaultRenameTemplate;

    /// <summary>The default naming template, in the Organizer grammar the shared engine reads (<see cref="TemplateGrammar.Organizer"/>).</summary>
    public const string DefaultRenameTemplate = "{<publisher>}/{<series>} ({<volumeyear>})/{<series>} #{<number3>}";

    /// <summary>The default before templates moved to the Organizer grammar; also the column default that existing rows were created with.</summary>
    public const string LegacyDefaultRenameTemplate = "{publisher}/{series} ({volumeyear})/{series} #{number:000}";

    /// <summary>
    /// Which grammar <see cref="RenameTemplate"/> is written in. Rows that predate the shared template engine are <see cref="TemplateGrammar.Import"/>
    /// until the one-time upgrade translates them (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md section 5).
    /// </summary>
    public TemplateGrammar RenameTemplateGrammar { get; set; } = TemplateGrammar.Organizer;

    /// <summary>
    /// The template exactly as the user last saved it before the upgrade, kept until they explicitly save a new one. When the upgrade could not
    /// translate it, this is what Preferences shows next to the note that the default is in use.
    /// </summary>
    public string? RenameTemplateOriginal { get; set; }

    /// <summary>True when the upgrade couldn't translate the original template and fell back to the default.</summary>
    public bool RenameTemplateUpgradeFailed { get; set; }

    /// <summary>Write a <c>ComicInfo.xml</c> into imported archives (from ComicVine's data for the issue).</summary>
    public bool WriteComicInfo { get; set; } = true;

    /// <summary>
    /// Move the original out of the client's folder instead of copying. Off by default: the download stays put so it keeps seeding
    /// (imports always work on a copy, since repacking or tagging changes the file's bytes).
    /// </summary>
    public bool MoveOriginalOnImport { get; set; }

    /// <summary>
    /// After an issue is imported, fetch its ComicVine details by the id the want already carries (no search, no review) and add them to the issue.
    /// On by default: the match is known, so there is nothing to confirm.
    /// </summary>
    public bool ScrapeOnImport { get; set; } = true;
}

/// <summary>The syntax a stored naming template is written in.</summary>
public enum TemplateGrammar
{
    /// <summary>The acquisition importer's original CE <c>ExtendedStringFormater</c> syntax: <c>{token}</c>, <c>{token:000}</c>, <c>[optional group]</c>.</summary>
    Import = 0,

    /// <summary>The shared engine's <c>{prefix&lt;name(args)&gt;postfix}</c> syntax.</summary>
    Organizer = 1,
}
