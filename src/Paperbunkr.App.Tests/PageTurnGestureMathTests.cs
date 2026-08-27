using Avalonia;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="PageTurnGestureMath"/> - the pure zone/flick intent resolution behind
/// <see cref="PageCanvas"/>'s click-and-tap page-turn zones and touch flicks, for both the
/// horizontal paged modes and vertical (`TopToBottom`) mode
/// (docs/superpowers/specs/2026-08-27-vertical-paged-reading-mode-design.md §3). Plain geometry -
/// no Avalonia input plumbing.
/// </summary>
public class PageTurnGestureMathTests
{
    private static readonly Size Canvas = new(1000, 800);

    // --- ResolveZone, 2-way (mouse) ---

    [Theory]
    [InlineData(200, false)] // left half -> back
    [InlineData(800, true)]  // right half -> forward
    [InlineData(500, true)]  // exact midpoint -> forward (>=)
    public void ResolveZone_TwoWay_Horizontal(double x, bool expectedForward)
    {
        Assert.Equal(expectedForward, PageTurnGestureMath.ResolveZone(new Point(x, 400), Canvas, vertical: false, divisions: 2));
    }

    [Theory]
    [InlineData(150, false)] // top half -> back
    [InlineData(650, true)]  // bottom half -> forward
    public void ResolveZone_TwoWay_Vertical(double y, bool expectedForward)
    {
        Assert.Equal(expectedForward, PageTurnGestureMath.ResolveZone(new Point(500, y), Canvas, vertical: true, divisions: 2));
    }

    // --- ResolveZone, 3-way (touch) ---

    [Theory]
    [InlineData(100, false)]  // left third -> back
    [InlineData(900, true)]   // right third -> forward
    [InlineData(500, null)]   // middle third -> no-op
    public void ResolveZone_ThreeWay_Horizontal(double x, bool? expected)
    {
        Assert.Equal(expected, PageTurnGestureMath.ResolveZone(new Point(x, 400), Canvas, vertical: false, divisions: 3));
    }

    [Theory]
    [InlineData(100, false)]  // top third -> back
    [InlineData(700, true)]   // bottom third -> forward
    [InlineData(400, null)]   // middle third -> no-op
    public void ResolveZone_ThreeWay_Vertical(double y, bool? expected)
    {
        Assert.Equal(expected, PageTurnGestureMath.ResolveZone(new Point(500, y), Canvas, vertical: true, divisions: 3));
    }

    [Fact]
    public void ResolveZone_ZeroExtent_ReturnsNull()
    {
        Assert.Null(PageTurnGestureMath.ResolveZone(new Point(0, 0), new Size(0, 0), vertical: false, divisions: 2));
    }

    // --- ResolveFlick ---

    [Fact]
    public void ResolveFlick_Horizontal_FlickLeft_IsForward()
    {
        Assert.True(PageTurnGestureMath.ResolveFlick(new Vector(-120, 5), vertical: false, minDistance: 60));
    }

    [Fact]
    public void ResolveFlick_Horizontal_FlickRight_IsBackward()
    {
        Assert.False(PageTurnGestureMath.ResolveFlick(new Vector(120, 5), vertical: false, minDistance: 60));
    }

    [Fact]
    public void ResolveFlick_Vertical_FlickUp_IsForward()
    {
        // Flick content up (delta.Y negative) to advance - scroll convention.
        Assert.True(PageTurnGestureMath.ResolveFlick(new Vector(5, -120), vertical: true, minDistance: 60));
    }

    [Fact]
    public void ResolveFlick_Vertical_FlickDown_IsBackward()
    {
        Assert.False(PageTurnGestureMath.ResolveFlick(new Vector(5, 120), vertical: true, minDistance: 60));
    }

    [Fact]
    public void ResolveFlick_BelowMinDistance_ReturnsNull()
    {
        Assert.Null(PageTurnGestureMath.ResolveFlick(new Vector(-40, 2), vertical: false, minDistance: 60));
    }

    [Fact]
    public void ResolveFlick_TooDiagonal_ReturnsNull()
    {
        // Dominant-axis travel must strictly exceed the cross axis.
        Assert.Null(PageTurnGestureMath.ResolveFlick(new Vector(-100, -100), vertical: false, minDistance: 60));
        Assert.Null(PageTurnGestureMath.ResolveFlick(new Vector(-100, -120), vertical: false, minDistance: 60));
    }

    [Fact]
    public void ResolveFlick_VerticalGestureIgnoresHorizontalFlick()
    {
        // A big horizontal swipe with little vertical travel is not a vertical page turn.
        Assert.Null(PageTurnGestureMath.ResolveFlick(new Vector(-200, 10), vertical: true, minDistance: 60));
    }
}
