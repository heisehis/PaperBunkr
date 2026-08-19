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
}
