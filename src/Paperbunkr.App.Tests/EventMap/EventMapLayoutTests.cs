using Avalonia;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.EventMap.EventMapFixtures;

namespace Paperbunkr.App.Tests.EventMap;

/// <summary>Covers <see cref="EventMapLayout.Compute"/> (docs/superpowers/specs/2026-09-25-event-map-design.md §1 "Layout").</summary>
public class EventMapLayoutTests
{
    // ----- Spine mode -----

    [Fact]
    public void Spine_Sample_CompactColumns()
    {
        var layout = Layout(Source("Crisis", SpineSampleRows()));

        Assert.True(layout.IsSpineMode);
        Assert.Equal(new[] { 0, 1, 2, 2, 3, 4, 5, 5, 6, 6 }, layout.Cells.Select(c => c.Column));
        Assert.Equal(7, layout.ColumnCount);
        Assert.Equal(new[] { 1, 0, 1, 2, 1, 0, 2, 1, 2, 1 }, layout.Cells.Select(c => c.Track));
    }

    [Fact]
    public void Spine_Sample_Segments()
    {
        var layout = Layout(Source("Crisis", SpineSampleRows()));

        Assert.Equal(new[] { 0, 1, 1, 1, 1, 2, 2, 2, 2, 2 }, layout.Cells.Select(c => c.Segment));
        Assert.Null(layout.Segments[0].TrunkCell);
        Assert.Equal(1, layout.Segments[1].TrunkCell);
        Assert.Equal(2, layout.Segments[1].StartColumn);
        Assert.Equal(5, layout.Segments[2].TrunkCell);
        Assert.Equal(5, layout.Segments[2].StartColumn);
    }

    [Fact]
    public void Spine_TrunkIsSpineRowsPlusPrologueAndEpilogue_CoreNeverPromotes()
    {
        var layout = Layout(Source("Crisis",
            Row(SeriesB, "0", 1, EventMembershipRole.Prologue),
            Row(Crisis, "1", 2),
            Row(SeriesB, "1", 3, EventMembershipRole.Core),
            Row(SeriesA, "9", 4, EventMembershipRole.Epilogue)));

        Assert.Equal(new[] { true, true, false, true }, layout.Cells.Select(c => c.IsTrunk));
        Assert.Equal(new[] { 0, 1, 2, 3 }, layout.Cells.Select(c => c.Column));
    }

    [Fact]
    public void Spine_SeriesWithNoRowsLeftAfterPromotion_GetsNoLane()
    {
        var layout = Layout(Source("Crisis",
            Row(SeriesB, "0", 1, EventMembershipRole.Prologue),
            Row(Crisis, "1", 2),
            Row(SeriesB, "1", 3),
            Row(SeriesA, "9", 4, EventMembershipRole.Epilogue)));

        Assert.Equal(2, layout.TrackCount);
        Assert.True(layout.Tracks[0].IsTrunk);
        Assert.Equal(SeriesB, layout.Tracks[1].SeriesId);
    }

    [Fact]
    public void Spine_LanesOrderedBySeriesFirstPosition()
    {
        var layout = Layout(Source("Crisis", Row(Crisis, "1", 1), Row(SeriesB, "1", 2), Row(SeriesA, "1", 3), Row(SeriesB, "2", 4)));

        Assert.Equal(new[] { Crisis, SeriesB, SeriesA }, layout.Tracks.Select(t => t.SeriesId));
        Assert.Equal(new[] { -1, 0, 1 }, layout.Tracks.Select(t => t.ColorIndex));
        Assert.Equal(new[] { 1, 2, 1 }, layout.Tracks.Select(t => t.Count));
    }

    [Fact]
    public void Spine_TieIns_OnlyToFirstRowOfEachLanePerSegment_NoneFromSegmentZero()
    {
        var layout = Layout(Source("Crisis", SpineSampleRows()));

        var tieIns = layout.Edges.Where(e => e.Kind == EventMapEdgeKind.TieIn).Select(e => (e.From, e.To)).OrderBy(x => x).ToList();

        Assert.Equal(new[] { (1, 2), (1, 3), (5, 6), (5, 7) }, tieIns);
    }

    [Fact]
    public void Spine_SequenceEdges_WithinLanesAndAlongTheTrunk()
    {
        var layout = Layout(Source("Crisis", SpineSampleRows()));

        var sequence = layout.Edges.Where(e => e.Kind == EventMapEdgeKind.Sequence).Select(e => (e.From, e.To)).OrderBy(x => x).ToList();

        Assert.Equal(new[] { (0, 2), (1, 5), (2, 4), (3, 6), (4, 7), (6, 8), (7, 9) }, sequence);
    }

    [Fact]
    public void Spine_ContinuityEdge_FromPromotedRowToNextRowOfItsSeries()
    {
        var layout = Layout(Source("Crisis",
            Row(SeriesB, "0", 1, EventMembershipRole.Prologue),
            Row(Crisis, "1", 2),
            Row(SeriesB, "1", 3),
            Row(SeriesA, "9", 4, EventMembershipRole.Epilogue)));

        var continuity = Assert.Single(layout.Edges, e => e.Kind == EventMapEdgeKind.Continuity);
        Assert.Equal((0, 2), (continuity.From, continuity.To));
    }

    [Fact]
    public void Spine_OnlyTheTrunk_IsAnOrderedStrip()
    {
        var layout = Layout(Source("Crisis", Row(Crisis, "1", 1), Row(Crisis, "2", 2), Row(Crisis, "3", 3)));

        Assert.True(layout.IsSpineMode);
        Assert.Equal(1, layout.TrackCount);
        Assert.Equal(new[] { 0, 1, 2 }, layout.Cells.Select(c => c.Column));
    }

    // ----- Relay mode -----

    [Fact]
    public void Relay_Sample_StrictColumns_ThreeLanes()
    {
        var layout = Layout(Source("Unrelated", RelaySampleRows()));

        Assert.False(layout.IsSpineMode);
        Assert.Equal(Enumerable.Range(0, 12), layout.Cells.Select(c => c.Column));
        Assert.Equal(12, layout.ColumnCount);
        Assert.Equal(new[] { X, Y, Z }, layout.Tracks.Select(t => t.SeriesId));
        Assert.Equal(new[] { 0, 0, 1, 0, 2, 1, 2, 0, 1, 2, 0, 1 }, layout.Cells.Select(c => c.Track));
    }

    [Fact]
    public void Relay_ChainEdges_BetweenEveryConsecutivePair_HopsMuted()
    {
        var layout = Layout(Source("Unrelated", RelaySampleRows()));

        Assert.All(layout.Edges, e => Assert.Equal(EventMapEdgeKind.Chain, e.Kind));
        Assert.Equal(Enumerable.Range(0, 11).Select(i => (i, i + 1)), layout.Edges.Select(e => (e.From, e.To)));
        Assert.Equal(0, layout.Edges[0].ColorIndex);                          // X1 → X2 stays in lane X
        Assert.Equal(EventMapLayout.MutedColor, layout.Edges[1].ColorIndex);  // X2 → Y1 hops
    }

    // ----- Filters -----

    [Fact]
    public void Filter_SpineOnly_KeepsTrunkRowsOnly()
    {
        var layout = Layout(Source("Crisis", SpineSampleRows()), EventMapFilter.SpineOnly);

        Assert.Equal(2, layout.Cells.Count);
        Assert.All(layout.Cells, c => Assert.True(c.IsTrunk));
        Assert.Equal(new[] { 0, 1 }, layout.Cells.Select(c => c.Column));
        Assert.Equal(1, layout.TrackCount);
    }

    [Fact]
    public void Filter_HideOptional_FreesColumns()
    {
        var rows = SpineSampleRows();
        rows[4] = rows[4] with { Role = EventMembershipRole.Optional };    // A #2

        var layout = Layout(Source("Crisis", rows), EventMapFilter.HideOptional);

        Assert.Equal(9, layout.Cells.Count);
        Assert.Equal(3, layout.Cells.Single(c => c.IsTrunk && c.Row.Number == "2").Column);  // was 4
        Assert.Equal(6, layout.ColumnCount);
    }

    [Fact]
    public void TiedPositions_OrderByMembershipId()
    {
        var layout = Layout(Source("Unrelated",
            Row(SeriesA, "2", 5, membershipId: 20),
            Row(SeriesB, "1", 5, membershipId: 10)));

        Assert.Equal(new[] { 10, 20 }, layout.Cells.Select(c => c.Row.MembershipId));
    }

    [Fact]
    public void NoMembers_GivesAnEmptyResult()
    {
        var layout = Layout(Source("Crisis"));

        Assert.True(layout.IsEmpty);
        Assert.Equal(0, layout.ColumnCount);
        Assert.Equal(0, layout.TrackCount);
        Assert.True(layout.VisibleRange(new Rect(0, 0, 500, 500)).IsEmpty);
        Assert.Null(layout.FirstUnread());
    }

    [Fact]
    public void MissingFile_IsLaidOutNormally()
    {
        var rows = SpineSampleRows();
        rows[2] = rows[2] with { FileIsMissing = true };

        var layout = Layout(Source("Crisis", rows));

        Assert.Equal(10, layout.Cells.Count);
        Assert.True(layout.Cells[2].Row.FileIsMissing);
    }

    // ----- Density stops -----

    [Theory]
    [InlineData(EventMapDensity.Compact, 245, 51, 100, 30)]
    [InlineData(EventMapDensity.Standard, 350, 94, 150, 64)]
    [InlineData(EventMapDensity.Covers, 313, 232, 124, 196)]
    public void CardRect_CentredInItsCell_PerDensityStop(EventMapDensity density, double x, double y, double w, double h)
    {
        var layout = Layout(Source("Crisis", SpineSampleRows()), density: density);

        Assert.Equal(new Rect(x, y, w, h), layout.CardRect(2));    // A #1: column 2, track 1
    }

    [Fact]
    public void TotalSize_IsColumnsBySlotWidth_TracksByTrackHeight()
    {
        var layout = Layout(Source("Crisis", SpineSampleRows()), density: EventMapDensity.Compact);

        Assert.Equal(new Size(7 * 118, 3 * 44), layout.TotalSize);
    }

    // ----- Edge geometry (Standard: slot 170, track 84, card 150×64 → 10 px padding each side) -----

    [Fact]
    public void EdgePoints_TieIn_DownTheTrunkColumn_AlongTheGutter_IntoTheTarget()
    {
        var layout = Layout(Source("Crisis", SpineSampleRows()));
        var edge = layout.Edges.Single(e => e.Kind == EventMapEdgeKind.TieIn && e.From == 1 && e.To == 2);

        Assert.Equal(new[] { new Point(255, 74), new Point(255, 89), new Point(425, 89), new Point(425, 94) }, layout.EdgePoints(edge));
    }

    [Fact]
    public void EdgePoints_ChainHop_Down()
    {
        var layout = Layout(Source("Unrelated", RelaySampleRows()));

        Assert.Equal(new[] { new Point(330, 42), new Point(340, 42), new Point(340, 126), new Point(350, 126) }, layout.EdgePoints(layout.Edges[1]));
    }

    [Fact]
    public void EdgePoints_ChainHop_Up()
    {
        var layout = Layout(Source("Unrelated", RelaySampleRows()));

        Assert.Equal(new[] { new Point(500, 126), new Point(510, 126), new Point(510, 42), new Point(520, 42) }, layout.EdgePoints(layout.Edges[2]));
    }

    [Fact]
    public void EdgePoints_Sequence_IsAStraightLine()
    {
        var layout = Layout(Source("Unrelated", RelaySampleRows()));

        Assert.Equal(new[] { new Point(160, 42), new Point(180, 42) }, layout.EdgePoints(layout.Edges[0]));
    }

    // ----- Visible range (Standard, 7 columns × 3 tracks) -----

    [Fact]
    public void VisibleRange_AddsOneColumnOfOverscan_ClampedAtTheStart()
    {
        var layout = Layout(Source("Crisis", SpineSampleRows()));

        Assert.Equal(new EventMapVisibleRange(0, 2, 0, 2), layout.VisibleRange(new Rect(0, 0, 340, 168)));
    }

    [Fact]
    public void VisibleRange_Middle()
    {
        var layout = Layout(Source("Crisis", SpineSampleRows()));

        Assert.Equal(new EventMapVisibleRange(1, 5, 0, 1), layout.VisibleRange(new Rect(400, 0, 300, 84)));
    }

    [Fact]
    public void VisibleRange_ClampedAtTheEnd()
    {
        var layout = Layout(Source("Crisis", SpineSampleRows()));

        Assert.Equal(new EventMapVisibleRange(4, 6, 0, 2), layout.VisibleRange(new Rect(1000, 0, 500, 500)));
    }

    [Fact]
    public void CellsIn_ReturnsOnlyCellsInsideTheRange()
    {
        var layout = Layout(Source("Crisis", SpineSampleRows()));

        var inRange = layout.CellsIn(new EventMapVisibleRange(4, 5, 0, 1)).OrderBy(i => i).ToList();

        Assert.Equal(new[] { 5, 7 }, inRange);   // Crisis #2 (col 4, trunk) and A #3 (col 5, lane A); B #2 is on track 2
    }
}
