using System;
using Paperbunkr.Data.Metadata;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Models;

/// <summary>
/// One scored tracker search result row (docs/superpowers/specs/2026-08-23-tracker-write-back-sync-
/// design.md) - deliberately separate from <see cref="AniListMatchSample"/> despite the identical
/// shape, since this feeds <c>TrackerLinkResolver</c> (account-write consequences) rather than
/// <c>MetadataLinkResolver</c> (read-only), and a naming collision here would blur that distinction
/// this feature's whole design exists to keep visible. <see cref="LinkConfirm"/> is the explicit
/// "this will write to your account" confirmation step per row (docs/superpowers/specs/2026-08-22-
/// delete-functionality-design.md's <see cref="TwoStepConfirm"/>, reused here for its "arm, then
/// confirm" shape rather than its original delete purpose).
/// </summary>
public sealed class TrackerMatchSample
{
    public required string ExternalId { get; init; }
    public required string Title { get; init; }
    public string? Url { get; init; }
    public required double Confidence { get; init; }
    public required MatchTier Tier { get; init; }

    public required TwoStepConfirm LinkConfirm { get; init; }

    public int ConfidencePercent => (int)Math.Round(Confidence * 100);

    public string TierLabel => Tier switch
    {
        MatchTier.Auto => "Best match",
        MatchTier.NeedsReview => "Possible match",
        _ => "Low confidence",
    };

    public string TierClass => Tier switch
    {
        MatchTier.Auto => "auto",
        MatchTier.NeedsReview => "review",
        _ => "reject",
    };
}
