using Avalonia;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Pure tap-zone resolution (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md section 4): Mihon's layouts plus Paperbunkr's Default.</summary>
public class TapZoneResolverTests
{
    private static readonly Size Canvas = new(900, 900);

    // Centres of the 3x3 grid cells.
    private const double L = 150, M = 450, R = 750;
    private const double T = 150, C = 450, B = 750;

    private static TapAction Touch(TapZoneLayout layout, double x, double y, TapZoneInvert invert = TapZoneInvert.None, bool rtl = false, bool continuous = false) =>
        TapZoneResolver.Resolve(new Point(x, y), Canvas, layout, invert, TapInput.Touch, true, rtl, false, continuous);

    private static TapAction Mouse(TapZoneLayout layout, double x, double y, bool zonesForMouse = true, bool continuous = false) =>
        TapZoneResolver.Resolve(new Point(x, y), Canvas, layout, TapZoneInvert.None, TapInput.Mouse, zonesForMouse, false, false, continuous);

    // --- Default = today's behaviour ---

    [Theory]
    [InlineData(100, TapAction.Left)]
    [InlineData(450, TapAction.Menu)]
    [InlineData(800, TapAction.Right)]
    public void Default_Touch_IsThreeColumnsWithMenuInTheMiddle(double x, TapAction expected) =>
        Assert.Equal(expected, Touch(TapZoneLayout.Default, x, 450));

    [Theory]
    [InlineData(100, TapAction.Left)]
    [InlineData(449, TapAction.Left)]
    [InlineData(450, TapAction.Right)]
    [InlineData(800, TapAction.Right)]
    public void Default_Mouse_IsTwoHalves(double x, TapAction expected) =>
        Assert.Equal(expected, Mouse(TapZoneLayout.Default, x, 450));

    [Theory]
    [InlineData(100, TapAction.Left)]
    [InlineData(450, TapAction.Menu)]
    [InlineData(800, TapAction.Right)]
    public void Default_Touch_VerticalPaged_SplitsOnY(double y, TapAction expected) =>
        Assert.Equal(expected, TapZoneResolver.Resolve(new Point(450, y), Canvas, TapZoneLayout.Default, TapZoneInvert.None, TapInput.Touch, true, false, true, false));

    [Fact]
    public void Default_InContinuousMode_IsNoZones()
    {
        Assert.Equal(TapAction.None, Touch(TapZoneLayout.Default, L, C, continuous: true));
        Assert.Equal(TapAction.None, Mouse(TapZoneLayout.Default, R, C, continuous: true));
    }

    [Fact]
    public void Disabled_IsNoZonesForEveryInput()
    {
        Assert.Equal(TapAction.None, Touch(TapZoneLayout.Disabled, L, C));
        Assert.Equal(TapAction.None, Mouse(TapZoneLayout.Disabled, R, C));
    }

    // --- Named layouts, every grid cell (rows top, centre, bottom; columns left, middle, right) ---

    [Theory]
    [InlineData(L, T, TapAction.Previous)] [InlineData(M, T, TapAction.Previous)] [InlineData(R, T, TapAction.Previous)]
    [InlineData(L, C, TapAction.Previous)] [InlineData(M, C, TapAction.Menu)] [InlineData(R, C, TapAction.Next)]
    [InlineData(L, B, TapAction.Next)] [InlineData(M, B, TapAction.Next)] [InlineData(R, B, TapAction.Next)]
    public void LShaped_EveryCell(double x, double y, TapAction expected) => Assert.Equal(expected, Touch(TapZoneLayout.LShaped, x, y));

    [Theory]
    [InlineData(L, T, TapAction.Menu)] [InlineData(M, T, TapAction.Menu)] [InlineData(R, T, TapAction.Menu)]
    [InlineData(L, C, TapAction.Previous)] [InlineData(M, C, TapAction.Next)] [InlineData(R, C, TapAction.Next)]
    [InlineData(L, B, TapAction.Previous)] [InlineData(M, B, TapAction.Next)] [InlineData(R, B, TapAction.Next)]
    public void Kindlish_EveryCell(double x, double y, TapAction expected) => Assert.Equal(expected, Touch(TapZoneLayout.Kindlish, x, y));

    [Theory]
    [InlineData(L, T, TapAction.Next)] [InlineData(M, T, TapAction.Menu)] [InlineData(R, T, TapAction.Next)]
    [InlineData(L, C, TapAction.Next)] [InlineData(M, C, TapAction.Menu)] [InlineData(R, C, TapAction.Next)]
    [InlineData(L, B, TapAction.Next)] [InlineData(M, B, TapAction.Previous)] [InlineData(R, B, TapAction.Next)]
    public void Edge_EveryCell(double x, double y, TapAction expected) => Assert.Equal(expected, Touch(TapZoneLayout.Edge, x, y));

    [Theory]
    [InlineData(L, T, TapAction.Left)] [InlineData(M, T, TapAction.Menu)] [InlineData(R, T, TapAction.Right)]
    [InlineData(L, C, TapAction.Left)] [InlineData(M, C, TapAction.Menu)] [InlineData(R, C, TapAction.Right)]
    [InlineData(L, B, TapAction.Left)] [InlineData(M, B, TapAction.Menu)] [InlineData(R, B, TapAction.Right)]
    public void RightAndLeft_EveryCell(double x, double y, TapAction expected) => Assert.Equal(expected, Touch(TapZoneLayout.RightAndLeft, x, y));

    // --- Uncovered area: menu for touch, nothing for the mouse ---

    [Fact]
    public void UncoveredArea_IsMenuForTouch_AndNothingForMouse()
    {
        Assert.Equal(TapAction.Menu, Touch(TapZoneLayout.LShaped, M, C));
        Assert.Equal(TapAction.None, Mouse(TapZoneLayout.LShaped, M, C));
    }

    [Fact]
    public void NamedLayout_AppliesToMouseToo()
    {
        Assert.Equal(TapAction.Next, Mouse(TapZoneLayout.Kindlish, R, C));
        Assert.Equal(TapAction.Previous, Mouse(TapZoneLayout.Kindlish, L, C));
    }

    [Fact]
    public void MouseOptOut_KeepsTheTwoHalves_InPagedMode_AndNothingInContinuous()
    {
        Assert.Equal(TapAction.Left, Mouse(TapZoneLayout.Kindlish, 100, 450, zonesForMouse: false));
        Assert.Equal(TapAction.Right, Mouse(TapZoneLayout.Kindlish, 800, 450, zonesForMouse: false));
        Assert.Equal(TapAction.None, Mouse(TapZoneLayout.Kindlish, 800, 450, zonesForMouse: false, continuous: true));
    }

    [Fact]
    public void MouseOptOut_DoesNotAffectTouch() =>
        Assert.Equal(TapAction.Next, TapZoneResolver.Resolve(new Point(R, C), Canvas, TapZoneLayout.Kindlish, TapZoneInvert.None, TapInput.Touch, false, false, false, false));

    // --- Right-to-left mirroring ---

    [Fact]
    public void RightToLeft_MirrorsReadingOrderRegions()
    {
        Assert.Equal(TapAction.Previous, Touch(TapZoneLayout.LShaped, L, C));
        Assert.Equal(TapAction.Next, Touch(TapZoneLayout.LShaped, L, C, rtl: true));
        Assert.Equal(TapAction.Previous, Touch(TapZoneLayout.LShaped, R, C, rtl: true));
    }

    [Fact]
    public void RightToLeft_DoesNotMirrorTheSpatialLayout()
    {
        Assert.Equal(TapAction.Left, Touch(TapZoneLayout.RightAndLeft, L, C, rtl: true));
        Assert.Equal(TapAction.Right, Touch(TapZoneLayout.RightAndLeft, R, C, rtl: true));
    }

    // --- Manual invert ---

    [Fact]
    public void InvertHorizontal_MirrorsLeftAndRight()
    {
        Assert.Equal(TapAction.Next, Touch(TapZoneLayout.Kindlish, L, C, TapZoneInvert.Horizontal));
        Assert.Equal(TapAction.Right, Touch(TapZoneLayout.RightAndLeft, L, C, TapZoneInvert.Horizontal));
    }

    [Fact]
    public void InvertVertical_MirrorsTopAndBottom()
    {
        Assert.Equal(TapAction.Next, Touch(TapZoneLayout.LShaped, M, T, TapZoneInvert.Vertical));
        Assert.Equal(TapAction.Previous, Touch(TapZoneLayout.LShaped, M, B, TapZoneInvert.Vertical));
    }

    [Fact]
    public void InvertBoth_MirrorsEverything() =>
        Assert.Equal(TapAction.Next, Touch(TapZoneLayout.LShaped, L, T, TapZoneInvert.Both));

    [Fact]
    public void RightToLeftPlusInvertHorizontal_CancelOut() =>
        Assert.Equal(TapAction.Previous, Touch(TapZoneLayout.LShaped, L, C, TapZoneInvert.Horizontal, rtl: true));

    [Fact]
    public void ZeroSizedCanvas_IsNoZones() =>
        Assert.Equal(TapAction.None, TapZoneResolver.Resolve(new Point(1, 1), new Size(0, 0), TapZoneLayout.LShaped, TapZoneInvert.None, TapInput.Touch, true, false, false, false));

    [Fact]
    public void EdgesOfTheCanvas_StayInsideARegion()
    {
        Assert.Equal(TapAction.Previous, Touch(TapZoneLayout.Kindlish, 0, 450));
        Assert.Equal(TapAction.Next, Touch(TapZoneLayout.Kindlish, 900, 900));
    }
}
