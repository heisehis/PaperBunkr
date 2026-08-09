using Paperbunkr.Data.CeMigration;

namespace Paperbunkr.Data.Tests;

public class SeriesNameMatcherTests
{
    [Fact]
    public void Similarity_IsOne_ForIdenticalNames()
    {
        Assert.Equal(1.0, SeriesNameMatcher.Similarity("Astro Sentinels", "Astro Sentinels"));
    }

    [Fact]
    public void Similarity_IsOne_ForNamesDifferingOnlyByCaseOrPunctuation()
    {
        // Normalize() lowercases and strips punctuation, so these collapse to the same string.
        Assert.Equal(1.0, SeriesNameMatcher.Similarity("Moonlit Blade", "MOONLIT, BLADE!"));
    }

    [Fact]
    public void Similarity_IsHigh_ForOneCharacterTypo()
    {
        double similarity = SeriesNameMatcher.Similarity("Silver City", "Silver Citi");
        Assert.True(similarity >= SeriesNameMatcher.SimilarityThreshold, $"Expected >= threshold, got {similarity}");
    }

    [Fact]
    public void Similarity_IsLow_ForClearlyDifferentNames()
    {
        double similarity = SeriesNameMatcher.Similarity("Astro Sentinels", "Moonlit Blade");
        Assert.True(similarity < SeriesNameMatcher.SimilarityThreshold, $"Expected < threshold, got {similarity}");
    }

    [Fact]
    public void Similarity_DoesNotFlag_SharedPrefixButDifferentCharacter()
    {
        // Regression: char-only similarity scored this pair 0.83 (above threshold) despite being
        // entirely different characters/series - docs/superpowers/specs/2026-08-06-migration-ux-polish-design.md §1.
        double similarity = SeriesNameMatcher.Similarity("Wonder Woman", "Wonder Man");
        Assert.True(similarity < SeriesNameMatcher.SimilarityThreshold, $"Expected < threshold, got {similarity}");
    }

    [Fact]
    public void Similarity_StillFlags_RealSingularPluralDuplicate()
    {
        double similarity = SeriesNameMatcher.Similarity("World War Hulks", "World War Hulk");
        Assert.True(similarity >= SeriesNameMatcher.SimilarityThreshold, $"Expected >= threshold, got {similarity}");
    }

    [Fact]
    public void FindIntraGroupCandidates_SkipsExactCaseInsensitiveMatches()
    {
        var candidates = SeriesNameMatcher.FindIntraGroupCandidates(new[] { "Astro Sentinels", "astro sentinels" });
        Assert.Empty(candidates);
    }

    [Fact]
    public void FindIntraGroupCandidates_FindsNearDuplicatePair()
    {
        var candidates = SeriesNameMatcher.FindIntraGroupCandidates(new[] { "Silver City", "Silver Citi", "Moonlit Blade" });

        var candidate = Assert.Single(candidates);
        Assert.Equal("Silver City", candidate.IncomingName);
        Assert.Equal("Silver Citi", candidate.MatchedName);
    }

    [Fact]
    public void FindAgainstExistingCandidates_SkipsExactMatches_ButFindsNearDuplicates()
    {
        var candidates = SeriesNameMatcher.FindAgainstExistingCandidates(
            incomingNames: new[] { "Astro Sentinels", "Silver Citi" },
            existingNames: new[] { "astro sentinels", "Silver City" });

        var candidate = Assert.Single(candidates);
        Assert.Equal("Silver Citi", candidate.IncomingName);
        Assert.Equal("Silver City", candidate.MatchedName);
    }
}
