using Avalonia;
using Paperbunkr.App.Services.EventMap;
using static Paperbunkr.App.Tests.EventMap.EventMapFixtures;

namespace Paperbunkr.App.Tests.EventMap;

public class EventMapMappingTests
{
    [Theory]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void HitTestingUsesTheDrawnCardAndPinnedRail(double scale)
    {
        var layout = Layout(Source("Event", Row(SeriesA, "1", 1)));
        var metrics = EventMapMetrics.ForScale(scale);
        var offset = new Vector(2, 1);
        var rect = layout.CardRect(0, metrics);
        var point = new Point(rect.Center.X + 52 - offset.X, rect.Center.Y + 28 - offset.Y);
        Assert.Equal(new EventMapHit(EventMapHitKind.Card, 0), EventMapHitTest.At(point, layout, metrics, offset, 52, 28));
        Assert.Equal(EventMapHitKind.Corner, EventMapHitTest.At(new Point(1, 1), layout, metrics, offset, 52, 28).Kind);
        Assert.Equal(EventMapHitKind.Lane, EventMapHitTest.At(new Point(1, 30), layout, metrics, offset, 52, 28).Kind);
    }

    [Fact]
    public void OverviewProjectsViewportAndCentersClicks()
    {
        var layout = Layout(Source("Event", RelaySampleRows()));
        var metrics = EventMapMetrics.ForScale(1);
        var strip = new Size(240, 32);
        var viewport = new Size(340, 84);
        var offset = new Vector(170, 0);
        var box = EventMapOverviewMapping.ViewportBox(offset, viewport, layout, metrics, strip);
        Assert.Equal(offset.X / layout.TotalSizeFor(metrics).Width * strip.Width, box.X, 8);
        var center = EventMapOverviewMapping.CenterOffsetFor(box.Center, viewport, layout, metrics, strip);
        Assert.Equal(offset.X, center.X, 8);
        Assert.True(EventMapOverviewMapping.TickRect(layout.Cells[0], layout, strip).Width >= 1);
    }

    [Fact]
    public void SelectedDotUsesItsEnlargedHitArea()
    {
        var layout = Layout(Source("Event", Row(SeriesA, "1", 1)));
        var metrics = EventMapMetrics.ForScale(0.1);
        var dot = layout.CardRect(0, metrics);
        var point = new Point(52 + dot.Left - 1, 28 + dot.Center.Y);
        Assert.Equal(EventMapHitKind.Empty, EventMapHitTest.At(point, layout, metrics, default, 52, 28).Kind);
        Assert.Equal(new EventMapHit(EventMapHitKind.Card, 0), EventMapHitTest.At(point, layout, metrics, default, 52, 28, 0));
    }
}
