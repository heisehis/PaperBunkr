using Avalonia;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="SharedElementFlightMath"/>'s pure translate/scale computation, same
/// "plain values, no app context needed" shape as <see cref="ImageAdjustmentMathTests"/> -
/// <see cref="Rect"/>/<see cref="CornerRadius"/> are plain structs, no Avalonia Application needed
/// to construct or compare them.
/// </summary>
public class SharedElementFlightMathTests
{
    [Fact]
    public void ComputeFlight_IdenticalRects_ReturnsIdentityTransform()
    {
        var rect = new Rect(10, 10, 100, 100);

        var flight = SharedElementFlightMath.ComputeFlight(rect, rect, new CornerRadius(6), new CornerRadius(6));

        Assert.False(flight.IsNoOp);
        Assert.Equal(0, flight.TranslateX);
        Assert.Equal(0, flight.TranslateY);
        Assert.Equal(1, flight.ScaleX);
        Assert.Equal(1, flight.ScaleY);
    }

    [Fact]
    public void ComputeFlight_SquareTileToWideHero_ScalesNonUniformly()
    {
        // A ~110px square Library tile flying to a wide, taller hero band - the exact
        // tile→hero shape from the design doc's Library↔Detail cover flight.
        var tile = new Rect(40, 200, 110, 160);
        var hero = new Rect(0, 0, 400, 220);

        var flight = SharedElementFlightMath.ComputeFlight(tile, hero, new CornerRadius(6), new CornerRadius(0));

        Assert.False(flight.IsNoOp);
        Assert.Equal(-40, flight.TranslateX);
        Assert.Equal(-200, flight.TranslateY);
        Assert.Equal(400.0 / 110, flight.ScaleX, precision: 6);
        Assert.Equal(220.0 / 160, flight.ScaleY, precision: 6);
        Assert.Equal(new CornerRadius(6), flight.StartRadius);
        Assert.Equal(new CornerRadius(0), flight.EndRadius);
    }

    [Fact]
    public void ComputeFlight_ZeroSizeSource_ReturnsNoOp()
    {
        var flight = SharedElementFlightMath.ComputeFlight(
            new Rect(0, 0, 0, 0), new Rect(0, 0, 200, 200), default, default);

        Assert.True(flight.IsNoOp);
    }

    [Fact]
    public void ComputeFlight_ZeroSizeDestination_ReturnsNoOp()
    {
        // The realistic trigger: the destination element (e.g. a not-yet-realized grid tile on the
        // back trip) never laid out, so its Bounds is still Rect.Empty (0,0,0,0) when captured.
        var flight = SharedElementFlightMath.ComputeFlight(
            new Rect(0, 0, 100, 100), default, default, default);

        Assert.True(flight.IsNoOp);
    }

    [Fact]
    public void ComputeFlight_NegativeCoordinates_StillComputesTranslate()
    {
        // A source that's scrolled partially above the viewport (negative Y) - still a valid flight,
        // just translating from off-screen.
        var source = new Rect(-30, -50, 80, 80);
        var destination = new Rect(0, 0, 400, 220);

        var flight = SharedElementFlightMath.ComputeFlight(source, destination, default, default);

        Assert.False(flight.IsNoOp);
        Assert.Equal(30, flight.TranslateX);
        Assert.Equal(50, flight.TranslateY);
    }
}
