namespace Paperbunkr.Data.Entities;

/// <summary>
/// Provenance for one <see cref="EventRelation"/> (docs/superpowers/specs/2026-08-27-metadata-model-
/// phase4d-event-relations-design.md) - a user-created relation gets exactly one row
/// (<see cref="Provider"/> = <see cref="RelationEvidenceProvider.User"/>, <see cref="Confidence"/> =
/// 1.0), identical to every <see cref="RelationEvidence"/> row created today. There's no automatic
/// population path this phase (no scanner signal, no external event-linking provider).
/// </summary>
public class EventRelationEvidence
{
    public int Id { get; set; }

    public int EventRelationId { get; set; }

    public EventRelation? EventRelation { get; set; }

    public RelationEvidenceProvider Provider { get; set; }

    /// <summary>Raw provider-side relation-type string - null for a <see cref="RelationEvidenceProvider.User"/> row.</summary>
    public string? ProviderRelationType { get; set; }

    /// <summary>The provider's own id for this relation, for re-fetching or dedup - null for <see cref="RelationEvidenceProvider.User"/>.</summary>
    public string? ProviderSourceId { get; set; }

    /// <summary>0.0-1.0.</summary>
    public decimal Confidence { get; set; }

    public DateTime RetrievedAt { get; set; } = DateTime.UtcNow;
}
