using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>Exercises <see cref="TitleMatchScorer"/> (docs/superpowers/specs/2026-08-19-metadata-model-anilist-search-and-link-design.md).</summary>
public class TitleMatchScorerTests
{
    [Fact]
    public void Score_IdenticalTitles_Returns1()
    {
        Assert.Equal(1.0, TitleMatchScorer.Score("One Piece", "One Piece"));
    }

    [Fact]
    public void Score_IgnoresCaseAndPunctuationAndWhitespace()
    {
        Assert.Equal(1.0, TitleMatchScorer.Score("One Piece", "one-piece"));
    }

    [Fact]
    public void Score_CompletelyDifferentTitles_IsLow()
    {
        double score = TitleMatchScorer.Score("One Piece", "Naruto");
        Assert.True(score < TitleMatchScorer.ReviewThreshold, $"Expected a low score, got {score}");
    }

    [Fact]
    public void Score_BothEmpty_Returns1()
    {
        Assert.Equal(1.0, TitleMatchScorer.Score("", ""));
    }

    [Fact]
    public void Score_OneEmpty_Returns0()
    {
        Assert.Equal(0.0, TitleMatchScorer.Score("", "One Piece"));
    }

    [Theory]
    [InlineData(1.0, MatchTier.Auto)]
    [InlineData(0.95, MatchTier.Auto)]
    [InlineData(0.9, MatchTier.NeedsReview)]
    [InlineData(0.75, MatchTier.NeedsReview)]
    [InlineData(0.5, MatchTier.Reject)]
    [InlineData(0.0, MatchTier.Reject)]
    public void Tier_MapsScoreToExpectedTier(double score, MatchTier expected)
    {
        Assert.Equal(expected, TitleMatchScorer.Tier(score));
    }

    [Fact]
    public void BestScore_PicksHighestAcrossKnownTitles()
    {
        var knownTitles = new[] { "Attack on Titan", "進撃の巨人" };

        double score = TitleMatchScorer.BestScore(knownTitles, "進撃の巨人");

        Assert.Equal(1.0, score);
    }

    [Fact]
    public void BestScore_NoKnownTitles_Returns0()
    {
        Assert.Equal(0.0, TitleMatchScorer.BestScore(Array.Empty<string>(), "One Piece"));
    }
}
