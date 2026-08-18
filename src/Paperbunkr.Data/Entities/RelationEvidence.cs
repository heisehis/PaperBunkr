namespace Paperbunkr.Data.Entities;

/// <summary>
/// Provenance for one <see cref="MediaRelation"/> (docs/superpowers/specs/2026-08-17-metadata-
/// model-phase3-media-relations-design.md) - a user-created relation gets exactly one row
/// (<see cref="Provider"/> = <see cref="RelationEvidenceProvider.User"/>, <see cref="Confidence"/> =
/// 1.0, a manual assertion is maximally confident by definition). Multiple rows per relation (from
/// disagreeing external providers) becomes real once Phase 5 exists to produce them.
/// </summary>
public class RelationEvidence
{
    public int Id { get; set; }

    public int MediaRelationId { get; set; }

    public MediaRelation? MediaRelation { get; set; }

    public RelationEvidenceProvider Provider { get; set; }

    /// <summary>Raw provider-side relation-type string (e.g. AniList's own vocabulary) - null for a <see cref="RelationEvidenceProvider.User"/> row, since there's no external vocabulary to preserve.</summary>
    public string? ProviderRelationType { get; set; }

    /// <summary>The provider's own id for this relation/media, for re-fetching or dedup - null for <see cref="RelationEvidenceProvider.User"/>.</summary>
    public string? ProviderSourceId { get; set; }

    /// <summary>0.0-1.0.</summary>
    public decimal Confidence { get; set; }

    public DateTime RetrievedAt { get; set; } = DateTime.UtcNow;
}
