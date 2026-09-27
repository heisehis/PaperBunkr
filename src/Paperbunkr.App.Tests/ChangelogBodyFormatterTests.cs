using System.IO;
using System.Linq;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ChangelogBodyFormatter"/> (docs/superpowers/specs/2026-09-26-about-polish-design.md §3) - pure string parsing
/// over a <see cref="ChangelogEntry.Body"/> string, no I/O except the one real-file check.
/// </summary>
public class ChangelogBodyFormatterTests
{
    [Fact]
    public void Format_TwoSubheadings_ReturnsTwoTaggedGroups()
    {
        string body = "### Added\n- Auto-update via NetSparkle.\n\n### Fixed\n- Cover thumbnails surviving a rescan.";

        var groups = ChangelogBodyFormatter.Format(body);

        Assert.Equal(2, groups.Count);
        Assert.Equal("Added", groups[0].Category);
        Assert.Equal(ChangelogTagKind.Added, groups[0].Kind);
        Assert.Equal("Auto-update via NetSparkle.", groups[0].Blocks[0].PlainText);
        Assert.Equal(MdBlockKind.Bullet, groups[0].Blocks[0].Kind);
        Assert.Equal(ChangelogTagKind.Fixed, groups[1].Kind);
    }

    [Fact]
    public void Format_NoSubheadings_ReturnsOneUncategorizedGroup()
    {
        string body = "Alpha feature set, first tagged release.";

        var group = Assert.Single(ChangelogBodyFormatter.Format(body));

        Assert.Null(group.Category);
        Assert.Equal(body, group.Blocks[0].PlainText);
    }

    [Fact]
    public void Format_EmptyBody_ReturnsEmpty()
    {
        Assert.Empty(ChangelogBodyFormatter.Format(""));
    }

    [Fact]
    public void Format_HardWrappedBullet_IsOneBlockWithBoldRun()
    {
        string body = "### Added\n- **Comic acquisition.** A Mylar-style want-list built on your own Prowlarr and qBittorrent.\n  Preferences → **Acquisition** connects them.\n- Second thing.";

        var group = Assert.Single(ChangelogBodyFormatter.Format(body));

        Assert.Equal(2, group.Blocks.Count);
        Assert.Equal(MdRunStyle.Bold, group.Blocks[0].Runs[0].Style);
        Assert.EndsWith("qBittorrent. Preferences → Acquisition connects them.", group.Blocks[0].PlainText);
    }

    [Theory]
    [InlineData("Changed", ChangelogTagKind.Changed)]
    [InlineData("removed", ChangelogTagKind.Removed)]
    [InlineData("Security", ChangelogTagKind.Security)]
    [InlineData("Deprecated", ChangelogTagKind.Other)]
    public void KindOf_MapsKeepAChangelogCategories(string category, ChangelogTagKind kind)
    {
        Assert.Equal(kind, ChangelogBodyFormatter.KindOf(category));
    }

    [Fact]
    public void RealChangelog_EveryEntryFormatsWithoutLeftoverMarkup()
    {
        string path = Path.Combine(MarkdownLiteTests.FindRepoRoot(), "CHANGELOG.md");
        var entries = ChangelogParser.Parse(File.ReadAllText(path));

        foreach (var entry in entries.Where(e => e.Body.Length > 0))
        {
            var runs = ChangelogBodyFormatter.Format(entry.Body).SelectMany(g => g.Blocks).SelectMany(b => b.Runs)
                .Where(r => r.Style != MdRunStyle.Code);
            Assert.All(runs, r => Assert.DoesNotContain("**", r.Text));
        }
    }
}
