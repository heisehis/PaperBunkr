using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Resolves <see cref="AdPageProposal"/> rows from Needs Review (docs/superpowers/specs/2026-09-21-comic-reader-
/// page-intelligence-design.md §5). Accepting writes a normal <see cref="IssuePage"/> Advertisement tag (through
/// <see cref="IssuePageTagger"/>, so an existing rotation/spread override is kept and page skipping applies
/// immediately) and marks the proposal Accepted; rejecting marks it Rejected so the page is never proposed again.
/// Only Pending proposals change. Each method saves its own work.
/// </summary>
public static class AdPageProposalResolver
{
    /// <summary>Accepts one Pending proposal. Returns whether anything changed.</summary>
    public static bool Accept(PaperbunkrDbContext context, int proposalId)
    {
        var proposal = context.AdPageProposals.Find(proposalId);
        if (proposal is not { Status: AdPageProposalStatus.Pending })
        {
            return false;
        }

        IssuePageTagger.SetPageType(context, proposal.IssueId, proposal.PageNumber, PageType.Advertisement);
        proposal.Status = AdPageProposalStatus.Accepted;
        proposal.ResolvedAt = DateTime.UtcNow;
        context.SaveChanges();
        return true;
    }

    /// <summary>Rejects one Pending proposal. Returns whether anything changed.</summary>
    public static bool Reject(PaperbunkrDbContext context, int proposalId)
    {
        var proposal = context.AdPageProposals.Find(proposalId);
        if (proposal is not { Status: AdPageProposalStatus.Pending })
        {
            return false;
        }

        proposal.Status = AdPageProposalStatus.Rejected;
        proposal.ResolvedAt = DateTime.UtcNow;
        context.SaveChanges();
        return true;
    }

    /// <summary>Accepts every listed Pending proposal; returns how many changed.</summary>
    public static int AcceptAll(PaperbunkrDbContext context, IEnumerable<int> proposalIds) =>
        proposalIds.ToList().Count(id => Accept(context, id));

    /// <summary>Rejects every listed Pending proposal; returns how many changed.</summary>
    public static int RejectAll(PaperbunkrDbContext context, IEnumerable<int> proposalIds) =>
        proposalIds.ToList().Count(id => Reject(context, id));
}
