using Paperbunkr.App.Services.EventMap;
using static Paperbunkr.App.Tests.EventMap.EventMapFixtures;

namespace Paperbunkr.App.Tests.EventMap;

/// <summary>Navigation, inspector links and the dimming set on <see cref="EventMapLayoutResult"/> (docs/superpowers/specs/2026-09-25-event-map-design.md §1).</summary>
public class EventMapNavigationTests
{
    private static EventMapLayoutResult SpineLayout(EventMapRow[]? rows = null) => Layout(Source("Crisis", rows ?? SpineSampleRows()));

    private static EventMapLayoutResult RelayLayout(EventMapRow[]? rows = null) => Layout(Source("Unrelated", rows ?? RelaySampleRows()));

    [Fact]
    public void NextAndPrev_FollowReadingOrder_InBothModes()
    {
        foreach (var layout in new[] { SpineLayout(), RelayLayout() })
        {
            Assert.Equal(4, layout.Next(3));
            Assert.Equal(2, layout.Prev(3));
            Assert.Null(layout.Prev(0));
            Assert.Null(layout.Next(layout.Cells.Count - 1));
        }
    }

    [Fact]
    public void FirstAndLast()
    {
        var layout = SpineLayout();

        Assert.Equal(0, layout.First());
        Assert.Equal(9, layout.Last());
    }

    [Fact]
    public void NearestInLane_PicksTheClosestColumnOnTheNeighbouringTrack()
    {
        var layout = SpineLayout();

        Assert.Equal(2, layout.NearestInLane(3, -1));   // B #1 (col 2) up → A #1 (col 2)
        Assert.Equal(6, layout.NearestInLane(7, +1));   // A #3 (col 5) down → B #2 (col 5)
    }

    [Fact]
    public void NearestInLane_Tie_EarlierColumnWins()
    {
        var layout = SpineLayout();

        Assert.Equal(0, layout.NearestInLane(1, +1));   // Crisis #1 (col 1) down → A #0 (col 0) and A #1 (col 2) tie → col 0
    }

    [Fact]
    public void NearestInLane_AtTheGridEdges_ReturnsNull()
    {
        var layout = SpineLayout();

        Assert.Null(layout.NearestInLane(1, -1));       // trunk is the top track
        Assert.Null(layout.NearestInLane(3, +1));       // lane B is the bottom track
    }

    [Fact]
    public void FirstUnread_SpineMode_IsTheFirstUnreadTrunkCard()
    {
        var rows = SpineSampleRows();
        rows[1] = rows[1] with { ReadState = EventMapReadState.Read };        // Crisis #1 read
        rows[0] = rows[0] with { ReadState = EventMapReadState.Unread };      // a lane card before it stays unread

        Assert.Equal(5, SpineLayout(rows).FirstUnread());
    }

    [Fact]
    public void FirstUnread_RelayMode_IsTheFirstUnreadCard_InProgressCountsAsUnread()
    {
        var rows = RelaySampleRows();
        rows[0] = rows[0] with { ReadState = EventMapReadState.Read };
        rows[1] = rows[1] with { ReadState = EventMapReadState.InProgress };

        Assert.Equal(1, RelayLayout(rows).FirstUnread());
    }

    [Fact]
    public void FirstUnread_AllRead_FallsBackToTheFirstCard()
    {
        var rows = RelaySampleRows().Select(r => r with { ReadState = EventMapReadState.Read }).ToArray();

        Assert.Equal(0, RelayLayout(rows).FirstUnread());
    }

    [Fact]
    public void Links_SpineMode_LaneNeighboursTrunkAnchorAndSegmentOrder()
    {
        var links = SpineLayout().LinksFor(7);                     // A #3

        Assert.Equal(4, links.Follows);                            // A #2
        Assert.Equal(9, links.LeadsTo);                            // A #4
        Assert.Equal(5, links.TiesInto);                           // Crisis #2
        Assert.Null(links.PreviousInSeries);
        Assert.Equal(new[] { 5, 6, 7, 8, 9 }, links.SegmentOrder);
    }

    [Fact]
    public void Links_SpineMode_TrunkCard_FollowsAlongTheTrunk_AndTiesIntoNothing()
    {
        var links = SpineLayout().LinksFor(5);                     // Crisis #2

        Assert.Equal(1, links.Follows);
        Assert.Null(links.LeadsTo);
        Assert.Null(links.TiesInto);
    }

    [Fact]
    public void Links_SpineMode_SegmentZero_TiesIntoNothing()
    {
        var links = SpineLayout().LinksFor(0);

        Assert.Null(links.TiesInto);
        Assert.Equal(new[] { 0 }, links.SegmentOrder);
    }

    [Fact]
    public void Links_RelayMode_ChainNeighboursAndPreviousInSeries()
    {
        var links = RelayLayout().LinksFor(5);                     // Y2

        Assert.Equal(4, links.Follows);
        Assert.Equal(6, links.LeadsTo);
        Assert.Equal(2, links.PreviousInSeries);                   // Y1
        Assert.Null(links.TiesInto);
        Assert.Empty(links.SegmentOrder);
    }

    [Fact]
    public void RelatedSet_SpineMode_LaneSegmentTrunkAndTrunkNeighbours()
    {
        var related = SpineLayout().RelatedSet(7);                 // A #3

        Assert.Equal(new[] { 0, 1, 2, 4, 5, 7, 9 }, related.OrderBy(i => i));
    }

    [Fact]
    public void RelatedSet_RelayMode_ChainNeighboursAndLane()
    {
        var related = RelayLayout().RelatedSet(5);                 // Y2

        Assert.Equal(new[] { 2, 4, 5, 6, 8, 11 }, related.OrderBy(i => i));
    }
}
