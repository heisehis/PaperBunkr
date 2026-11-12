using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>The Corners curve, text scale and focus stroke (docs/superpowers/specs/2026-11-06-personalization-design.md).</summary>
public class PersonalizationMathTests
{
    [Fact]
    public void Corners_EndsAndMiddle()
    {
        Assert.Equal((0.0, 0.0, 0.0), PersonalizationMath.CornersToRadii(0));
        Assert.Equal((7.0, 5.0, 14.0), PersonalizationMath.CornersToRadii(50));
        Assert.Equal((14.0, 10.0, 28.0), PersonalizationMath.CornersToRadii(100));
    }

    [Fact]
    public void Corners_AreMonotonic_AndClamped()
    {
        double previous = -1;
        for (int s = 0; s <= 100; s++)
        {
            double radius = PersonalizationMath.CornersToRadii(s).Radius;
            Assert.True(radius >= previous);
            previous = radius;
        }

        Assert.Equal(PersonalizationMath.CornersToRadii(0), PersonalizationMath.CornersToRadii(-20));
        Assert.Equal(PersonalizationMath.CornersToRadii(100), PersonalizationMath.CornersToRadii(400));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(80)]
    [InlineData(100)]
    public void Corners_RoundTrip(int slider)
    {
        var (r, sm, lg) = PersonalizationMath.CornersToRadii(slider);
        int? back = PersonalizationMath.RadiiToCorners(r, sm, lg);
        Assert.NotNull(back);
        Assert.Equal(slider, back);
    }

    [Fact]
    public void RadiiOffTheCurve_AreCustom()
    {
        Assert.Null(PersonalizationMath.RadiiToCorners(3, 12, 9));
        Assert.Null(PersonalizationMath.RadiiToCorners(7, 5, 30));
    }

    [Theory]
    [InlineData(13, 100, 13)]
    [InlineData(13, 110, 14.5)]
    [InlineData(13, 90, 11.5)]
    [InlineData(10, 90, 9)]
    [InlineData(8, 90, 8)]
    [InlineData(13, 125, 13)] // an unsupported scale falls back to 100
    public void ScaleFontSize_RoundsToHalfPoints_AndNeverGoesTooSmall(double size, int percent, double expected)
    {
        Assert.Equal(expected, PersonalizationMath.ScaleFontSize(size, percent));
    }

    [Theory]
    [InlineData(90, 90)]
    [InlineData(100, 100)]
    [InlineData(110, 110)]
    [InlineData(125, 100)]
    [InlineData(0, 100)]
    public void FontScale_Normalizes(int input, int expected)
    {
        Assert.Equal(expected, PersonalizationMath.NormalizeFontScale(input));
    }

    [Theory]
    [InlineData(0, 1.5)]
    [InlineData(1, 2.0)]
    [InlineData(2, 3.0)]
    [InlineData(9, 3.0)]
    [InlineData(-4, 1.5)]
    public void FocusStroke_ByThickness(int thickness, double expected)
    {
        Assert.Equal(expected, PersonalizationMath.FocusRingStroke(thickness));
    }
}
