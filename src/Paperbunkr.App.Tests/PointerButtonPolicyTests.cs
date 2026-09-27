using Avalonia;
using Avalonia.Input;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>Pure press classification and tap detection behind <see cref="PageCanvas"/> (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md §1).</summary>
public class PointerButtonPolicyTests
{
    [Theory]
    [InlineData(PointerUpdateKind.LeftButtonPressed, PointerRole.Primary)]
    [InlineData(PointerUpdateKind.LeftButtonReleased, PointerRole.Primary)]
    [InlineData(PointerUpdateKind.RightButtonPressed, PointerRole.Secondary)]
    [InlineData(PointerUpdateKind.RightButtonReleased, PointerRole.Secondary)]
    [InlineData(PointerUpdateKind.MiddleButtonPressed, PointerRole.Middle)]
    [InlineData(PointerUpdateKind.XButton1Pressed, PointerRole.Back)]
    [InlineData(PointerUpdateKind.XButton2Pressed, PointerRole.Forward)]
    [InlineData(PointerUpdateKind.XButton2Released, PointerRole.Forward)]
    [InlineData(PointerUpdateKind.Other, PointerRole.Other)]
    public void Classify_MapsEveryButton(PointerUpdateKind kind, PointerRole expected) =>
        Assert.Equal(expected, PointerButtonPolicy.Classify(kind));

    [Theory]
    [InlineData(PointerRole.Primary, true)]
    [InlineData(PointerRole.Secondary, false)]
    [InlineData(PointerRole.Middle, false)]
    [InlineData(PointerRole.Back, false)]
    [InlineData(PointerRole.Forward, false)]
    [InlineData(PointerRole.Other, false)]
    public void MayAct_OnlyForPrimary(PointerRole role, bool expected) =>
        Assert.Equal(expected, PointerButtonPolicy.MayAct(role));

    [Theory]
    [InlineData(0, 0, 100, true)]
    [InlineData(3, 4, 399, true)]       // exactly 5 px, well inside the limits
    [InlineData(6, 0, 400, true)]       // both limits are inclusive
    [InlineData(6.1, 0, 100, false)]    // moved too far
    [InlineData(0, 0, 401, false)]      // held too long
    [InlineData(-5, -5, 100, false)]    // ~7.07 px diagonal
    public void IsTap_UsesDistanceAndDuration(double dx, double dy, double elapsedMs, bool expected) =>
        Assert.Equal(expected, PointerButtonPolicy.IsTap(new Vector(dx, dy), elapsedMs));
}
