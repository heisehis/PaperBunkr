using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ChangelogParser"/> (docs/superpowers/specs/2026-09-01-auto-update-and-
/// changelog-design.md) - pure string parsing, no I/O. Uses inline fixtures rather than reading the
/// real repo CHANGELOG.md, so these stay independent of that file's future edits.
/// </summary>
public class ChangelogParserTests
{
    [Fact]
    public void Parse_MultipleEntries_ReturnsInFileOrder()
    {
        string markdown = """
            # Changelog

            ## [0.2.0-beta] - 2026-09-01
            ### Added
            - Auto-update.

            ## [0.1.0-alpha] - 2026-08-09
            ### Added
            - Initial alpha.
            """;

        var entries = ChangelogParser.Parse(markdown);

        Assert.Equal(2, entries.Count);
        Assert.Equal("0.2.0-beta", entries[0].Version);
        Assert.Equal("2026-09-01", entries[0].Date);
        Assert.Contains("Auto-update.", entries[0].Body);
        Assert.Equal("0.1.0-alpha", entries[1].Version);
        Assert.Contains("Initial alpha.", entries[1].Body);
    }

    [Fact]
    public void Parse_SingleEntry_BodyExcludesHeading()
    {
        string markdown = """
            ## [1.0.0] - 2026-01-01
            ### Fixed
            - A bug.
            """;

        var entries = ChangelogParser.Parse(markdown);

        Assert.Single(entries);
        Assert.Equal("1.0.0", entries[0].Version);
        Assert.DoesNotContain("## [1.0.0]", entries[0].Body);
        Assert.Contains("A bug.", entries[0].Body);
    }

    [Fact]
    public void Parse_HeadingWithNoDate_DateIsNull()
    {
        var entries = ChangelogParser.Parse("## [Unreleased]\n- Work in progress.");

        Assert.Single(entries);
        Assert.Equal("Unreleased", entries[0].Version);
        Assert.Null(entries[0].Date);
    }

    [Fact]
    public void Parse_NoHeadings_ReturnsEmpty()
    {
        var entries = ChangelogParser.Parse("# Changelog\n\nNothing shipped yet.");

        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_TrimsLeadingAndTrailingWhitespaceFromBody()
    {
        var entries = ChangelogParser.Parse("## [1.0.0] - 2026-01-01\n\n\n   - A line.   \n\n\n## [0.9.0] - 2025-12-01\n- Older.");

        Assert.Equal("- A line.", entries[0].Body);
    }
}
