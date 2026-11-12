using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests.SeriesConnector;

/// <summary><see cref="EventSpineResolver"/>: the event-main-series rule shared by the Event Map and the series connector.</summary>
public class EventSpineResolverTests
{
    [Fact]
    public void UserRelationsChooseMainBeforeNameMatchButNotBeforeSavedChoice()
    {
        var rows = new[] { (1, "Event"), (2, "Main"), (3, "Tie"), (2, "Main") };
        var links = new[] { (3, 2, Paperbunkr.Data.Entities.RelationType.TieIn), (2, 1, Paperbunkr.Data.Entities.RelationType.MainEventSeries) };
        Assert.Equal(2, EventSpineResolver.Resolve("Event", null, rows, links));
        Assert.Equal(1, EventSpineResolver.Resolve("Event", 1, rows, links));
        Assert.Null(EventSpineResolver.Resolve("Event", EventSpineResolver.ForceRelay, rows, links));
        Assert.Equal(1, EventSpineResolver.Resolve("Event", null, rows));
    }

    [Fact]
    public void RelationTiesPreferMoreRowsThenFirstAppearance()
    {
        var rows = new[] { (1, "A"), (2, "B"), (3, "C"), (2, "B") };
        var links = new[] { (3, 1, Paperbunkr.Data.Entities.RelationType.TieIn), (3, 2, Paperbunkr.Data.Entities.RelationType.TieIn) };
        Assert.Equal(2, EventSpineResolver.FindRelationMatch(rows, links));
        Assert.Equal(1, EventSpineResolver.FindRelationMatch(rows.Take(3).ToArray(), links));
    }
    private static readonly (int, string)[] Metal =
    {
        (1, "Batman (2016)"), (2, "Dark Nights: Metal (2017)"), (1, "Batman (2016)"), (3, "Nightwing (2016)"), (1, "Batman (2016)"), (2, "Dark Nights: Metal (2017)"),
    };

    [Fact]
    public void NameMatch_IgnoresTheYearSuffix_AndNeverPicksTheSeriesWithMostIssues()
    {
        Assert.Equal(2, EventSpineResolver.FindNameMatch("Dark Nights: Metal", Metal));
    }

    [Fact]
    public void NameMatch_PrefersMostRows_ThenFirstAppearance()
    {
        var rows = new (int, string)[] { (5, "Metal"), (6, "Metal"), (6, "Metal") };
        Assert.Equal(6, EventSpineResolver.FindNameMatch("Metal", rows));

        var tie = new (int, string)[] { (7, "Metal"), (8, "Metal") };
        Assert.Equal(7, EventSpineResolver.FindNameMatch("Metal", tie));
    }

    [Fact]
    public void NoMatchingName_NoMainSeries()
    {
        Assert.Null(EventSpineResolver.FindNameMatch("Convergence", Metal));
        Assert.Null(EventSpineResolver.FindNameMatch(null, Metal));
    }

    [Fact]
    public void SavedChoice_WinsWhileStillAMember_AndZeroMeansNone()
    {
        Assert.Equal(3, EventSpineResolver.Resolve("Dark Nights: Metal", 3, Metal));
        Assert.Equal(2, EventSpineResolver.Resolve("Dark Nights: Metal", 99, Metal));
        Assert.Null(EventSpineResolver.Resolve("Dark Nights: Metal", EventSpineResolver.ForceRelay, Metal));
    }
}
