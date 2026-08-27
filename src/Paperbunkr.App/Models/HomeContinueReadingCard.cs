namespace Paperbunkr.App.Models;

/// <summary>Home screen's Continue Reading row card (docs/superpowers/specs/
/// 2026-08-18-home-screen-design.md Module 1) - pairs a <see cref="SeriesCardSample"/> (for the
/// visuals) with the specific in-progress issue to resume, which is <em>not</em> necessarily what
/// <see cref="SeriesCardSample.ContinueReadingIssueId"/> would pick (see
/// <c>HomeFeedResolver.ContinueReadingCandidate</c>'s own doc comment for why).</summary>
public sealed class HomeContinueReadingCard
{
    public required SeriesCardSample Series { get; init; }
    public int ResumeIssueId { get; init; }

    /// <summary>0-1, fed straight to <c>PosterTile.ProgressFraction</c> (docs/superpowers/specs/
    /// 2026-08-24-home-screen-design.md) - <see cref="Paperbunkr.Data.Metadata.IssueMetadataExtensions.ReadPercentage"/>
    /// returns 0-100, so this is a divide-by-100, not a re-derivation.</summary>
    public double ResumeProgressFraction { get; init; }

    /// <summary>PosterTile's badge for this row - the specific in-progress issue's own number
    /// ("#4"), not <see cref="SeriesCardSample.IssueCountLabel"/>'s series-wide total. A
    /// "how many issues does this series have" badge on a card whose whole point is "here's the
    /// exact issue you're mid-way through" was the wrong piece of information to surface.</summary>
    public required string ResumeIssueBadge { get; init; }
}
