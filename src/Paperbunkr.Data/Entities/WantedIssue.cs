namespace Paperbunkr.Data.Entities;

/// <summary>
/// One issue the user (or a followed series) wants acquired. Carries its own copy of the number, name and
/// store date so it stands alone even when requested from an arc without a fully cached catalog.
/// </summary>
public class WantedIssue
{
    public int Id { get; set; }

    public int WatchedSeriesId { get; set; }

    public WatchedSeries? WatchedSeries { get; set; }

    /// <summary>ComicVine issue id. Unique: an issue is wanted at most once.</summary>
    public int ExternalIssueId { get; set; }

    /// <summary>Which provider <see cref="ExternalIssueId"/> belongs to. Existing rows predate Metron and are ComicVine.</summary>
    public ComicProvider Provider { get; set; } = ComicProvider.ComicVine;

    public string IssueNumber { get; set; } = string.Empty;

    public string? Name { get; set; }

    /// <summary>ComicVine's store date; a future value keeps the row in "Upcoming" until it arrives.</summary>
    public DateTime? StoreDate { get; set; }

    public string? CoverImageUrl { get; set; }

    public WantedIssueStatus Status { get; set; } = WantedIssueStatus.Wanted;

    /// <summary>Torrent hash once snatched (slice 2); identifies the download, not the issue.</summary>
    public string? TorrentHash { get; set; }

    /// <summary>The local <see cref="Issue"/> once imported (or matched as already owned).</summary>
    public int? IssueId { get; set; }

    public Issue? Issue { get; set; }

    /// <summary>Title of the release that was grabbed (kept so a failure can blocklist it by name).</summary>
    public string? GrabbedTitle { get; set; }

    /// <summary>0..1 while a download is running, refreshed by the download tracker; <c>null</c> when nothing is downloading.</summary>
    public double? DownloadProgress { get; set; }

    /// <summary>Why the last attempt failed, shown next to the Retry button.</summary>
    public string? FailureReason { get; set; }

    public DateTime? ImportedAt { get; set; }

    /// <summary>
    /// Whether ComicVine's details have been added to the imported issue (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md 6.2).
    /// Durable on purpose: it is set <see cref="ScrapeStatus.Pending"/> in the same save that marks the issue imported, so a crash between the two can't leave a silent gap.
    /// </summary>
    public ScrapeStatus ScrapeStatus { get; set; } = ScrapeStatus.NotApplicable;

    /// <summary>How many times the scrape has been tried; drives the retry backoff.</summary>
    public int ScrapeAttempts { get; set; }

    public DateTime? ScrapeLastAttemptAt { get; set; }

    /// <summary>Why the last scrape failed, shown in the needs-review list.</summary>
    public string? ScrapeError { get; set; }

    /// <summary>A failure that retrying can't fix (ComicVine has no such issue, no API key): the sweep leaves it alone until the user acts.</summary>
    public bool ScrapeFailureIsTerminal { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastSearchedAt { get; set; }

    public List<ReleaseCandidate> Candidates { get; set; } = new();
}

/// <summary>Where an imported issue is in getting its ComicVine details.</summary>
public enum ScrapeStatus
{
    /// <summary>Not an acquired issue, or adding ComicVine details to downloads is switched off.</summary>
    NotApplicable = 0,

    /// <summary>Imported and waiting for its details (also the state after a retryable failure is queued again).</summary>
    Pending = 1,

    Scraped = 2,

    /// <summary>The last attempt failed; see <see cref="WantedIssue.ScrapeError"/>.</summary>
    Failed = 3,
}
