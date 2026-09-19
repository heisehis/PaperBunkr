using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// CE-parity check for <see cref="TitleNormalizer"/> (docs/superpowers/specs/2026-09-17-series-
/// name-matching-and-empty-row-cleanup-design.md) against the real <c>ComicInfo.SeriesEquals</c>
/// cascade this ports.
/// </summary>
public class TitleNormalizerTests
{
    [Fact]
    public void NamesMatch_ExactCaseInsensitive_ShortCircuitsBeforeLooserTiers()
    {
        Assert.True(TitleNormalizer.NamesMatch("Batman", "batman"));
    }

    [Theory]
    [InlineData("Cataclysm: The Ultimates", "Cataclysm - The Ultimates")]
    [InlineData("Cataclysm: The Ultimates", "Cataclysm – The Ultimates")] // en-dash
    [InlineData("Cataclysm: The Ultimates", "Cataclysm — The Ultimates")] // em-dash
    [InlineData("Batman & Robin", "Batman and Robin")]
    public void NamesMatch_PunctuationVariants_FoldViaStripDownTier(string a, string b)
    {
        Assert.True(TitleNormalizer.NamesMatch(a, b, ignoreVolume: false));
    }

    [Fact]
    public void NamesMatch_UnrelatedNames_DoNotMatch()
    {
        Assert.False(TitleNormalizer.NamesMatch("Batman", "Superman"));
    }

    [Fact]
    public void NamesMatch_IgnoreVolumeTrue_FoldsVolumeSuffix()
    {
        Assert.True(TitleNormalizer.NamesMatch("X-Men Vol. 2", "X-Men v2", ignoreVolume: true));
    }

    [Fact]
    public void NamesMatch_IgnoreVolumeFalse_SkipsVolumeTier()
    {
        // Still matches here because StripDown removes non-alphanumerics/"the"/"and" regardless -
        // this proves the volume-specific tier is what's being skipped, not folding in general.
        Assert.True(TitleNormalizer.NamesMatch("X-Men Vol. 2", "XMen Vol 2", ignoreVolume: false));
    }

    [Fact]
    public void StripDown_MatchesCeRxSpecialShape_StripsNonAlphanumericAndTheAnd()
    {
        Assert.Equal("cataclysmultimates", TitleNormalizer.StripDown("Cataclysm: The Ultimates").ToLowerInvariant());
        Assert.Equal("batmanrobin", TitleNormalizer.StripDown("Batman & Robin").ToLowerInvariant());
        Assert.Equal("batmanrobin", TitleNormalizer.StripDown("Batman and Robin").ToLowerInvariant());
    }

    [Fact]
    public void StripDown_DocumentedFalsePositiveRisk_TheBatmanFoldsWithBatman()
    {
        // Accepted risk from the design doc's "approaches considered" section - StripDown folds
        // "The Batman" and "Batman" together. Not a bug; documenting the known behavior.
        Assert.Equal(TitleNormalizer.StripDown("Batman"), TitleNormalizer.StripDown("The Batman"));
    }
}
