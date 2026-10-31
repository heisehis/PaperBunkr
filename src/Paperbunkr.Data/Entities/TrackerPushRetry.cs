using System;

namespace Paperbunkr.Data.Entities;

public enum TrackerPushRetryState
{
    Pending,
    Exhausted,
}

/// <summary>Durable automatic progress push work for one tracker link. It is intentionally separate
/// from <see cref="TrackingLink"/> so retry timing does not affect pull throttling.</summary>
public sealed class TrackerPushRetry
{
    public int Id { get; set; }
    public int TrackingLinkId { get; set; }
    public TrackingLink? TrackingLink { get; set; }
    public string TargetExternalId { get; set; } = string.Empty;
    public ReadingStatus DesiredStatus { get; set; }
    public int? HighestChapter { get; set; }
    /// <summary>Scheduled retries already consumed; the initial automatic attempt is not counted.</summary>
    public int RetriesUsed { get; set; }
    public DateTime NextAttemptUtc { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
    public string? LastError { get; set; }
    public TrackerPushRetryState State { get; set; }
    public DateTime CreatedUtc { get; set; }
}
