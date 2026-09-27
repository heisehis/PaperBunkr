using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Paperbunkr.App.Services;

/// <summary>The Keep a Changelog category of a group, which picks its tag colour. Anything unrecognised is <see cref="Other"/>.</summary>
public enum ChangelogTagKind
{
    Added,
    Changed,
    Fixed,
    Removed,
    Security,
    Other,
}

/// <summary>One category group (e.g. "Added") within a changelog entry's body, or an uncategorized one (<see cref="Category"/> null).</summary>
public sealed record ChangelogBodyGroup(string? Category, ChangelogTagKind Kind, IReadOnlyList<MdBlock> Blocks)
{
    /// <summary>Removed and Security share the danger-coloured tag.</summary>
    public bool IsRemovalOrSecurity => Kind is ChangelogTagKind.Removed or ChangelogTagKind.Security;
}

/// <summary>
/// Splits a <see cref="ChangelogEntry.Body"/> on its "### Added" / "### Fixed" sub-headings into category groups, each parsed with
/// <see cref="MarkdownLite"/> so hard-wrapped bullets and inline bold render as one piece of text (docs/superpowers/specs/
/// 2026-09-26-about-polish-design.md §3). View-layer only: <see cref="ChangelogParser"/>/<see cref="ChangelogEntry"/> are untouched.
/// </summary>
public static class ChangelogBodyFormatter
{
    private static readonly Regex CategoryHeadingPattern = new(@"^###\s*(?<category>.+?)\s*$", RegexOptions.Multiline);

    public static IReadOnlyList<ChangelogBodyGroup> Format(string body)
    {
        var groups = new List<ChangelogBodyGroup>();
        var matches = CategoryHeadingPattern.Matches(body);

        string preamble = (matches.Count == 0 ? body : body[..matches[0].Index]).Trim();
        if (preamble.Length > 0)
        {
            groups.Add(new ChangelogBodyGroup(null, ChangelogTagKind.Other, MarkdownLite.Parse(preamble)));
        }

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            string category = match.Groups["category"].Value;
            int contentStart = match.Index + match.Length;
            int contentEnd = i + 1 < matches.Count ? matches[i + 1].Index : body.Length;
            var blocks = MarkdownLite.Parse(body[contentStart..contentEnd]);
            if (blocks.Count > 0)
            {
                groups.Add(new ChangelogBodyGroup(category, KindOf(category), blocks));
            }
        }

        return groups;
    }

    public static ChangelogTagKind KindOf(string category) => category.Trim().ToLowerInvariant() switch
    {
        "added" => ChangelogTagKind.Added,
        "changed" => ChangelogTagKind.Changed,
        "fixed" => ChangelogTagKind.Fixed,
        "removed" => ChangelogTagKind.Removed,
        "security" => ChangelogTagKind.Security,
        _ => ChangelogTagKind.Other,
    };
}
