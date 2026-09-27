namespace Paperbunkr.Data.Entities;

/// <summary>
/// A page the ad-detection scan thinks is an advertisement (docs/superpowers/specs/2026-09-21-comic-reader-page-
/// intelligence-design.md §5, pitch #9), waiting for review in Needs Review. Never applied automatically: accepting
/// writes a normal <see cref="IssuePage"/> Advertisement row. A separate entity from <see cref="MetadataProposal"/>,
/// which is field-based with string values. A page with any proposal, Rejected included, is never proposed again.
/// </summary>
public class AdPageProposal
{
    public int Id { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    /// <summary>0-based.</summary>
    public int PageNumber { get; set; }

    /// <summary>The ad-library entry this page matched. Null once that entry was removed (its source page was un-tagged).</summary>
    public int? MatchedAdHashId { get; set; }

    public AdPageHash? MatchedAdHash { get; set; }

    /// <summary>Hamming distance to the matched hash (0 identical, at most 6 to be proposed).</summary>
    public int Distance { get; set; }

    public AdPageProposalStatus Status { get; set; } = AdPageProposalStatus.Pending;

    public DateTime CreatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }
}
