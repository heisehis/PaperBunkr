namespace Paperbunkr.Data.Entities;

/// <summary>
/// A provider's rating for a <see cref="Series"/> (docs/superpowers/specs/2026-08-17-metadata-
/// model-phase5a-external-metadata-schema-design.md §32) - one column per provider
/// (AniListScore/MALScore/...) was explicitly rejected by the source doc in favor of this shape.
/// <see cref="Scale"/> is the rating's maximum (e.g. 10 or 100) so values from different providers
/// can be normalized for display without hardcoding per-provider knowledge.
/// </summary>
public class ExternalRating
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series? Series { get; set; }

    public ExternalMetadataProvider Provider { get; set; }

    public decimal Value { get; set; }

    public decimal Scale { get; set; }

    public int? VoteCount { get; set; }

    public DateTime RetrievedAt { get; set; }
}
