using Avalonia.Media;
using Paperbunkr.App.Services;
using Paperbunkr.App.Views.Stats;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

/// <summary>docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #16 - chart hover text, colour contrast, and the reduced-motion donut sweep.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class InsightsChartPolishTests
{
    private static readonly IReadOnlyList<(string Label, int Value)> Bars = new[] { ("Jan", 4), ("Feb", 9), ("Mar", 0) };

    [Fact]
    public void Bar_HoverOnTheBar_DescribesLabelAndValue()
        => Assert.Equal("Feb: 9 finished", InsightsChartHover.DescribeBar(1.05, 4.0, Bars, "finished"));

    [Theory]
    [InlineData(0.55, 2.0)]   // in the gutter between bars 0 and 1 (beyond 0.4 from either centre)
    [InlineData(1.0, 12.0)]   // above the bar
    [InlineData(1.0, -1.0)]   // below the baseline
    [InlineData(-0.6, 1.0)]   // left of the first bar
    [InlineData(3.0, 1.0)]    // right of the last bar
    public void Bar_HoverOffTheBar_ShowsNothing(double x, double y)
        => Assert.Null(InsightsChartHover.DescribeBar(x, y, Bars, "finished"));

    [Fact]
    public void Bar_ZeroValueBar_IsStillHoverableAtTheBaseline()
        => Assert.Equal("Mar: 0 finished", InsightsChartHover.DescribeBar(2.0, 0.5, Bars, "finished"));

    [Fact]
    public void Bar_NoData_ShowsNothing()
        => Assert.Null(InsightsChartHover.DescribeBar(0, 0, Array.Empty<(string, int)>(), "x"));

    [Fact]
    public void Growth_SingleSeries_ShowsTheMonthAndItsRunningTotal()
    {
        var text = InsightsChartHover.DescribeGrowth(1.2, new[] { "Jan 26", "Feb 26" }, new[] { ("Total", new[] { 5.0, 12.0 }) });

        Assert.Equal("Feb 26 · 12", text);
    }

    [Fact]
    public void Growth_MultipleSeries_AreListedLargestFirst()
    {
        var text = InsightsChartHover.DescribeGrowth(0, new[] { "Jan 26" }, new[] { ("Reading", new[] { 3.0 }), ("Finished", new[] { 40.0 }) });

        Assert.Equal("Jan 26 · Finished 40 · Reading 3", text);
    }

    [Theory]
    [InlineData(-0.6)]
    [InlineData(2.6)]
    public void Growth_OutsideTheMonths_ShowsNothing(double x)
        => Assert.Null(InsightsChartHover.DescribeGrowth(x, new[] { "Jan", "Feb", "Mar" }, new[] { ("t", new[] { 1.0, 2.0, 3.0 }) }));

    [Fact]
    public void EveryBuiltInSkin_KeepsEveryChartColour_AtLeastThreeToOneAgainstItsBackground()
    {
        var service = new ThemeService();
        foreach (string key in new[] { "default", "windows_11", "cool_technical", "vibrant_pop", "vintage_paperback", "daylight", "overcast", "maximum_contrast", "colorblind_safe", "matrix" })
        {
            var theme = service.LoadTheme(key);
            var bg = Color.Parse(theme.Colors.Bg);
            foreach (string hex in new[] { theme.Colors.Accent, theme.Colors.ChartBlue, theme.Colors.Badge, theme.Colors.Success, theme.Colors.ChartViolet, "#D96C6C" /* PbDangerColor is app-wide, not skin-defined */ })
            {
                var c = Color.Parse(hex);
                var adjusted = InsightsChartTheme.EnsureContrast(new ScottPlot.Color(c.R, c.G, c.B), bg);

                double ratio = ThemeService.ContrastRatio(Color.FromRgb(adjusted.R, adjusted.G, adjusted.B), bg);
                Assert.True(ratio >= InsightsChartTheme.MinGraphicContrast - 0.01, $"{key}: {hex} on {theme.Colors.Bg} is {ratio:0.00}:1 after adjustment");
            }
        }
    }

    [Fact]
    public void ColourThatAlreadyPasses_IsLeftExactlyAsTheSkinDefinedIt()
    {
        var white = new ScottPlot.Color(255, 255, 255);

        Assert.Equal(white, InsightsChartTheme.EnsureContrast(white, Colors.Black));
    }

    [Fact]
    public void Donut_WithNoMotionResource_IsAlwaysFullyDrawn()
    {
        var donut = new CategoryDonut();

        donut.Slices = new List<CompositionSlice> { new("A", 3), new("B", 1) };

        Assert.Equal(TimeSpan.Zero, donut.EntranceDuration());
        Assert.Equal(1.0, donut.SweepProgress); // never left at 0 (an invisible donut) when motion is unavailable/off
    }

    [Fact]
    public void Donut_UnderReducedMotion_IsFullyDrawnImmediately()
    {
        var donut = new CategoryDonut();
        donut.Resources["PbMotionLarge"] = TimeSpan.Zero;

        donut.Slices = new List<CompositionSlice> { new("A", 3) };

        Assert.Equal(1.0, donut.SweepProgress);
    }
}
