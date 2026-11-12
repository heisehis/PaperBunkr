using Avalonia;
using Paperbunkr.App.Services.EventMap;

namespace Paperbunkr.App.Tests.EventMap;

public class EventMapZoomTests
{
    [Theory]
    [InlineData(23.99, EventMapCardLook.Dots)]
    [InlineData(24, EventMapCardLook.Chips)]
    [InlineData(80, EventMapCardLook.Compact)]
    [InlineData(130, EventMapCardLook.Standard)]
    [InlineData(190, EventMapCardLook.Covers)]
    public void LooksFollowScreenWidth(double width, EventMapCardLook expected) =>
        Assert.Equal(expected, EventMapCardLooks.For(width));

    [Fact]
    public void FitAccountsForBothDimensionsAndPinnedChrome()
    {
        var viewport = new Size(900, 500);
        double scale = EventMapZoom.FitScale(10, 5, viewport, 52, 28);
        var metrics = EventMapMetrics.ForScale(scale);
        Assert.True(10 * metrics.SlotWidth <= 848.00001);
        Assert.True(5 * metrics.TrackHeight <= 472.00001);
        Assert.Equal(848, 10 * metrics.SlotWidth, 6);
        Assert.Equal(EventMapZoom.AbsoluteMinimum, EventMapZoom.FitScale(10000, 10000, viewport, 52, 28));
        Assert.Equal(1, EventMapZoom.MinimumScaleForFit(2));
        Assert.Equal(0.4, EventMapZoom.MinimumScaleForFit(0.4));
    }

    [Fact]
    public void SliderRoundTripsAndZoomKeepsPointerAnchored()
    {
        const double minimum = 0.06;
        double scale = 0.7;
        Assert.Equal(scale, EventMapZoom.FromSlider(EventMapZoom.ToSlider(scale, minimum), minimum), 10);
        var offset = new Vector(100, 200);
        var pointer = new Point(80, 40);
        var result = EventMapZoom.ZoomAbout(scale, offset, pointer, 1.1, minimum);
        Assert.Equal((offset.X + pointer.X) / scale, (result.Offset.X + pointer.X) / result.Scale, 10);
        Assert.Equal((offset.Y + pointer.Y) / scale, (result.Offset.Y + pointer.Y) / result.Scale, 10);
        Assert.Equal(0.5, EventMapZoom.FitBand(4, 340), 10);
    }

    [Fact]
    public void CoversTrackHeightBlendsContinuously()
    {
        double threshold = 190d / 150;
        foreach (double scale in new[] { threshold * 0.8, threshold })
        {
            double before = EventMapMetrics.ForScale(scale - 0.000001).TrackHeight;
            double after = EventMapMetrics.ForScale(scale + 0.000001).TrackHeight;
            Assert.InRange(Math.Abs(after - before), 0, 0.01);
        }
    }

    [Fact]
    public void MetricsPreserveStandardAndBoundDotSize()
    {
        var standard = EventMapMetrics.ForScale(1);
        Assert.Equal(170, standard.SlotWidth);
        Assert.Equal(84, standard.TrackHeight);
        Assert.Equal(150, standard.CardWidth);
        Assert.Equal(64, standard.CardHeight);
        Assert.Equal(12, EventMapMetrics.ForScale(0.1).CardHeight);
        Assert.Equal(0, EventMapZoom.ToSlider(2, 2));
    }
}
