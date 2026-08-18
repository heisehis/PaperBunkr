namespace Paperbunkr.Data.Entities;

/// <summary>
/// The <see cref="Issue"/> field a <see cref="MetadataProposal"/> proposes a value for - CE's 7
/// <c>Shadow*</c>/<c>Proposed*</c> fields (docs/superpowers/specs/2026-08-17-metadata-model-
/// phase2a-metadata-proposals-design.md). <see cref="Series"/> is architecturally different from
/// the other 6 (docs/superpowers/specs/2026-08-17-metadata-model-phase2b-series-reassignment-
/// design.md): it's a mandatory FK, not a free-text shadow field, so accepting it is a write-time
/// reassignment (<c>SeriesReassignmentResolver.Apply</c>) rather than a read-time
/// <c>IssueMetadataExtensions.Effective*</c> fallback - the other 6 fields have both a
/// <c>MetadataProposalField</c> member and an <c>Effective*</c> resolver; <c>Series</c> has only
/// the former.
/// </summary>
public enum MetadataProposalField
{
    Title,
    Format,
    Volume,
    Number,
    Count,
    Year,
    Series
}
