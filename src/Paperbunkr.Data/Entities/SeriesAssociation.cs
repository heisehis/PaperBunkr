namespace Paperbunkr.Data.Entities;

/// <summary>
/// A related series a provider names for one of ours - Metron's <c>associated</c> field (spinoffs,
/// companion series; docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md). No
/// requirement the related series exists in the user's own library, so this stores the provider's own
/// id/name pair rather than a second <see cref="Series"/> FK - it may point at something never
/// scraped locally.
/// </summary>
public class SeriesAssociation
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series? Series { get; set; }

    public ComicProvider Provider { get; set; }

    public string ExternalSeriesId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
