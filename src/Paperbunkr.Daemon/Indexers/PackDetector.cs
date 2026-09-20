using System.Globalization;
using System.Text.RegularExpressions;

namespace Paperbunkr.Daemon.Indexers;

/// <summary>Result of <see cref="PackDetector.Detect"/>. A pack with a numeric range knows which issues it covers.</summary>
public sealed record PackInfo(bool IsPack, int? RangeStart = null, int? RangeEnd = null)
{
    public static readonly PackInfo NotAPack = new(false);

    public bool Covers(int issue) => RangeStart is int s && RangeEnd is int e && issue >= s && issue <= e;
}

/// <summary>
/// Spots multi-issue releases ("v1-6", "#1-12", "Complete Series", "Weekly Pack") so they are never
/// auto-accepted for a single-issue want (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §5).
/// Year ranges ("1985-2026") and dates ("2026-09-16") are not issue ranges.
/// </summary>
public static class PackDetector
{
    // Optional "#", "v" or "vol" in front of either number: "#1-12", "v1-6", "Vol 1-6", "001 to 050".
    private static readonly Regex Range = new(@"(?<![\w.])(?:#|v(?:ol\.?)?\s*)?(\d{1,4})\s*(?:-|–|—|\bto\b)\s*(?:#|v(?:ol\.?)?\s*)?(\d{1,4})(?![\w.])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Keywords = new(@"\b(complete(\s+(series|collection|run))?|collection|mega\s?pack|weekly\s+pack|pack|bundle|\d+\s+issues)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static PackInfo Detect(string title)
    {
        foreach (Match match in Range.Matches(title))
        {
            int start = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            int end = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

            bool looksLikeYearOrDate = start >= 1900 || end >= 1900;
            if (!looksLikeYearOrDate && start < end)
            {
                return new PackInfo(true, start, end);
            }
        }

        return Keywords.IsMatch(title) ? new PackInfo(true) : PackInfo.NotAPack;
    }
}
