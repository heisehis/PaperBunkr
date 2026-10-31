using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.SeriesConnector;

/// <summary>
/// One series relation the series connector worked out (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §3).
/// <see cref="Type"/> describes <see cref="SourceSeriesId"/>'s role relative to <see cref="TargetSeriesId"/>, same convention as
/// <see cref="MediaRelation"/>. <see cref="Reason"/> is what the Related tab card shows; it's stored in
/// <see cref="RelationEvidence.ProviderRelationType"/>. <see cref="ProviderSourceId"/> is the story event id for tie-ins and crossovers,
/// the Metron series id for associated series, and null otherwise. A relation backed twice (one tie-in through two events) is two of
/// these with different <see cref="ProviderSourceId"/>s.
/// </summary>
public sealed record InferredSeriesRelation(
    int SourceSeriesId,
    int TargetSeriesId,
    RelationType Type,
    decimal Confidence,
    string Reason,
    RelationEvidenceProvider Provider = RelationEvidenceProvider.Inferred,
    string? ProviderSourceId = null)
{
    /// <summary>At or above this, a relation is saved automatically; below it, it's only a suggestion (design §3).</summary>
    public const decimal AutomaticThreshold = 0.75m;

    public (int Low, int High) Pair => SeriesConnectorGuards.Pair(SourceSeriesId, TargetSeriesId);
}
