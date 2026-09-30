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
    Other,

    /// <summary>Worked out by the smart connector from the library's own data (docs/superpowers/specs/2026-09-27-continuity-map-design.md §2); the reason is in <c>ProviderRelationType</c>.</summary>
    Inferred,

    /// <summary>Wikidata's follows/followed-by (P155/P156); the item id is in <c>ProviderSourceId</c>.</summary>
    Wikidata,

    /// <summary>A Grand Comics Database series bond (docs/superpowers/specs/2026-09-27-gcd-data-design.md §4); the bond type is in <c>ProviderRelationType</c>, the origin GCD series id in <c>ProviderSourceId</c>.</summary>
    Gcd,
}
