namespace Paperbunkr.Data.Entities;

/// <summary>
/// A pending or resolved "here's an inferred value for this field" record (docs/superpowers/specs/
/// 2026-08-17-metadata-model-phase2a-metadata-proposals-design.md) - Paperbunkr's real, auditable
/// replacement for CE's <c>Shadow*</c>/<c>Proposed*</c> field pattern (<c>ComicBook.cs</c>).
/// <see cref="MetadataProposalStatus.Accepted"/> rows still surface in the Needs Review queue
/// ("applied but auditable/correctable"), not just <see cref="MetadataProposalStatus.Pending"/>
/// ones - see <c>NeedsReviewViewModel</c> (Paperbunkr.App).
/// </summary>
public class MetadataProposal
{
    public int Id { get; set; }

    /// <summary>
    /// Set for an Issue-scoped proposal (the original 7 fields), null for a Series-scoped one.
    /// Exactly one of <see cref="IssueId"/>/<see cref="SeriesId"/> is ever set - enforced by the
    /// callers that create rows (<c>MetadataLinkResolver</c> for Series-scoped,
    /// <c>LibraryFolderScanner</c> for Issue-scoped), not a DB constraint (docs/superpowers/specs/
    /// 2026-08-23-apply-from-provider-design.md).
    /// </summary>
    public int? IssueId { get; set; }

    public Issue? Issue { get; set; }

    /// <summary>Set for a Series-scoped proposal (Summary/Status/Genre) - see <see cref="IssueId"/>'s own doc comment.</summary>
    public int? SeriesId { get; set; }

    public Series? Series { get; set; }

    public MetadataProposalField Field { get; set; }

    /// <summary>
    /// Snapshot of <see cref="Issue"/>'s stored value for <see cref="Field"/> at proposal-creation
    /// time, for display/audit purposes only - not a live read (the resolver reads the current
    /// stored value directly from <see cref="Issue"/> instead). String even where the underlying
    /// field is numeric (<see cref="MetadataProposalField.Count"/>/<see cref="MetadataProposalField.Year"/>) -
    /// same "never destroy the display value" treatment <c>Issue.Number</c>/<c>Volume</c> already use.
    /// </summary>
    public string? CurrentValue { get; set; }

    public string? ProposedValue { get; set; }

    public MetadataProposalSource Source { get; set; }

    /// <summary>Which linked provider produced this value, when <see cref="Source"/> is <see cref="MetadataProposalSource.MetadataProvider"/> - null for every other source. A series can be linked to more than one provider at once, so <see cref="Source"/> alone can't say "via MangaBaka" vs. "via AniList" for the Detail screen's attribution line (docs/superpowers/specs/2026-08-23-apply-from-provider-design.md).</summary>
    public ExternalMetadataProvider? ProviderKey { get; set; }

    /// <summary>0.0-1.0. Fixed per <see cref="Source"/> (e.g. 0.6 for <see cref="MetadataProposalSource.FilenameParser"/>) - not a computed per-value score this phase.</summary>
    public decimal Confidence { get; set; }

    public MetadataProposalStatus Status { get; set; } = MetadataProposalStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ResolvedAt { get; set; }
}
