namespace Paperbunkr.Data.Entities;

/// <summary>State of an <see cref="AdPageProposal"/> (docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-design.md §5).</summary>
public enum AdPageProposalStatus
{
    Pending,
    Accepted,
    Rejected,
}
