namespace Paperbunkr.Data.Entities;

/// <summary>
/// Global policy governing how a newly-created <see cref="MetadataProposal"/> is handled
/// (docs/superpowers/specs/2026-08-17-metadata-model-phase2a-metadata-proposals-design.md).
/// Scoped down from the source doc's 4-value <c>PreferStored | PreferProposed | Prompt |
/// Automatic</c> - with a single active proposal source (<see cref="MetadataProposalSource.FilenameParser"/>)
/// and no safe way to let a proposal silently beat a stored value (would overwrite user-entered
/// metadata), <c>PreferStored</c>/<c>PreferProposed</c> have nothing to actually prefer between
/// yet. Add them back once a second competing source exists.
/// </summary>
public enum MetadataResolutionPolicy
{
    /// <summary>Proposals are created already <see cref="MetadataProposalStatus.Accepted"/> - matches the pre-existing scanner UX (filename-inferred values are immediately visible), now with an audit trail and a reject/undo path.</summary>
    Automatic,

    /// <summary>Proposals are created <see cref="MetadataProposalStatus.Pending"/> - a proposed value contributes nothing to <c>IssueMetadataExtensions.Effective*</c> until a human accepts it in the Needs Review queue.</summary>
    Prompt
}
