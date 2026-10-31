using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.SeriesConnector;

namespace Paperbunkr.Data.Tests.SeriesConnector;

/// <summary>
/// The series connector's local rules L1-L5 (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §3.2-§3.5), run on
/// hand-built snapshots - no database, no network.
/// </summary>
public class LocalInferenceTests
{
    internal static ConnectorSeries S(int id, string name, int? start, int? end = null, string? publisher = "DC Comics", ContentType type = ContentType.Comic) =>
        new(id, name, SeriesConnectorSnapshot.TitleKey(name), SeriesConnectorSnapshot.PublisherKeyFor(publisher), type, start, end ?? start, 10);

    private static SeriesConnectorSnapshot Snap(params ConnectorSeries[] series) => new(series);

    // --- L1 volume chain ---

    [Fact]
    public void VolumeChain_LinksEachVolumeOnlyToTheOneBefore()
    {
        var snap = Snap(S(1, "Superman (2011)", 2011, 2016), S(2, "Superman (2016)", 2016, 2018), S(3, "Superman (2018)", 2018, 2021));

        var links = VolumeChainInference.Infer(snap);

        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.SourceSeriesId == 2 && l.TargetSeriesId == 1 && l.Type == RelationType.Continuation);
        Assert.Contains(links, l => l.SourceSeriesId == 3 && l.TargetSeriesId == 2);
        Assert.DoesNotContain(links, l => l.Pair == (1, 3));
        Assert.All(links, l => Assert.Equal("Later volume", l.Reason));
    }

    [Fact]
    public void VolumeChain_OverlappingRunsAreASplitSeries_NotAVolume()
    {
        Assert.Empty(VolumeChainInference.Infer(Snap(S(1, "Batman", 2016, 2020), S(2, "Batman (2018)", 2018, 2022))));
    }

    [Fact]
    public void VolumeChain_DifferentKnownPublishers_NeverLink_UnknownDoesNotBlock()
    {
        Assert.Empty(VolumeChainInference.Infer(Snap(S(1, "Star Wars (1977)", 1977, 1986, "Marvel"), S(2, "Star Wars (1998)", 1998, 2005, "Dark Horse Comics"))));
        Assert.Single(VolumeChainInference.Infer(Snap(S(1, "Flash (1987)", 1987, 2009), S(2, "Flash (2010)", 2010, 2011, publisher: null))));
    }

    [Fact]
    public void VolumeChain_ComicAndMangaWithTheSameTitle_NeverLink()
    {
        Assert.Empty(VolumeChainInference.Infer(Snap(S(1, "Akira", 1988, 1995, "Marvel"), S(2, "Akira (2000)", 2000, 2002, "Marvel", ContentType.Manga))));
    }

    [Fact]
    public void Rules_SkipPairsYouRelatedYourself_AndDismissedPairs()
    {
        var series = new[] { S(1, "Superman (2011)", 2011, 2016), S(2, "Superman (2016)", 2016, 2018) };
        Assert.Empty(VolumeChainInference.Infer(new SeriesConnectorSnapshot(series, userPairs: new[] { (2, 1) })));
        Assert.Empty(VolumeChainInference.Infer(new SeriesConnectorSnapshot(series, dismissedPairs: new[] { (1, 2) })));
    }

    // --- L2 companion suffix ---

    [Theory]
    [InlineData("Batman Annual (2016)", "Batman", "Annual")]
    [InlineData("Batman: Annual 2019", "Batman", "Annual")]
    [InlineData("Superman: Rebirth", "Superman", "Rebirth")]
    [InlineData("Giant-Size X-Men", "X-Men", "Giant-Size")]
    [InlineData("Detective Comics 80-Page Giant", "Detective Comics", "80-Page Giant")]
    [InlineData("Batman Secret Files and Origins", "Batman", "Secret Files")]
    public void CompanionSuffix_ParsesTheBaseAndLabel(string name, string expectedBase, string expectedLabel)
    {
        Assert.Equal((expectedBase, expectedLabel), CompanionSuffixInference.Parse(name));
    }

    [Fact]
    public void CompanionSuffix_LinksToTheVolumeItOverlapsMost()
    {
        var snap = Snap(S(1, "Batman (2011)", 2011, 2016), S(2, "Batman (2016)", 2016, 2024), S(3, "Batman Annual (2017)", 2017, 2020));

        var link = Assert.Single(CompanionSuffixInference.Infer(snap));

        Assert.Equal((3, 2, RelationType.SideStory, "Annual"), (link.SourceSeriesId, link.TargetSeriesId, link.Type, link.Reason));
    }

    [Fact]
    public void CompanionSuffix_NoBaseSeries_Nothing()
    {
        Assert.Empty(CompanionSuffixInference.Infer(Snap(S(1, "Aquaman Annual", 2012), S(2, "Batman", 2012))));
    }

    [Fact]
    public void CompanionSuffix_NoYears_OnlyALoneCandidate()
    {
        Assert.Single(CompanionSuffixInference.Infer(Snap(S(1, "Batman", null), S(2, "Batman Special", null))));
        Assert.Empty(CompanionSuffixInference.Infer(Snap(S(1, "Batman (2011)", 2011), S(3, "Batman (2016)", 2016), S(2, "Batman Special", null))));
    }

    // --- L3 slash titles ---

    [Fact]
    public void SlashTitle_LinksEachNamedSeries_AcrossPublishers()
    {
        var snap = Snap(
            S(1, "Batman (2011)", 2011, 2016),
            S(2, "Teenage Mutant Ninja Turtles (2011)", 2011, 2020, "IDW Publishing"),
            S(3, "Batman/Teenage Mutant Ninja Turtles", 2016),
            S(4, "Avengers", 2018));

        var links = SlashTitleInference.Infer(snap);

        Assert.Equal(2, links.Count);
        Assert.All(links, l => Assert.Equal((3, RelationType.Crossover, "Team-up title"), (l.TargetSeriesId, l.Type, l.Reason)));
        Assert.Equal(new[] { 1, 2 }, links.Select(l => l.SourceSeriesId).OrderBy(i => i));
    }

    [Fact]
    public void SlashTitle_SubtitleOnTheLastName_StillMatches_AndUnknownNamesAreSkipped()
    {
        var snap = Snap(S(1, "Batman", 2019), S(2, "Superman", 2019), S(3, "Batman/Superman: World's Finest", 2022), S(4, "Shazam/Unknown Thing", 2020));

        var links = SlashTitleInference.Infer(snap);

        Assert.Equal(new[] { 1, 2 }, links.Where(l => l.TargetSeriesId == 3).Select(l => l.SourceSeriesId).OrderBy(i => i));
        Assert.DoesNotContain(links, l => l.TargetSeriesId == 4);
    }

    // --- L4 / L5 events ---

    private static ConnectorEventMember M(int seriesId, string seriesName, string number) =>
        new(seriesId, seriesName, number, SeriesConnectorSnapshot.NumberKey(number));

    [Fact]
    public void Event_WithAMainSeries_TiesEveryOtherSeriesToIt_WithIssueRanges()
    {
        var series = new[] { S(1, "Dark Nights: Metal", 2017), S(2, "Batman (2016)", 2016, 2020), S(3, "Nightwing (2016)", 2016, 2020) };
        var metal = new ConnectorEvent(50, "Dark Nights: Metal", null, null, new[]
        {
            M(1, "Dark Nights: Metal", "1"), M(2, "Batman (2016)", "33"), M(3, "Nightwing (2016)", "29"), M(2, "Batman (2016)", "34"), M(1, "Dark Nights: Metal", "2"),
        });

        var links = EventTieInInference.Infer(new SeriesConnectorSnapshot(series, new[] { metal }));

        Assert.Equal(2, links.Count);
        Assert.All(links, l => Assert.Equal((1, RelationType.TieIn, "50"), (l.TargetSeriesId, l.Type, l.ProviderSourceId)));
        Assert.Equal("Tie-in · #33–34", links.Single(l => l.SourceSeriesId == 2).Reason);
        Assert.Equal("Tie-in · #29", links.Single(l => l.SourceSeriesId == 3).Reason);
    }

    [Fact]
    public void Event_UserPickedMainSeries_Wins_AndZeroMeansNoMainSeries()
    {
        var series = new[] { S(1, "Dark Nights: Metal", 2017), S(2, "Batman (2016)", 2016, 2020) };
        var members = new[] { M(1, "Dark Nights: Metal", "1"), M(2, "Batman (2016)", "33") };

        var picked = EventTieInInference.Infer(new SeriesConnectorSnapshot(series, new[] { new ConnectorEvent(9, "Dark Nights: Metal", 2, null, members) }));
        Assert.Equal((1, 2, RelationType.TieIn), (picked.Single().SourceSeriesId, picked.Single().TargetSeriesId, picked.Single().Type));

        var none = EventTieInInference.Infer(new SeriesConnectorSnapshot(series, new[] { new ConnectorEvent(9, "Dark Nights: Metal", 0, null, members) }));
        Assert.Equal(RelationType.Crossover, none.Single().Type);
        Assert.Equal("Crossover · Dark Nights: Metal", none.Single().Reason);
    }

    [Fact]
    public void Event_NoMainSeries_TwoOrThreeSeriesCross_FourOrMoreGetNothing()
    {
        var series = Enumerable.Range(1, 4).Select(i => S(i, $"Series {i}", 2015)).ToArray();
        ConnectorEvent Spanning(int count) => new(7, "Convergence", null, null, Enumerable.Range(1, count).Select(i => M(i, $"Series {i}", "1")).ToList());

        Assert.Equal(3, EventTieInInference.Infer(new SeriesConnectorSnapshot(series, new[] { Spanning(3) })).Count);
        Assert.Empty(EventTieInInference.Infer(new SeriesConnectorSnapshot(series, new[] { Spanning(4) })));
    }

    [Fact]
    public void Event_OneSeries_Nothing()
    {
        var series = new[] { S(1, "Batman (2016)", 2016) };
        var arc = new ConnectorEvent(3, "I Am Gotham", null, null, new[] { M(1, "Batman (2016)", "1"), M(1, "Batman (2016)", "2") });
        Assert.Empty(EventTieInInference.Infer(new SeriesConnectorSnapshot(series, new[] { arc })));
    }

    [Theory]
    [InlineData(new[] { "33", "34" }, "#33–34")]
    [InlineData(new[] { "33", "35" }, "#33, #35")]
    [InlineData(new[] { "1", "2", "3", "5", "7", "9" }, "#1–3, #5, #7, …")]
    [InlineData(new[] { "Alpha" }, "#Alpha")]
    [InlineData(new[] { "12.1" }, "#12.1")]
    public void IssueRange_CollapsesRuns(string[] numbers, string expected)
    {
        Assert.Equal(expected, EventTieInInference.IssueRange(numbers.Select(n => M(1, "X", n)).ToList()));
    }
}
