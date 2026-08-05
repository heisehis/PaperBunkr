namespace Paperbunkr.Data.Entities;

/// <summary>
/// Links a <see cref="Series"/> to an external tracker/metadata source. New entity — feeds the
/// content-type classification pipeline (docs/onboarding.md §7) and chapter/volume alignment
/// during metadata scraping (§9).
/// </summary>
public class TrackingLink
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series? Series { get; set; }

    public TrackingService Service { get; set; }

    public string ExternalId { get; set; } = string.Empty;

    public string? LastSyncedIssueNumber { get; set; }

    public DateTime? LastSyncedAt { get; set; }
}
