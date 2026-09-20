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
    public int ComicVineIssueId { get; set; }

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

    public DateTime CreatedAt { get; set; }

    public DateTime? LastSearchedAt { get; set; }

    public List<ReleaseCandidate> Candidates { get; set; } = new();
}
