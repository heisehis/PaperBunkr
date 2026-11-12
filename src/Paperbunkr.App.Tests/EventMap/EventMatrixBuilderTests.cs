using Paperbunkr.App.Services.EventMap;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.EventMap.EventMapFixtures;

namespace Paperbunkr.App.Tests.EventMap;

public sealed class EventMatrixBuilderTests
{
    [Fact]
    public void SpineSegmentsIncludeBeforeAndTotalsWithLiveReadCounts()
    {
        var source = Source("Crisis", SpineSampleRows());
        var layout = Layout(source);
        var matrix = EventMatrixBuilder.Build(layout, readState: i => i == 0 ? EventMapReadState.Read : EventMapReadState.Unread);
        Assert.Equal(new[] { "Before", "#1", "#2", "Total" }, matrix.Columns.Select(c => c.Label));
        Assert.Equal(Crisis, matrix.Rows[0].SeriesId);
        var adventure = matrix.Rows.Single(r => r.SeriesId == SeriesA);
        Assert.Equal(1, adventure.Cells[0].Read);
        Assert.Equal(5, adventure.Cells[^1].Count);
    }

    [Fact]
    public void RelayChunksAndFoldedTracksKeepIndividualSeries()
    {
        var rows = Enumerable.Range(0, 12).Select(i => Row(10 + i, "1", i + 1)).ToArray();
        var layout = EventMapLayout.Compute(Source("Unmatched", rows), SpineChoice.Relay, EventMapFilter.All,
            EventMapDensity.Standard, relayPacked: true, foldShortLanes: true);
        Assert.Single(layout.Tracks);
        var matrix = EventMatrixBuilder.Build(layout, new HashSet<int> { 10 });
        Assert.Equal(new[] { "1–8", "9–16", "Total" }, matrix.Columns.Select(c => c.Label));
        Assert.Equal(11, matrix.Rows.Count);
        Assert.DoesNotContain(matrix.Rows, r => r.SeriesId == 10);
        Assert.All(matrix.Rows, r => Assert.Equal(1, r.Cells[^1].Count));
    }

    [Fact]
    public void EmptyBeforeIsOmittedAndPrologueIsNamed()
    {
        var source = Source("Crisis", Row(Crisis, "0", 1, EventMembershipRole.Prologue), Row(Crisis, "1", 2));
        var matrix = EventMatrixBuilder.Build(Layout(source));
        Assert.Equal(new[] { "Prologue", "#1", "Total" }, matrix.Columns.Select(c => c.Label));
    }
}
