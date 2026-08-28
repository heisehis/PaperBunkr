using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="ComicAgeCatalog"/> (docs/superpowers/specs/2026-08-27-metadata-model-
/// phase4g-age-progression-design.md) - pure classification, no database. The 1980-84 disputed
/// window is covered by <see cref="BookAgeResolverTests"/> (the catalog itself has no confidence
/// concept).
/// </summary>
public class ComicAgeCatalogTests
{
    [Theory]
    [InlineData(1937, ComicAge.Platinum)]
    [InlineData(1938, ComicAge.Golden)]
    [InlineData(1955, ComicAge.Golden)]
    [InlineData(1956, ComicAge.Silver)]
    [InlineData(1969, ComicAge.Silver)]
    [InlineData(1970, ComicAge.Bronze)]
    [InlineData(1979, ComicAge.Bronze)]
    [InlineData(1980, ComicAge.Modern)]
    [InlineData(2015, ComicAge.Modern)]
    public void FromYear_MatchesCeBoundariesAtEachSeam(int year, ComicAge expected)
    {
        Assert.Equal(expected, ComicAgeCatalog.FromYear(year));
    }

    [Fact]
    public void FromYear_BeforePlatinum_ReturnsNull()
    {
        Assert.Null(ComicAgeCatalog.FromYear(1850));
    }

    [Fact]
    public void All_HasFiveAges_WithCeYearsAndDisplayNames()
    {
        Assert.Equal(5, ComicAgeCatalog.All.Count);
        Assert.Equal("Bronze Age", ComicAgeCatalog.All[ComicAge.Bronze].DisplayName);
        Assert.Equal(1970, ComicAgeCatalog.All[ComicAge.Bronze].CeStartYear);
        Assert.Equal(1979, ComicAgeCatalog.All[ComicAge.Bronze].CeEndYear);
        Assert.Null(ComicAgeCatalog.All[ComicAge.Modern].CeEndYear);
        Assert.Null(ComicAgeCatalog.All[ComicAge.Modern].CommonlyCitedRange);
        Assert.NotNull(ComicAgeCatalog.All[ComicAge.Bronze].CommonlyCitedRange);
    }
}
