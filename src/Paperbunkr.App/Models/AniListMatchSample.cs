using System;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Models;

/// <summary>
/// One scored AniList search result row (docs/superpowers/specs/2026-08-19-metadata-model-anilist-
/// search-and-link-design.md) - <see cref="TierLabel"/>/<see cref="TierClass"/> drive the confidence
/// badge (Best Match/Possible Match/Low Confidence), matching this app's existing established
/// enum-to-label/style-class pattern (e.g. <c>RelationTypeOption.FormatLabel</c>).
/// </summary>
public sealed class AniListMatchSample
{
    public required string ExternalId { get; init; }
    public required string Title { get; init; }
    public string? Url { get; init; }
    public required double Confidence { get; init; }
    public required MatchTier Tier { get; init; }

    public int ConfidencePercent => (int)Math.Round(Confidence * 100);

    public string TierLabel => Tier switch
    {
        MatchTier.Auto => "Best match",
        MatchTier.NeedsReview => "Possible match",
        _ => "Low confidence",
    };

    /// <summary>Avalonia style-class name for the confidence badge - "auto"/"review"/"reject".</summary>
    public string TierClass => Tier switch
    {
        MatchTier.Auto => "auto",
        MatchTier.NeedsReview => "review",
        _ => "reject",
    };
}
