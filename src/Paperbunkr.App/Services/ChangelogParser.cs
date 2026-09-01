using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Paperbunkr.App.Services;

/// <summary>One version's changelog entry (docs/superpowers/specs/2026-09-01-auto-update-and-changelog-design.md).</summary>
public sealed record ChangelogEntry(string Version, string? Date, string Body);

/// <summary>
/// Parses <c>CHANGELOG.md</c> (Keep a Changelog format) into per-version entries. Shared by the
/// Preferences → About section (renders every entry) and the update-available overlay (renders just
/// the newest entry's <see cref="ChangelogEntry.Body"/>) - one parser, two consumers, not two
/// implementations of changelog rendering.
/// </summary>
public static class ChangelogParser
{
    // "## [0.2.0-beta] - 2026-09-01" - the date suffix is optional so a heading with no date still parses.
    private static readonly Regex HeadingPattern =
        new(@"^##\s*\[(?<version>[^\]]+)\](?:\s*-\s*(?<date>\S+))?\s*$", RegexOptions.Multiline);

    /// <summary>Parses <paramref name="markdown"/> into entries, newest-first (the order headings appear in the file).</summary>
    public static IReadOnlyList<ChangelogEntry> Parse(string markdown)
    {
        var entries = new List<ChangelogEntry>();
        var matches = HeadingPattern.Matches(markdown);

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            int bodyStart = match.Index + match.Length;
            int bodyEnd = i + 1 < matches.Count ? matches[i + 1].Index : markdown.Length;
            string body = markdown[bodyStart..bodyEnd].Trim();

            string version = match.Groups["version"].Value.Trim();
            string? date = match.Groups["date"].Success ? match.Groups["date"].Value.Trim() : null;

            entries.Add(new ChangelogEntry(version, date, body));
        }

        return entries;
    }
}
