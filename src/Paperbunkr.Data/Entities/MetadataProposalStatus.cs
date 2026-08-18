namespace Paperbunkr.Data.Entities;

/// <summary>Resolution state of a <see cref="MetadataProposal"/>, stored as its string name (see PaperbunkrDbContext.OnModelCreating).</summary>
public enum MetadataProposalStatus
{
    Pending,
    Accepted,
    Rejected,
    Ignored
}
