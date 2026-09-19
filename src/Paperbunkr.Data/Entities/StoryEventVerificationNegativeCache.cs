namespace Paperbunkr.Data.Entities;

/// <summary>
/// Records that a <see cref="Metadata.StoryArcGroupingResolver"/> candidate's arc name returned no
/// match from a given external source, so <see cref="Metadata.ArcExternalVerificationService"/>
/// doesn't spend a rate-limited call re-querying it every scheduled run (docs/superpowers/specs/
/// 2026-09-17-storyevent-continuity-autopopulate-design.md). A row older than 30 days is treated
/// as expired and the lookup is retried, since external catalogs are eventually updated.
/// </summary>
public class StoryEventVerificationNegativeCache
{
    public int Id { get; set; }

    public string ArcName { get; set; } = string.Empty;

    public string Publisher { get; set; } = string.Empty;

    public ArcVerificationSource Source { get; set; }

    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}
