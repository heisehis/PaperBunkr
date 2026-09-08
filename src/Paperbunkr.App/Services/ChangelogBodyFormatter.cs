using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Paperbunkr.App.Services;

/// <summary>One category group (e.g. "Added") within a changelog entry's body, or an uncategorized fallback.</summary>
public sealed record ChangelogBodyGroup(string? Category, IReadOnlyList<string> Lines);

/// <summary>
/// Splits a <see cref="ChangelogEntry.Body"/> string on its "### Added" / "### Fixed" sub-headings
/// into category-tagged groups for the About section's accordion (docs/superpowers/specs/
/// 2026-09-07-about-redesign-design.md §Architecture 3). View-layer only - does not touch
/// <see cref="ChangelogParser"/>/<see cref="ChangelogEntry"/>, which the update-available overlay
/// also depends on.
/// </summary>
public static class ChangelogBodyFormatter
{
    private static readonly Regex CategoryHeadingPattern = new(@"^###\s*(?<category>.+)$", RegexOptions.Multiline);

    public static IReadOnlyList<ChangelogBodyGroup> Format(string body)
    {
        var matches = CategoryHeadingPattern.Matches(body);
        if (matches.Count == 0)
        {
            return body.Length == 0
                ? []
                : [new ChangelogBodyGroup(null, [body])];
        }

        var groups = new List<ChangelogBodyGroup>();
        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            string category = match.Groups["category"].Value.Trim();
            int contentStart = match.Index + match.Length;
            int contentEnd = i + 1 < matches.Count ? matches[i + 1].Index : body.Length;
            string content = body[contentStart..contentEnd].Trim();

            IReadOnlyList<string> lines = content.Length == 0
                ? []
                : content.Replace("\r\n", "\n").Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .Select(l => l.StartsWith("- ") ? l[2..] : l)
                    .ToArray();

            groups.Add(new ChangelogBodyGroup(category, lines));
        }

        return groups;
    }
}
