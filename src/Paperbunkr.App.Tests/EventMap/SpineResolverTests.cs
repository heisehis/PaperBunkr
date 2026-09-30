using Paperbunkr.App.Services.EventMap;
using static Paperbunkr.App.Tests.EventMap.EventMapFixtures;

namespace Paperbunkr.App.Tests.EventMap;

/// <summary>Covers <see cref="SpineResolver"/> (docs/superpowers/specs/2026-09-25-event-map-design.md §1 "Choosing the spine").</summary>
public class SpineResolverTests
{
    [Fact]
    public void SavedSpine_StillAMember_Wins()
    {
        var source = Source("Crisis", savedSpine: SeriesA, Row(Crisis, "1", 1), Row(SeriesA, "1", 2));

        var spine = SpineResolver.Resolve(source);

        Assert.Equal(SeriesA, spine.SeriesId);
        Assert.Equal(SpineSource.User, spine.Source);
        Assert.Equal(Crisis, spine.AutoMatchSeriesId);
    }

    [Fact]
    public void StaleSavedSpine_FallsBackToAuto()
    {
        var source = Source("Crisis", savedSpine: 999, Row(Crisis, "1", 1), Row(SeriesA, "1", 2));

        var spine = SpineResolver.Resolve(source);

        Assert.Equal(Crisis, spine.SeriesId);
        Assert.Equal(SpineSource.Auto, spine.Source);
    }

    [Fact]
    public void ZeroSentinel_ForcesRelay_EvenWithANameMatch()
    {
        var source = Source("Crisis", savedSpine: SpineResolver.ForceRelay, Row(Crisis, "1", 1), Row(SeriesA, "1", 2));

        var spine = SpineResolver.Resolve(source);

        Assert.Null(spine.SeriesId);
        Assert.Equal(SpineSource.None, spine.Source);
        Assert.Equal(Crisis, spine.AutoMatchSeriesId);
    }

    [Theory]
    [InlineData("Secret Wars (2015)")]
    [InlineData("Secret Wars Vol. 2")]
    [InlineData("secret wars")]
    public void NameMatch_IgnoresVolumeAndYear(string seriesName)
    {
        var source = Source("Secret Wars", Row(7, "1", 1, seriesName: seriesName), Row(SeriesA, "1", 2));

        Assert.Equal(7, SpineResolver.Resolve(source).SeriesId);
    }

    [Fact]
    public void SeveralMatches_MostRowsWins()
    {
        var source = Source("Crisis",
            Row(20, "1", 1, seriesName: "Crisis (1985)"),
            Row(21, "1", 2, seriesName: "Crisis (2005)"),
            Row(21, "2", 3, seriesName: "Crisis (2005)"));

        Assert.Equal(21, SpineResolver.Resolve(source).SeriesId);
    }

    [Fact]
    public void SeveralMatches_TiedRowCount_FirstAppearanceWins()
    {
        var source = Source("Crisis",
            Row(SeriesA, "1", 1),
            Row(21, "1", 2, seriesName: "Crisis (2005)"),
            Row(20, "1", 3, seriesName: "Crisis (1985)"));

        Assert.Equal(21, SpineResolver.Resolve(source).SeriesId);
    }

    [Fact]
    public void NoMatch_GivesRelay_NeverTheBiggestSeries()
    {
        var source = Source("Crisis", Row(SeriesA, "1", 1), Row(SeriesA, "2", 2), Row(SeriesA, "3", 3), Row(SeriesB, "1", 4));

        var spine = SpineResolver.Resolve(source);

        Assert.Null(spine.SeriesId);
        Assert.Equal(SpineSource.None, spine.Source);
    }
}
