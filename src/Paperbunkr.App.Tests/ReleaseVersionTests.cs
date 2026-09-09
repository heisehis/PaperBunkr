using System;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="ReleaseVersion"/> - changelog-heading parsing and the three-component version
/// comparison that drives the "What's New" startup check (docs/superpowers/specs/2026-09-09-
/// startup-onboarding-whats-new-design.md).
/// </summary>
public class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.3.0-beta", 0, 3, 0)]
    [InlineData("0.1.1-alpha", 0, 1, 1)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("2.0", 2, 0, 0)]
    [InlineData("0.3.0.0+abcdef", 0, 3, 0)]
    [InlineData(" 0.3.0-beta ", 0, 3, 0)]
    public void TryParseHeading_ParsesCoreVersion_DroppingSuffix(string input, int maj, int min, int build)
    {
        Assert.True(ReleaseVersion.TryParseHeading(input, out var v));
        Assert.Equal(new Version(maj, min, build), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("-beta")]
    [InlineData("v1")]
    public void TryParseHeading_ReturnsFalse_ForUnparseable(string? input)
    {
        Assert.False(ReleaseVersion.TryParseHeading(input, out _));
    }

    [Theory]
    [InlineData("0.3.0.0", "0.2.0.0", true)]   // minor bump
    [InlineData("0.3.1.0", "0.3.0.0", true)]   // patch bump
    [InlineData("1.0.0.0", "0.9.9.0", true)]   // major bump
    [InlineData("0.3.0.0", "0.3.0.0", false)]  // equal
    [InlineData("0.2.0.0", "0.3.0.0", false)]  // downgrade
    [InlineData("0.3.0.5", "0.3.0.0", false)]  // revision-only difference is not "newer"
    public void IsNewerThan_ComparesFirstThreeComponentsOnly(string candidate, string baseline, bool expected)
    {
        Assert.Equal(expected, ReleaseVersion.IsNewerThan(Version.Parse(candidate), Version.Parse(baseline)));
    }

    [Fact]
    public void DisplayString_IsThreePartPlusBetaSuffix()
    {
        // Whatever the built assembly version is, DisplayString drops the revision and appends -beta.
        var v = ReleaseVersion.Current;
        Assert.Equal($"{v.Major}.{v.Minor}.{v.Build}-beta", ReleaseVersion.DisplayString);
        Assert.DoesNotContain("..", ReleaseVersion.DisplayString);
    }
}
