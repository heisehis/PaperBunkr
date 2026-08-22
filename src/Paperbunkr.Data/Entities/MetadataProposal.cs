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

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

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

    /// <summary>0.0-1.0. Fixed per <see cref="Source"/> (e.g. 0.6 for <see cref="MetadataProposalSource.FilenameParser"/>) - not a computed per-value score this phase.</summary>
    public decimal Confidence { get; set; }

    public MetadataProposalStatus Status { get; set; } = MetadataProposalStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ResolvedAt { get; set; }
}
