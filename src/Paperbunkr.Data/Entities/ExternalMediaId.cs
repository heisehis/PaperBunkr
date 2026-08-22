namespace Paperbunkr.Data.Entities;

/// <summary>
/// A provider-keyed external identifier for a <see cref="Series"/> (docs/superpowers/specs/
/// 2026-08-17-metadata-model-phase5a-external-metadata-schema-design.md §30) - a single relation
/// column per provider (e.g. AniListId/MALId) was explicitly rejected by the source doc in favor of
/// this shape.
/// </summary>
public class ExternalMediaId
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series? Series { get; set; }

    public ExternalMetadataProvider Provider { get; set; }

    public string ExternalId { get; set; } = string.Empty;

    public string? Url { get; set; }

    public DateTime? LastFetchedAt { get; set; }
}
