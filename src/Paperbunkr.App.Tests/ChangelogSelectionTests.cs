using System;
using System.Linq;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>Exercises <see cref="ChangelogSelection"/> (docs/superpowers/specs/2026-09-26-about-polish-design.md §3, Q14).</summary>
public class ChangelogSelectionTests
{
    private static readonly ChangelogEntry[] Entries =
    [
        new("Unreleased", null, ""),
        new("0.7.0-beta", "2026-09-21", "### Added\n- Pull list."),
        new("0.6.4-beta", "2026-09-19", "### Fixed\n- Search lag."),
    ];

    [Fact]
    public void Visible_DropsEmptyEntries()
    {
        Assert.Equal(new[] { "0.7.0-beta", "0.6.4-beta" }, ChangelogSelection.Visible(Entries).Select(e => e.Version));
    }

    [Theory]
    [InlineData("0.7.3.0", "0.7.0-beta")]
    [InlineData("0.7.0.0", "0.7.0-beta")]
    [InlineData("0.6.9.0", "0.6.4-beta")]
    public void Current_IsNewestEntryNotNewerThanRunning(string running, string expected)
    {
        Assert.Equal(expected, ChangelogSelection.Current(Entries, Version.Parse(running))?.Version);
    }

    [Fact]
    public void Current_RunningOlderThanEveryEntry_IsNull()
    {
        Assert.Null(ChangelogSelection.Current(Entries, new Version(0, 1, 0, 0)));
    }

    [Fact]
    public void Current_NonEmptyUnreleased_IsNeverCurrent()
    {
        ChangelogEntry[] entries = [new("Unreleased", null, "### Added\n- Work in progress."), .. Entries[1..]];

        Assert.Equal("0.7.0-beta", ChangelogSelection.Current(entries, new Version(0, 7, 3, 0))?.Version);
    }

    [Fact]
    public void BuildRows_MarksAndExpandsOnlyTheCurrentEntry()
    {
        var rows = ChangelogSelection.BuildRows(Entries, new Version(0, 7, 3, 0));

        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].IsCurrent);
        Assert.True(rows[0].StartExpanded);
        Assert.False(rows[1].IsCurrent);
        Assert.False(rows[1].StartExpanded);
        Assert.Equal(ChangelogTagKind.Added, rows[0].Groups[0].Kind);
    }
}
