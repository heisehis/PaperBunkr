using Avalonia;
using Paperbunkr.App.Controls.EventMap;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests.EventMap;

/// <summary>
/// <see cref="ContinuityMapBuilder"/>, the block-relay layout and the ruler's band hit-testing (docs/superpowers/specs/
/// 2026-09-27-continuity-map-design.md §3-§4). Pure: the data is built by hand.
/// </summary>
public class ContinuityMapTests
{
    private const int Hulk = 1, Thor = 2, Aftersmash = 9;
    private const int PlanetHulk = 100, WorldWarHulk = 200;

    internal static EventMapRow Row(int issueId, int seriesId, string number, int? year, int? month = 1,
        EventMembershipRole role = EventMembershipRole.Core, bool outside = false)
    {
        string series = seriesId switch { Hulk => "Hulk", Thor => "Thor", Aftersmash => "WWH Aftersmash", _ => $"S{seriesId}" };
        return new EventMapRow(issueId, issueId, 0, role, seriesId, series, number, year, month, false, EventMapReadState.Unread, OutsideContinuity: outside);
    }

    /// <summary>
    /// Hulk and Thor are the continuity's series. Planet Hulk (2006) is the prequel of World War Hulk (2007); Hulk #93 is in both;
    /// World War Hulk also has an issue from a series outside the continuity. Loose issues: Hulk #1 (1990) and Thor #1 (Jan 2006)
    /// before Planet Hulk, Thor #2 (Dec 2006) between the two, Thor #3 undated.
    /// </summary>
    internal static ContinuityMapData Sample(IReadOnlyList<ContinuityRelationData>? relations = null)
    {
        var hulk92 = Row(92, Hulk, "92", 2006, 4);
        var hulk93 = Row(93, Hulk, "93", 2006, 5);
        var hulk105 = Row(105, Hulk, "105", 2007, 6, EventMembershipRole.Optional);
        var smash = Row(900, Aftersmash, "1", 2007, 9, outside: true);
        var events = new List<ContinuityEventData>
        {
            new(WorldWarHulk, "World War Hulk", 200706, 200709, new[] { hulk93, hulk105, smash }),
            new(PlanetHulk, "Planet Hulk", 200604, 200605, new[] { hulk92, hulk93 }),
        };
        var loose = new[] { Row(1, Hulk, "1", 1990), Row(11, Thor, "1", 2006, 1), Row(12, Thor, "2", 2006, 12), Row(13, Thor, "3", null, null) };
        var byIssue = new Dictionary<int, IReadOnlyList<EventMapEventRef>>
        {
            [92] = new[] { new EventMapEventRef(PlanetHulk, "Planet Hulk") },
            [93] = new[] { new EventMapEventRef(PlanetHulk, "Planet Hulk"), new EventMapEventRef(WorldWarHulk, "World War Hulk") },
            [105] = new[] { new EventMapEventRef(WorldWarHulk, "World War Hulk") },
            [900] = new[] { new EventMapEventRef(WorldWarHulk, "World War Hulk") },
        };
        return new ContinuityMapData(7, "Earth-616", new[] { new ContinuityLane(Hulk, "Hulk", false), new ContinuityLane(Thor, "Thor", false) },
            events, relations ?? new[] { new ContinuityRelationData(PlanetHulk, WorldWarHulk, RelationType.Prequel, true, "yours") },
            loose, byIssue, PendingDuplicatePairs: 1);
    }

    private static ContinuityMapSource Build(ContinuityMapData data, ContinuityMapOptions? options = null) =>
        ContinuityMapBuilder.Build(data, options ?? ContinuityMapOptions.Default);

    [Fact]
    public void EventsMode_EventsInChronology_LooseIssuesBetweenThemByDate()
    {
        var map = Build(Sample());

        Assert.False(map.IsPublicationOrder);
        Assert.Equal(new[]
        {
            (EventMapBlockKind.Between, "Between events · 1990–2006"),
            (EventMapBlockKind.Event, "Planet Hulk"),
            (EventMapBlockKind.Between, "Between events · 2006"),
            (EventMapBlockKind.Event, "World War Hulk"),
            (EventMapBlockKind.Between, "Outside events · undated"),
        }, map.Blocks.Select(b => (b.Kind, b.Label)));
        Assert.Equal(new[] { 1, 11, 92, 93, 12, 105, 900, 13 }, map.Rows.Select(r => r.IssueId));
        Assert.Equal(4, map.EventIssueCount);
        Assert.Equal(4, map.LooseIssueCount);
    }

    [Fact]
    public void AnIssueInTwoEvents_AppearsOnce_WithAlsoIn()
    {
        var map = Build(Sample());

        var row = Assert.Single(map.Rows, r => r.IssueId == 93);
        Assert.Equal(PlanetHulk, row.OwnerEventId);
        Assert.Equal("World War Hulk", Assert.Single(row.AlsoIn!).Name);
        Assert.Null(map.Rows.Single(r => r.IssueId == 12).OwnerEventId);
    }

    [Fact]
    public void Lanes_MemberSeriesInOrder_ThenOutsideSeriesFlagged()
    {
        var map = Build(Sample());

        Assert.Equal(new[] { ("Hulk", false), ("Thor", false), ("WWH Aftersmash", true) }, map.Lanes.Select(l => (l.Name, l.Outside)));
    }

    [Fact]
    public void Connectors_RunEarlierToLater_BetweenEventBlocks()
    {
        var connector = Assert.Single(Build(Sample()).Connectors);

        Assert.Equal((1, 3, "Prequel", true, true), (connector.FromBlock, connector.ToBlock, connector.Label, connector.IsOrdered, connector.IsYours));
        Assert.Equal("Prequel · yours", connector.Tooltip);
    }

    [Fact]
    public void RelationsBeatDates_ForTheEventOrder()
    {
        // A (wrong-looking but stored) Sequel makes Planet Hulk come after World War Hulk despite its dates.
        var map = Build(Sample(new[] { new ContinuityRelationData(PlanetHulk, WorldWarHulk, RelationType.Sequel, false, "inferred · x") }));

        var events = map.Blocks.Where(b => b.Kind == EventMapBlockKind.Event).Select(b => b.Label).ToList();
        Assert.Equal(new[] { "World War Hulk", "Planet Hulk" }, events);
        Assert.False(Assert.Single(map.Connectors).IsYours);
    }

    [Fact]
    public void EventsOnly_DropsTheBetweenEventsBlocks()
    {
        var map = Build(Sample(), new ContinuityMapOptions(new HashSet<int>(), false, EventsOnly: true));

        Assert.All(map.Blocks, b => Assert.Equal(EventMapBlockKind.Event, b.Kind));
        Assert.Equal(new[] { 92, 93, 105, 900 }, map.Rows.Select(r => r.IssueId));
    }

    [Fact]
    public void HideOptional_DropsOptionalMembers()
    {
        var map = Build(Sample(), new ContinuityMapOptions(new HashSet<int>(), HideOptional: true, false));

        Assert.DoesNotContain(map.Rows, r => r.IssueId == 105);
    }

    [Fact]
    public void AHiddenEvent_FreesItsIssues_ForTheNextEvent()
    {
        var map = Build(Sample(), new ContinuityMapOptions(new HashSet<int> { PlanetHulk }, false, false));

        Assert.DoesNotContain(map.Blocks, b => b.EventId == PlanetHulk);
        Assert.Equal(WorldWarHulk, map.Rows.Single(r => r.IssueId == 93).OwnerEventId);
        Assert.DoesNotContain(map.Rows, r => r.IssueId == 92);
        Assert.Empty(map.Connectors);
        Assert.Equal(2, map.Events.Count);          // the picker still lists both
    }

    [Fact]
    public void NoEvents_IsPublicationOrder_InYearBlocks_UndatedLast()
    {
        var data = Sample() with { Events = Array.Empty<ContinuityEventData>(), Relations = Array.Empty<ContinuityRelationData>() };

        var map = Build(data);

        Assert.True(map.IsPublicationOrder);
        Assert.Equal(new[] { "1990", "2006", "Undated" }, map.Blocks.Select(b => b.Label));
        Assert.All(map.Blocks, b => Assert.Equal(EventMapBlockKind.Year, b.Kind));
        Assert.Equal(new[] { 1, 11, 12, 13 }, map.Rows.Select(r => r.IssueId));
    }

    [Fact]
    public void PublicationOrder_SameMonth_SeriesOrderThenNumber()
    {
        var rows = new[] { Row(3, Thor, "2", 2006, 5), Row(2, Hulk, "10", 2006, 5), Row(1, Hulk, "9", 2006, 5) };

        var sorted = ContinuityMapBuilder.SortLoose(rows, new Dictionary<int, int> { [Hulk] = 0, [Thor] = 1 });

        Assert.Equal(new[] { 1, 2, 3 }, sorted.Select(r => r.IssueId));
    }

    [Fact]
    public void PublicationOrder_PrefersGcdOnSaleDate_OverCoverDate()
    {
        // Cover-dated March, but GCD says on sale in January: it goes before a February cover.
        var rows = new[] { Row(1, Hulk, "1", 2006, 2), Row(2, Thor, "1", 2006, 3) with { SortDate = 200601 } };

        var sorted = ContinuityMapBuilder.SortLoose(rows, new Dictionary<int, int> { [Hulk] = 0, [Thor] = 1 });

        Assert.Equal(new[] { 2, 1 }, sorted.Select(r => r.IssueId));
    }

    [Fact]
    public void Lanes_ContinuedSeriesSitNextToEachOther_OlderFirst_WithMarker()
    {
        // Continuity order: Hulk (2008), Thor, Incredible Hulk (1968); Hulk (2008) continues Incredible Hulk.
        var lanes = new[] { new ContinuityLane(3, "Hulk (2008)", false), new ContinuityLane(Thor, "Thor", false), new ContinuityLane(Hulk, "Incredible Hulk", false) };

        var ordered = ContinuityMapLoader.OrderByContinuation(lanes, new[] { (Newer: 3, Older: Hulk) });

        Assert.Equal(new[] { Hulk, 3, Thor }, ordered.Select(l => l.SeriesId));
        Assert.Equal("Hulk (2008)", ordered[0].ContinuesAs);
        Assert.Null(ordered[1].ContinuesAs);
        Assert.Null(ordered[2].ContinuesAs);
    }

    [Fact]
    public void Lanes_ChainOfThree_AndACycle_DoNotLoop()
    {
        var lanes = new[] { new ContinuityLane(3, "C", false), new ContinuityLane(1, "A", false), new ContinuityLane(2, "B", false) };

        var chain = ContinuityMapLoader.OrderByContinuation(lanes, new[] { (Newer: 2, Older: 1), (Newer: 3, Older: 2) });
        var cycle = ContinuityMapLoader.OrderByContinuation(lanes, new[] { (Newer: 2, Older: 1), (Newer: 1, Older: 2) });

        Assert.Equal(new[] { 1, 2, 3 }, chain.Select(l => l.SeriesId));
        Assert.Equal(new[] { "B", "C", null }, chain.Select(l => l.ContinuesAs));
        Assert.Equal(3, cycle.Count);
        Assert.Equal(3, cycle.Select(l => l.SeriesId).Distinct().Count());
    }

    [Fact]
    public void BetweenBlocks_ArePackedByMonth_EventBlocksStayOnePerColumn()
    {
        // Publication order (no events): Nov 2003 has Hulk #37, Hulk #38 and Thor #12; Dec 2003 has Thor #13.
        var loose = new[]
        {
            Row(1, Hulk, "37", 2003, 11), Row(2, Hulk, "38", 2003, 11), Row(3, Thor, "12", 2003, 11), Row(4, Thor, "13", 2003, 12),
        };
        var data = new ContinuityMapData(7, "Earth-1610", new[] { new ContinuityLane(Hulk, "Hulk", false), new ContinuityLane(Thor, "Thor", false) },
            Array.Empty<ContinuityEventData>(), Array.Empty<ContinuityRelationData>(), loose,
            new Dictionary<int, IReadOnlyList<EventMapEventRef>>(), PendingDuplicatePairs: 0);

        var map = Build(data);
        var layout = EventMapLayout.ComputeBlocks(map, EventMapDensity.Standard);
        var column = layout.Cells.ToDictionary(c => c.Row.IssueId, c => c.Column);

        // Nov 2003 is two columns wide (Hulk has two that month); Thor #12 shares the first; Dec 2003 follows.
        Assert.Equal((0, 1, 0, 2), (column[1], column[2], column[3], column[4]));
        Assert.Equal(3, layout.ColumnCount);
        Assert.Equal(new[] { "Nov 2003", null, "Dec 2003" },
            Enumerable.Range(0, 3).Select(layout.ColumnLabel).Select(l => l?.Replace(".", "")));
        Assert.Equal((0, 2), layout.BlockColumns(map.Blocks.Single()));

        // The events sample keeps one card per column inside each event block.
        var events = Build(Sample());
        var eventLayout = EventMapLayout.ComputeBlocks(events, EventMapDensity.Standard);
        foreach (var block in events.Blocks.Where(b => b.Kind == EventMapBlockKind.Event))
        {
            var (first, last) = eventLayout.BlockColumns(block);
            Assert.Equal(block.LastRow - block.FirstRow, last - first);
        }
    }

    [Fact]
    public void LaneHeader_SaysContinuesAs()
    {
        var header = new Paperbunkr.App.ViewModels.EventMapLaneHeader("Hulk", "Hulk", 2, 0, false, 40, ContinuesAs: "Hulk (2008)");

        Assert.Equal("2 issues · continues as ↓", header.CountLabel);
    }

    [Fact]
    public void BlockLayout_ChainsInsideEvents_LaneLinesInsideOtherBlocks_NothingAcrossABoundary()
    {
        var map = Build(Sample());
        var layout = EventMapLayout.ComputeBlocks(map, EventMapDensity.Standard);

        Assert.True(layout.IsBlockMode);
        Assert.Equal(Enumerable.Range(0, map.Rows.Count), layout.Cells.Select(c => c.Column));
        foreach (var edge in layout.Edges)
        {
            var from = layout.Cells[edge.From];
            var to = layout.Cells[edge.To];
            Assert.Equal(from.Segment, to.Segment);                                         // same block
            var block = map.Blocks[from.Segment];
            Assert.Equal(block.Kind == EventMapBlockKind.Event ? EventMapEdgeKind.Chain : EventMapEdgeKind.Sequence, edge.Kind);
            if (edge.Kind == EventMapEdgeKind.Sequence)
            {
                Assert.Equal(from.Track, to.Track);
            }
        }

        Assert.Contains(layout.Edges, e => e.Kind == EventMapEdgeKind.Chain && layout.Cells[e.From].Row.IssueId == 92 && layout.Cells[e.To].Row.IssueId == 93);
        Assert.True(layout.Tracks[2].IsOutside);
    }

    [Fact]
    public void Ruler_HitTests_BandsAndConnectors()
    {
        var map = Build(Sample());
        var layout = EventMapLayout.ComputeBlocks(map, EventMapDensity.Standard);   // slot 170: blocks at columns 0-1, 2-3, 4, 5-6, 7

        Assert.Equal(1, EventMapRuler.BlockAt(layout, offsetX: 0, x: 2.5 * 170));
        Assert.Equal(3, EventMapRuler.BlockAt(layout, offsetX: 340, x: 3.5 * 170));     // scrolled two columns: column 5.5
        Assert.Null(EventMapRuler.BlockAt(layout, 0, 50 * 170));

        double x1 = EventMapRuler.BlockCentre(layout, 0, 1);
        double x2 = EventMapRuler.BlockCentre(layout, 0, 3);
        Assert.Equal(3 * 170, x1);
        Assert.Equal(6 * 170, x2);
        var hit = EventMapRuler.ConnectorAt(layout, map.Connectors, 0, new Point((x1 + x2) / 2, EventMapRuler.ConnectorY - 2));
        Assert.Equal("Prequel · yours", hit?.Tooltip);
        Assert.Null(EventMapRuler.ConnectorAt(layout, map.Connectors, 0, new Point((x1 + x2) / 2, 5)));
    }
}
