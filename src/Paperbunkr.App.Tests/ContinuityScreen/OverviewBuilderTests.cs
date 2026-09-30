using Paperbunkr.App.Services;
using Paperbunkr.App.Services.ContinuityScreen;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests.ContinuityScreen;

/// <summary>
/// The continuity and event Overviews' pure builders (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md): runs with
/// placeholders, branches and cycles, the stats lines, events in order with their link lines, attention items, Continue and the source
/// label. No database - the loaders are exercised through the page view model tests.
/// </summary>
public class OverviewBuilderTests
{
    private static int _nextIssue = 1;

    private static EventMapRow Row(int seriesId, string series, string number, int? year, EventMapReadState read = EventMapReadState.Unread,
        EventMembershipRole role = EventMembershipRole.Core, int? issueId = null, bool missing = false, int month = 1) =>
        new(0, issueId ?? _nextIssue++, 0, role, seriesId, series, number, year, month, missing, read, CoverKey: $"cover-{seriesId}-{number}");

    private static ContinuityMapData Map(IReadOnlyList<ContinuityLane> lanes, IReadOnlyList<EventMapRow> loose,
        IReadOnlyList<ContinuityEventData>? events = null, IReadOnlyList<ContinuityRelationData>? relations = null, int pending = 0) =>
        new(1, "Earth-616", lanes, events ?? Array.Empty<ContinuityEventData>(), relations ?? Array.Empty<ContinuityRelationData>(), loose,
            new Dictionary<int, IReadOnlyList<EventMapEventRef>>(), pending);

    private static ContinuityOverview Build(ContinuityMapData map, IReadOnlyList<(int Newer, int Older)>? links = null,
        IReadOnlyList<OverviewPlaceholder>? placeholders = null, IReadOnlyList<OverviewConnection>? connections = null, bool gcd = false) =>
        ContinuityOverviewBuilder.Build(new ContinuityOverviewInput(map, links ?? Array.Empty<(int, int)>(),
            placeholders ?? Array.Empty<OverviewPlaceholder>(), connections ?? Array.Empty<OverviewConnection>(), gcd));

    [Fact]
    public void Runs_ChainOlderFirst_WithLabel_AndTheRestStandaloneByYear()
    {
        var lanes = new[]
        {
            new ContinuityLane(2, "Hulk (2008)", false), new ContinuityLane(1, "Incredible Hulk (1968)", false),
            new ContinuityLane(3, "Thor", false), new ContinuityLane(4, "Avengers", false),
        };
        var loose = new[] { Row(1, "Incredible Hulk (1968)", "1", 1968), Row(1, "Incredible Hulk (1968)", "474", 1999), Row(2, "Hulk (2008)", "1", 2008),
            Row(3, "Thor", "1", 1966), Row(4, "Avengers", "1", 1963) };

        var overview = Build(Map(lanes, loose), links: new[] { (Newer: 2, Older: 1) });

        var run = Assert.Single(overview.Runs);
        Assert.Equal(new int?[] { 1, 2 }, run.Items.Select(i => i.SeriesId));
        Assert.Equal("Incredible Hulk · 1968–2008 · 2 series", run.Label);
        Assert.False(run.Items[0].ArrowBefore);
        Assert.True(run.Items[1].ArrowBefore);
        Assert.Equal(new[] { 4, 3 }, overview.Standalone);
    }

    [Fact]
    public void Runs_UnownedContinuations_AreDimmedPlaceholders_OnTheRightSide()
    {
        var lanes = new[] { new ContinuityLane(1, "Hulk (1999)", false) };
        var loose = new[] { Row(1, "Hulk (1999)", "1", 1999) };
        var placeholders = new[]
        {
            new OverviewPlaceholder(500, "Hulk", 2008, OwnedSeriesId: 1, OwnedIsOlder: true),
            new OverviewPlaceholder(400, "Incredible Hulk", 1968, OwnedSeriesId: 1, OwnedIsOlder: false),
        };

        var run = Assert.Single(Build(Map(lanes, loose), placeholders: placeholders, gcd: true).Runs);

        Assert.Equal(new[] { "Incredible Hulk (1968)", "Hulk (1999)", "Hulk (2008)" }, run.Items.Select(i => i.Name));
        Assert.True(run.Items[0].IsPlaceholder);
        Assert.Equal("https://www.comics.org/series/400/", run.Items[0].Url);
        Assert.False(run.Items[1].IsPlaceholder);
        Assert.Equal("Hulk · 1968–2008 · 3 series", run.Label);
    }

    [Fact]
    public void Runs_ABranchAndACycle_EachGiveOneRun_OrderedByYear()
    {
        var lanes = new[] { new ContinuityLane(1, "A", false), new ContinuityLane(2, "B", false), new ContinuityLane(3, "C", false) };
        var loose = new[] { Row(1, "A", "1", 1990), Row(2, "B", "1", 2000), Row(3, "C", "1", 1995) };

        var branch = Build(Map(lanes, loose), links: new[] { (Newer: 2, Older: 1), (Newer: 3, Older: 1) });
        Assert.Equal(new int?[] { 1, 3, 2 }, Assert.Single(branch.Runs).Items.Select(i => i.SeriesId));

        var cycle = Build(Map(lanes, loose), links: new[] { (Newer: 2, Older: 1), (Newer: 3, Older: 2), (Newer: 1, Older: 3) });
        Assert.Equal(new int?[] { 1, 2, 3 }, Assert.Single(cycle.Runs).Items.Select(i => i.SeriesId));
        Assert.Empty(cycle.Standalone);
    }

    [Fact]
    public void Stats_YearsSeriesIssuesEventsAndRead_WithTheGcdChip()
    {
        var lanes = new[] { new ContinuityLane(1, "X-Men", false), new ContinuityLane(2, "X-Factor", false) };
        var inEvent = Row(1, "X-Men", "3", 2002, EventMapReadState.Read);
        var loose = new[] { Row(1, "X-Men", "1", 2000, EventMapReadState.Read), Row(2, "X-Factor", "1", 2015), Row(2, "X-Factor", "2", 2015) };
        var events = new[] { new ContinuityEventData(10, "Messiah", 200201, 200203, new[] { inEvent, Row(9, "Cable", "1", 2002) }) };

        var overview = Build(Map(lanes, loose, events), gcd: true);

        Assert.Equal("2000–2015 · 2 series · 4 issues · 1 event · 50% read", overview.StatsLine);
        Assert.True(overview.ShowGcdChip);
        Assert.Equal((2, 2), (overview.SeriesStats[1].IssueCount, overview.SeriesStats[1].ReadCount));
        Assert.Equal("2000–2002", overview.SeriesStats[1].Years);
    }

    [Fact]
    public void Events_FollowTheChronology_WithLinkLinesBetweenLinkedNeighbours()
    {
        var lanes = new[] { new ContinuityLane(1, "Hulk", false) };
        var events = new[]
        {
            new ContinuityEventData(20, "World War Hulk", 200706, 200801, new[] { Row(1, "Hulk", "106", 2007, EventMapReadState.Read) }),
            new ContinuityEventData(10, "Planet Hulk", 200604, 200705, new[] { Row(1, "Hulk", "92", 2006), Row(1, "Hulk", "93", 2006) }),
            new ContinuityEventData(30, "Secret Invasion", 200804, 200812, new[] { Row(1, "Hulk", "200", 2008) }),
        };
        var relations = new[] { new ContinuityRelationData(10, 20, RelationType.Prequel, true, "yours") };

        var rows = Build(Map(lanes, Array.Empty<EventMapRow>(), events, relations)).Events;

        Assert.Equal(new[] { "Planet Hulk", "World War Hulk", "Secret Invasion" }, rows.Select(r => r.Name));
        Assert.Equal("↓ prequel of", rows[0].LinkBelow);
        Assert.Null(rows[1].LinkBelow);
        Assert.Equal("2006–2007", rows[0].Years);
        Assert.Equal((1, 1), (rows[1].IssueCount, rows[1].ReadCount));
    }

    [Fact]
    public void Attention_ShowsOnlyWhatIsThere()
    {
        var lanes = new[] { new ContinuityLane(1, "Hulk", false) };
        var events = new[] { new ContinuityEventData(10, "E", 200601, 200601, new[] { Row(1, "Hulk", "1", 2006), Row(7, "Thor", "5", 2006) }) };
        var connections = new[] { new OverviewConnection(10, 11, RelationType.Sequel, "E looks like the sequel of F", "dates") };

        var overview = Build(Map(lanes, Array.Empty<EventMapRow>(), events, pending: 2), connections: connections);

        Assert.Equal(new[] { AttentionKind.Duplicates, AttentionKind.Connections, AttentionKind.OutsideSeries }, overview.Attention.Select(a => a.Kind));
        Assert.Equal("2 possible duplicate events", overview.Attention[0].Text);
        Assert.Equal("1 series from its events isn't in this continuity", overview.Attention[2].Text);
        Assert.Equal("Thor", Assert.Single(overview.OutsideSeries).Name);

        Assert.Empty(Build(Map(lanes, new[] { Row(1, "Hulk", "1", 2006) })).Attention);
    }

    [Fact]
    public void Continue_IsTheFirstUnreadIssueYouHave_InMapOrder_AnchoredToItsEvent()
    {
        var lanes = new[] { new ContinuityLane(1, "Hulk", false) };
        var read = Row(1, "Hulk", "92", 2006, EventMapReadState.Read);
        var missing = Row(1, "Hulk", "93", 2006, missing: true);
        var next = Row(1, "Hulk", "94", 2006, month: 3);
        var events = new[] { new ContinuityEventData(10, "Planet Hulk", 200601, 200612, new[] { read, missing, next }) };

        var target = Build(Map(lanes, new[] { Row(1, "Hulk", "200", 2010) }, events)).Continue;

        Assert.NotNull(target);
        Assert.Equal(next.IssueId, target!.IssueId);
        Assert.Equal("Hulk #94", target.Label);
        Assert.Equal(10, target.EventId);

        var allRead = Build(Map(lanes, new[] { Row(1, "Hulk", "1", 2006, EventMapReadState.Read) })).Continue;
        Assert.Null(allRead);
    }

    [Fact]
    public void EventOverview_StatsSourceNeighboursContinueAndCollage()
    {
        var members = new[]
        {
            Row(1, "WWH: Prologue", "1", 2007, EventMapReadState.Read, EventMembershipRole.Prologue),
            Row(2, "World War Hulk", "1", 2007, EventMapReadState.Read),
            Row(3, "Incredible Hulk", "106", 2007, role: EventMembershipRole.TieIn),
            Row(2, "World War Hulk", "2", 2007),
        };
        var neighbours = new[]
        {
            new EventNeighbourInput(5, "Planet Hulk", 200604, 200705, true, IsEarlier: true),
            new EventNeighbourInput(6, "Aftersmash", 200802, 200803, false, IsEarlier: false),
        };

        var overview = EventOverviewBuilder.Build(new EventOverviewInput(9, "World War Hulk", 200706, 200801, members, "123", "456",
            StoryEventOrigin.Provider, new[] { new EventContinuityRef(1, "Earth-616") }, neighbours, null));

        Assert.Equal("2007–2008 · 4 issues (2 core · 1 tie-in) · 3 series · 50% read", overview.StatsLine);
        Assert.Equal("ComicVine + Metron", overview.SourceLabel);
        Assert.Equal("Planet Hulk", Assert.Single(overview.Follows).Name);
        Assert.Equal("Aftersmash", Assert.Single(overview.FollowedBy).Name);
        Assert.Equal("Incredible Hulk #106", overview.Continue!.Label);
        Assert.Equal(9, overview.Continue.EventId);
        Assert.Equal("cover-2-1", overview.CollageKeys[0]);
    }

    [Theory]
    [InlineData("1", null, StoryEventOrigin.Provider, "ComicVine")]
    [InlineData(null, "1", StoryEventOrigin.Provider, "Metron")]
    [InlineData(null, null, StoryEventOrigin.User, "Yours")]
    [InlineData(null, null, StoryEventOrigin.Provider, null)]
    public void EventSourceLabel(string? cv, string? metron, StoryEventOrigin origin, string? expected) =>
        Assert.Equal(expected, EventOverviewBuilder.SourceLabel(cv, metron, origin));

    [Fact]
    public void EventAttention_FollowsTheLiveCounts()
    {
        var items = EventOverviewBuilder.Attention(3, 6, 1, "Hulk: Planet Hulk");

        Assert.Equal(new[] { "3 role suggestions to review", "6 issue suggestions", "1 connection to review", "Possible duplicate: Hulk: Planet Hulk" },
            items.Select(i => i.Text));
        Assert.Empty(EventOverviewBuilder.Attention(0, 0, 0, null));
    }
}
