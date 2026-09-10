using System;
using System.Globalization;
using Paperbunkr.App.Services;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="VersionEqualsCurrentConverter"/> backs the About section's changelog accordion -
/// the initial-expand and the "Current" badge. It's an exact string compare, so the two inputs
/// (a <c>CHANGELOG.md</c> heading version and <c>PreferencesScreenViewModel.CurrentVersion</c>)
/// have to be the same shape. They weren't while <c>CurrentVersion</c> returned the four-part
/// <c>x.y.z.w</c> assembly version; it now returns <see cref="ReleaseVersion.DisplayString"/>
/// (docs/superpowers/specs/2026-09-10-versioning-convention-design.md Q5).
/// </summary>
public class AboutSectionConvertersTests
{
    private static bool Match(string? a, string? b) =>
        (bool)VersionEqualsCurrentConverter.Instance.Convert(
            new object?[] { a, b }, typeof(bool), null, CultureInfo.InvariantCulture)!;

    [Fact]
    public void CurrentVersionShape_MatchesAChangelogHeadingOfTheSameVersion()
    {
        // ReleaseVersion.DisplayString is "x.y.z-beta" - the exact text a "## [x.y.z-beta]" heading
        // parses to as ChangelogEntry.Version.
        string current = ReleaseVersion.DisplayString;
        Assert.Matches(@"^\d+\.\d+\.\d+-beta$", current);
        Assert.True(Match(current, current));
    }

    [Fact]
    public void DifferentVersions_DoNotMatch()
    {
        Assert.False(Match("9.9.9-beta", ReleaseVersion.DisplayString));
        Assert.False(Match("0.3.0.0", "0.3.0-beta")); // the old shape mismatch that kept the badge dark
    }
}
