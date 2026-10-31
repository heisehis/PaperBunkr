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

    /// <summary>
    /// The associated series' own ComicVine id, read from its Metron record so it can be matched to a local series that has no Metron
    /// id (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §3.1). Null = not looked up, or Metron has none.
    /// </summary>
    public int? ComicVineSeriesId { get; set; }

    /// <summary>The associated series' Grand Comics Database id, same purpose as <see cref="ComicVineSeriesId"/>.</summary>
    public int? GcdSeriesId { get; set; }

    /// <summary>When <see cref="ComicVineSeriesId"/>/<see cref="GcdSeriesId"/> were last looked up; null = never. Rechecked after 30 days.</summary>
    public DateTime? IdsCheckedUtc { get; set; }
}
