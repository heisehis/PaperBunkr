using System.Linq;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

/// <summary>Exercises <see cref="InsightsChartPalette"/> (docs/superpowers/specs/2026-10-04-insights-goal-outcomes-and-chart-colour-design.md).
/// Pure - no Avalonia application needed.</summary>
public class InsightsChartPaletteTests
{
    private static CompositionSlice S(string label, int count = 1) => new(label, count);

    [Theory]
    [InlineData("Completed", "Success")]
    [InlineData("Reading", "Accent")]
    [InlineData("Re-reading", "ChartViolet")]
    [InlineData("Planned", "ChartBlue")]
    [InlineData("Paused", "Badge")]
    [InlineData("Dropped", "Danger")]
    public void ReadingState_LabelsMapToTheirMeaningColour(string label, string expectedBase)
    {
        var slice = InsightsChartPalette.Layout(new[] { S(label) }, ChartCategoryKind.ReadingState).Single();
        Assert.Equal(expectedBase, slice.Base);
        Assert.False(slice.IsNeutral);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Other")]
    [InlineData("None")]
    public void NoInformationLabels_AreNeutral_InEveryKind(string label)
    {
        foreach (var kind in new[] { ChartCategoryKind.Positional, ChartCategoryKind.ReadingState, ChartCategoryKind.ContentRating })
        {
            var slice = InsightsChartPalette.Layout(new[] { S(label) }, kind).Single();
            Assert.True(slice.IsNeutral);
            Assert.Equal(InsightsChartPalette.NeutralBase, slice.Base);
        }
    }

    [Fact]
    public void Neutral_StaysNeutral_RegardlessOfPosition()
    {
        var layout = InsightsChartPalette.Layout(
            new[] { S("Comic"), S("Unknown"), S("Manga") }, ChartCategoryKind.Positional);

        Assert.Equal(InsightsChartPalette.NeutralBase, layout[1].Base);
        Assert.NotEqual(InsightsChartPalette.NeutralBase, layout[0].Base);
        Assert.NotEqual(InsightsChartPalette.NeutralBase, layout[2].Base);
    }

    [Theory]
    [InlineData("Everyone", "Success")]
    [InlineData("PG", "Success")]
    [InlineData("Teen", "ChartBlue")]
    [InlineData("Everyone 10+", "ChartBlue")]
    [InlineData("Teen Plus", "ChartBlue")]
    [InlineData("Mature 17+", "Badge")]
    [InlineData("M", "Badge")]
    [InlineData("Adults Only 18+", "Danger")]
    public void ContentRating_RampsFromAllAgesToAdultsOnly(string label, string expectedBase)
        => Assert.Equal(expectedBase, InsightsChartPalette.Semantic(ChartCategoryKind.ContentRating, label));

    [Fact]
    public void UnrecognisedContentRating_FallsBackToAnUnusedPositionalColour()
    {
        var layout = InsightsChartPalette.Layout(
            new[] { S("Mature 17+"), S("Weird Regional Rating") }, ChartCategoryKind.ContentRating);

        Assert.Equal("Badge", layout[0].Base);
        Assert.NotEqual("Badge", layout[1].Base);
        Assert.False(layout[1].IsNeutral);
    }

    [Fact]
    public void PositionalSlices_NeverShareAColour_WithinAFullPalette()
    {
        var layout = InsightsChartPalette.Layout(
            Enumerable.Range(1, 6).Select(i => S($"Cat{i}", 10 - i)), ChartCategoryKind.Positional);

        Assert.Equal(6, layout.Select(s => s.Base).Distinct().Count());
    }

    [Fact]
    public void MoreThanMaxSlices_FoldTheSmallestIntoANeutralOther()
    {
        var slices = Enumerable.Range(1, 9).Select(i => S($"Cat{i}", i * 10)).ToList();

        var layout = InsightsChartPalette.Layout(slices, ChartCategoryKind.Positional);

        Assert.Equal(6, layout.Count);
        var other = layout.Last();
        Assert.Equal("Other", other.Label);
        Assert.True(other.IsNeutral);
        Assert.Equal(10 + 20 + 30 + 40, other.Count); // Cat1..Cat4 folded; Cat5..Cat9 kept
        Assert.Equal(slices.Sum(s => s.Count), layout.Sum(s => s.Count));
    }

    [Fact]
    public void ZeroCountSlices_AreDropped()
    {
        var layout = InsightsChartPalette.Layout(new[] { S("Comic", 5), S("Manga", 0) }, ChartCategoryKind.Positional);
        Assert.Equal("Comic", layout.Single().Label);
    }

    [Fact]
    public void BrushAndColorKeys_FollowTheTokenNaming()
    {
        var slice = new PaletteSlice("Completed", 3, "Success", false);
        Assert.Equal("PbSuccessBrush", slice.BrushKey);
        Assert.Equal("PbSuccessColor", slice.ColorKey);
    }

    [Fact]
    public void RampColor_RunsFromTheDangerEndToTheSuccessEnd()
    {
        var first = InsightsChartPalette.RampColor(0, 5);
        var last = InsightsChartPalette.RampColor(4, 5);
        var middle = InsightsChartPalette.RampColor(2, 5);

        Assert.True(first.R > first.G, "the low end should be reddish");
        Assert.True(last.G > last.R, "the high end should be greenish");
        Assert.NotEqual(first, middle);
        Assert.NotEqual(middle, last);
    }
}
