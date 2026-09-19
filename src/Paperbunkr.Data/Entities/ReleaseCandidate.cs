namespace Paperbunkr.Data.Entities;

/// <summary>A scored release found for a <see cref="WantedIssue"/>, waiting for the user's approval (manual approve is the default).</summary>
public class ReleaseCandidate
{
    public int Id { get; set; }

    public int WantedIssueId { get; set; }

    public WantedIssue? WantedIssue { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Magnet URI or <c>.torrent</c> URL to hand to the download client.</summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>Indexer-side id used to avoid storing the same release twice.</summary>
    public string? Guid { get; set; }

    public long SizeBytes { get; set; }

    public int Seeders { get; set; }

    public string? Indexer { get; set; }

    public double Score { get; set; }

    /// <summary>A range/pack title ("v1-6", "#1-12", "Complete"): never auto-accepted for a single-issue want.</summary>
    public bool IsPack { get; set; }

    public DateTime FoundAt { get; set; }
}
