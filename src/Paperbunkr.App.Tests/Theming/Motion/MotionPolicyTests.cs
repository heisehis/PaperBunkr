using Paperbunkr.App.Theming;
using Paperbunkr.App.Theming.Motion;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests.Theming.Motion;

/// <summary>Motion v2 policy (docs/superpowers/specs/2026-10-20-motion-system-v2-foundation-overlays-design.md §3.4, §4).</summary>
public sealed class MotionPolicyTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Theory]
    [InlineData(MotionPreferenceMode.Full, false, EffectiveMotion.Full)]
    [InlineData(MotionPreferenceMode.Reduced, true, EffectiveMotion.Reduced)]
    [InlineData(MotionPreferenceMode.Off, true, EffectiveMotion.Off)]
    [InlineData(MotionPreferenceMode.System, true, EffectiveMotion.Full)]
    [InlineData(MotionPreferenceMode.System, false, EffectiveMotion.Reduced)]
    public void Effective_ResolvesSystemAgainstThePlatform(MotionPreferenceMode preference, bool systemAnimationsEnabled, EffectiveMotion expected) =>
        Assert.Equal(expected, MotionPolicy.Effective(preference, systemAnimationsEnabled));

    [Fact]
    public void Effective_SystemWithoutAPlatformReading_IsFull() =>
        Assert.Equal(EffectiveMotion.Full, MotionPolicy.Effective(MotionPreferenceMode.System, null));

    [Theory]
    [InlineData(MotionRole.Acknowledge, 90, 90, 90)]
    [InlineData(MotionRole.Hover, 120, 160, 120)]
    [InlineData(MotionRole.Reveal, 180, 180, 120)]
    [InlineData(MotionRole.Replace, 160, 0, 160)]
    [InlineData(MotionRole.Travel, 220, 220, 140)]
    [InlineData(MotionRole.Ambient, 360, 360, 360)]
    public void Full_MatchesTheRoleTable(MotionRole role, double fade, double move, double exit)
    {
        var spec = MotionPolicy.Resolve(role, EffectiveMotion.Full, ThemeEffectQuality.High);
        Assert.Equal((Ms(fade), Ms(move), Ms(exit), true), (spec.Fade, spec.Move, spec.Exit, spec.AllowsTransform));
    }

    [Theory]
    [InlineData(MotionRole.Acknowledge, 90, 90)]
    [InlineData(MotionRole.Hover, 100, 100)]
    [InlineData(MotionRole.Reveal, 100, 80)]
    [InlineData(MotionRole.Replace, 100, 100)]
    [InlineData(MotionRole.Travel, 120, 120)]
    [InlineData(MotionRole.Ambient, 0, 0)]
    public void Reduced_KeepsShortFadesAndNeverMoves(MotionRole role, double fade, double exit)
    {
        var spec = MotionPolicy.Resolve(role, EffectiveMotion.Reduced, ThemeEffectQuality.High);
        Assert.Equal((Ms(fade), TimeSpan.Zero, Ms(exit), false), (spec.Fade, spec.Move, spec.Exit, spec.AllowsTransform));
    }

    [Theory]
    [InlineData(MotionRole.Acknowledge)]
    [InlineData(MotionRole.Reveal)]
    [InlineData(MotionRole.Travel)]
    [InlineData(MotionRole.Ambient)]
    public void Off_IsInstantEverywhere(MotionRole role) =>
        Assert.Equal(MotionSpec.None, MotionPolicy.Resolve(role, EffectiveMotion.Off, ThemeEffectQuality.High));

    [Fact]
    public void Performance_KeepsCheapRoles_DegradesTravel_DropsAmbient()
    {
        foreach (var role in new[] { MotionRole.Acknowledge, MotionRole.Hover, MotionRole.Reveal, MotionRole.Replace })
        {
            Assert.Equal(
                MotionPolicy.Resolve(role, EffectiveMotion.Full, ThemeEffectQuality.High),
                MotionPolicy.Resolve(role, EffectiveMotion.Full, ThemeEffectQuality.Performance));
        }

        Assert.Equal(
            MotionPolicy.Resolve(MotionRole.Travel, EffectiveMotion.Reduced, ThemeEffectQuality.High),
            MotionPolicy.Resolve(MotionRole.Travel, EffectiveMotion.Full, ThemeEffectQuality.Performance));
        Assert.Equal(MotionSpec.None, MotionPolicy.Resolve(MotionRole.Ambient, EffectiveMotion.Full, ThemeEffectQuality.Performance));
        Assert.Equal(TimeSpan.Zero, MotionPolicy.FlightDuration(EffectiveMotion.Full, ThemeEffectQuality.Performance));
    }

    [Fact]
    public void Balanced_AllowsEveryFiniteRole() =>
        Assert.True(MotionPolicy.Resolve(MotionRole.Ambient, EffectiveMotion.Full, ThemeEffectQuality.Balanced).AllowsTransform);

    [Theory]
    [InlineData(EffectiveMotion.Full, ThemeEffectQuality.High, false, true)]
    [InlineData(EffectiveMotion.Full, ThemeEffectQuality.High, true, false)]
    [InlineData(EffectiveMotion.Full, ThemeEffectQuality.Balanced, false, false)]
    [InlineData(EffectiveMotion.Full, ThemeEffectQuality.Performance, false, false)]
    [InlineData(EffectiveMotion.Reduced, ThemeEffectQuality.High, false, false)]
    public void Continuous_NeedsFullHighAndBattery(EffectiveMotion mode, ThemeEffectQuality quality, bool batteryLow, bool expected) =>
        Assert.Equal(expected, MotionPolicy.AllowsContinuous(mode, quality, batteryLow));

    [Fact]
    public void Flight_Is280InFull_AndNoneWhenReduced()
    {
        Assert.Equal(Ms(280), MotionPolicy.FlightDuration(EffectiveMotion.Full, ThemeEffectQuality.High));
        Assert.Equal(TimeSpan.Zero, MotionPolicy.FlightDuration(EffectiveMotion.Reduced, ThemeEffectQuality.High));
    }

    [Fact]
    public void TimeScale_MultipliesEveryDuration()
    {
        var spec = MotionPolicy.Resolve(MotionRole.Reveal, EffectiveMotion.Full, ThemeEffectQuality.High, scale: 5);
        Assert.Equal((Ms(900), Ms(900), Ms(600)), (spec.Fade, spec.Move, spec.Exit));
        Assert.Equal(Ms(1400), MotionPolicy.FlightDuration(EffectiveMotion.Full, ThemeEffectQuality.High, scale: 5));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("abc", 1)]
    [InlineData("-2", 1)]
    [InlineData("5", 5)]
    [InlineData("0.5", 0.5)]
    [InlineData("0.01", 0.1)]
    [InlineData("40", 10)]
    public void ReadScale_ParsesAndClamps(string? raw, double expected) => Assert.Equal(expected, MotionPolicy.ReadScale(raw));
}
