using Avalonia.Media;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

public class SpineMetricsTests
{
    [Theory]
    [InlineData(null, 24, 54)]
    [InlineData(0, 24, 54)]
    [InlineData(20, 24, 54)]
    [InlineData(5000, 54, 54)]
    public void Issue_width_stays_inside_the_clamp(int? pages, double min, double max)
    {
        double w = SpineMetrics.WidthForPages(pages);
        Assert.InRange(w, min, max);
    }

    [Fact]
    public void Longer_books_get_thicker_spines()
    {
        Assert.True(SpineMetrics.WidthForPages(300) > SpineMetrics.WidthForPages(30));
    }

    [Fact]
    public void Series_width_grows_with_issue_count_and_is_clamped()
    {
        Assert.True(SpineMetrics.WidthForSeries(40) > SpineMetrics.WidthForSeries(2));
        Assert.Equal(66, SpineMetrics.WidthForSeries(10_000));
        Assert.Equal(28, SpineMetrics.WidthForSeries(-5));
    }

    [Fact]
    public void Height_is_deterministic_and_within_the_jitter_band()
    {
        for (int seed = 0; seed < 2000; seed++)
        {
            double h = SpineMetrics.HeightFor(seed);
            Assert.Equal(h, SpineMetrics.HeightFor(seed));
            Assert.InRange(h, 182, 210);
            Assert.True(h + SpineMetrics.BoardHeight <= SpineMetrics.CellHeight);
        }
    }

    [Fact]
    public void Heights_actually_vary()
    {
        var distinct = Enumerable.Range(0, 200).Select(SpineMetrics.HeightFor).Distinct().Count();
        Assert.True(distinct > 10);
    }

    [Fact]
    public void Body_colour_is_darker_than_the_cover_colour()
    {
        var body = SpineMetrics.BodyColor(Color.FromRgb(240, 200, 40));
        Assert.True(SpineMetrics.Luminance(body) < SpineMetrics.Luminance(Color.FromRgb(240, 200, 40)));
    }

    [Fact]
    public void Text_colour_follows_the_spine_luminance()
    {
        Assert.False(SpineMetrics.UseDarkText(Colors.Black));
        Assert.True(SpineMetrics.UseDarkText(Colors.White));
    }

    [Fact]
    public void New_enum_members_are_appended_so_stored_values_stay_valid()
    {
        Assert.Equal(0, (int)LibraryViewMode.PosterGrid);
        Assert.Equal(1, (int)LibraryViewMode.List);
        Assert.Equal(2, (int)LibraryViewMode.DetailsTable);
        Assert.Equal(3, (int)LibraryViewMode.Bookshelf);
        Assert.Equal(0, (int)ImageBackgroundMode.Auto);
        Assert.Equal(1, (int)ImageBackgroundMode.Color);
        Assert.Equal(2, (int)ImageBackgroundMode.Texture);
        Assert.Equal(3, (int)ImageBackgroundMode.Ambient);
    }
}
