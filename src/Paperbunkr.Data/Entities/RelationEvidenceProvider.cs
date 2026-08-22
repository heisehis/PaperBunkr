namespace Paperbunkr.Data.Entities;

/// <summary>
/// Where a <see cref="RelationEvidence"/> row's assertion came from (docs/superpowers/specs/
/// 2026-08-17-metadata-model-phase3-media-relations-design.md). Scoped to just <see cref="User"/>
/// this phase - same pattern as <see cref="MetadataProposalSource"/> in Phase 2a: only the real
/// source is populated now, real external-provider members arrive with Phase 5.
/// </summary>
public enum RelationEvidenceProvider
{
    User,
    Other
}
