using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ChangelogBodyFormatter"/> (docs/superpowers/specs/2026-09-07-about-redesign-
/// design.md §Architecture 3) - pure string parsing over a <see cref="ChangelogEntry.Body"/> string,
/// no I/O. Deliberately doesn't touch <see cref="ChangelogParser"/> itself.
/// </summary>
public class ChangelogBodyFormatterTests
{
    [Fact]
    public void Format_TwoSubheadings_ReturnsTwoGroups()
    {
        string body = "### Added\n- Auto-update via NetSparkle.\n\n### Fixed\n- Cover thumbnails surviving a rescan.";

        var groups = ChangelogBodyFormatter.Format(body);

        Assert.Equal(2, groups.Count);
        Assert.Equal("Added", groups[0].Category);
        Assert.Equal("Auto-update via NetSparkle.", groups[0].Lines[0]);
        Assert.Equal("Fixed", groups[1].Category);
        Assert.Equal("Cover thumbnails surviving a rescan.", groups[1].Lines[0]);
    }

    [Fact]
    public void Format_NoSubheadings_ReturnsOneUncategorizedGroupWithRawBody()
    {
        string body = "Alpha feature set, first tagged release.";

        var groups = ChangelogBodyFormatter.Format(body);

        Assert.Single(groups);
        Assert.Null(groups[0].Category);
        Assert.Equal(body, groups[0].Lines[0]);
    }

    [Fact]
    public void Format_EmptyBody_ReturnsEmpty()
    {
        var groups = ChangelogBodyFormatter.Format("");

        Assert.Empty(groups);
    }

    [Fact]
    public void Format_MultipleBulletsUnderOneCategory_AllLinesCaptured()
    {
        string body = "### Added\n- First thing.\n- Second thing.";

        var groups = ChangelogBodyFormatter.Format(body);

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Lines.Count);
        Assert.Equal("First thing.", groups[0].Lines[0]);
        Assert.Equal("Second thing.", groups[0].Lines[1]);
    }
}
