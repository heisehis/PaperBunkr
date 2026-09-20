using System.Globalization;
using System.Text.RegularExpressions;

namespace Paperbunkr.Daemon.Indexers;

/// <summary>User-configurable filtering and scoring inputs (a projection of <c>AcquisitionSettings</c>).</summary>
public sealed record ScoringOptions
{
    /// <summary>0 = no minimum.</summary>
    public int MinSizeMb { get; init; }
    /// <summary>0 = no maximum. Packs are exempt (they are legitimately large and never auto-accepted anyway).</summary>
    public int MaxSizeMb { get; init; } = 500;
    public IReadOnlyList<string> PreferredGroups { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> IgnoredWords { get; init; } = Array.Empty<string>();
    /// <summary>Small .cbz bonus and small .cbr penalty (Paperbunkr reads both and repacks to .cbz on import).</summary>
    public bool PreferCbz { get; init; } = true;

    public double SeederWeight { get; init; } = 1.0;
    public double SeederCap { get; init; } = 100;
    public double NoSeedersPenalty { get; init; } = 50;
    public double FormatAdjustment { get; init; } = 25;
    public double PreferredGroupBonus { get; init; } = 100;
    public double YearMismatchPenalty { get; init; } = 10;
    public double PackPenalty { get; init; } = 1000;

    public static IReadOnlyList<string> SplitList(string? commaSeparated) =>
        (commaSeparated ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>An accepted release with its score. <see cref="IsPack"/> releases sort last and are never auto-grabbed.</summary>
public sealed record ScoredRelease(IndexerRelease Release, double Score, bool IsPack);

/// <summary>
/// Decides whether a raw search hit is actually the wanted issue and how good it is (docs/superpowers/specs/
/// 2026-09-19-comic-acquisition-daemon-design.md §5): title must contain the series name, the issue number
/// must match (allowing zero padding, "#", "v" forms), size and ignored-word limits apply, and multi-issue
/// packs are flagged. Year disagreement only costs score - a title may carry the volume year or the cover
/// year, so it is not grounds for rejection.
/// </summary>
public static class ReleaseEvaluator
{
    private static readonly Regex NonAlphanumeric = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);
    private static readonly Regex Stopwords = new(@"\b(the|and)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex VolumeToken = new(@"^v\d{1,2}$", RegexOptions.Compiled);
    private static readonly Regex YearToken = new(@"^(19|20)\d{2}$", RegexOptions.Compiled);
    private static readonly HashSet<string> Marker = new(StringComparer.Ordinal) { "issue", "no", "nr", "number" };
    private static readonly Regex ParenYear = new(@"\(((?:19|20)\d{2})\)", RegexOptions.Compiled);

    public static ScoredRelease? Evaluate(IndexerRelease release, IndexerQuery query, ScoringOptions options)
    {
        var title = release.Title;

        if (options.IgnoredWords.Any(w => title.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var pack = PackDetector.Detect(title);

        if (release.SizeBytes > 0)
        {
            long sizeMb = release.SizeBytes / (1024 * 1024);
            if (options.MinSizeMb > 0 && sizeMb < options.MinSizeMb)
            {
                return null;
            }

            if (!pack.IsPack && options.MaxSizeMb > 0 && sizeMb > options.MaxSizeMb)
            {
                return null;
            }
        }

        var afterName = TextAfterSeriesName(title, query.SeriesName);
        if (afterName is null)
        {
            return null; // not this series
        }

        if (pack.IsPack)
        {
            // A ranged pack must actually contain the wanted issue; a keyword-only pack ("Complete Series") is kept for the user to judge.
            if (pack.RangeStart is not null && int.TryParse(query.IssueNumber.TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out int wanted) && !pack.Covers(wanted))
            {
                return null;
            }
        }
        else if (!NumberFollowsName(afterName, query.IssueNumber))
        {
            return null;
        }

        return new ScoredRelease(release, Score(release, query, options, pack.IsPack), pack.IsPack);
    }

    private static double Score(IndexerRelease release, IndexerQuery query, ScoringOptions options, bool isPack)
    {
        double score = release.Seeders <= 0
            ? -options.NoSeedersPenalty
            : Math.Min(release.Seeders, options.SeederCap) * options.SeederWeight;

        if (options.PreferCbz)
        {
            if (release.Title.Contains("cbz", StringComparison.OrdinalIgnoreCase)) score += options.FormatAdjustment;
            else if (release.Title.Contains("cbr", StringComparison.OrdinalIgnoreCase)) score -= options.FormatAdjustment;
        }

        if (options.PreferredGroups.Any(g => release.Title.Contains(g, StringComparison.OrdinalIgnoreCase)))
        {
            score += options.PreferredGroupBonus;
        }

        var years = ParenYear.Matches(release.Title).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        if (query.Year is int year && years.Count > 0 && !years.Contains(year))
        {
            score -= options.YearMismatchPenalty;
        }

        return isPack ? score - options.PackPenalty : score;
    }

    /// <summary>
    /// Returns the normalized text that follows the series name, or null when the title doesn't contain the
    /// series as a run of whole words. Matching is on letters/digits only, so "X-Men" matches "X Men".
    /// </summary>
    private static string? TextAfterSeriesName(string title, string seriesName)
    {
        var n = Normalize(seriesName);
        if (n.Length == 0)
        {
            return null;
        }

        var t = " " + Normalize(title) + " ";
        int index = t.IndexOf(" " + n + " ", StringComparison.Ordinal);
        if (index >= 0)
        {
            return t[(index + n.Length + 1)..];
        }

        // Retry without stopwords on both sides ("The Boys" vs "Boys", "Tom and Jerry" vs "Tom Jerry").
        var tSimple = " " + Normalize(Stopwords.Replace(title, " ")) + " ";
        var nSimple = Normalize(Stopwords.Replace(seriesName, " "));
        if (nSimple.Length == 0)
        {
            return null;
        }

        index = tSimple.IndexOf(" " + nSimple + " ", StringComparison.Ordinal);
        return index >= 0 ? tSimple[(index + nSimple.Length + 1)..] : null;
    }

    /// <summary>
    /// The issue number has to come right after the series name, allowing only volume/issue markers in between
    /// ("v2", "vol 2", "issue", "no") and one year. Finding the number anywhere later in the title would accept
    /// "Spawn Origins 5" for "Spawn" #5.
    /// </summary>
    private static bool NumberFollowsName(string afterName, string issueNumber)
    {
        var number = issueNumber.Trim().TrimStart('#').Trim();
        if (number.Length == 0)
        {
            return false;
        }

        var tokens = afterName.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        int i = 0;
        while (i < tokens.Count)
        {
            var tok = tokens[i];
            if (VolumeToken.IsMatch(tok) || Marker.Contains(tok))
            {
                i++;
            }
            else if ((tok is "vol" or "volume") && i + 1 < tokens.Count && tokens[i + 1].All(char.IsAsciiDigit))
            {
                i += 2;
            }
            else
            {
                break;
            }
        }

        // One leading year ("Batman 2016 005") is tolerated, unless the year itself is the wanted number.
        var rest = tokens.Skip(i).ToList();
        if (rest.Count >= 2 && YearToken.IsMatch(rest[0]) && !IsWanted(rest[0], number))
        {
            rest = rest.Skip(1).ToList();
        }

        if (rest.Count == 0)
        {
            return false;
        }

        if (number.All(char.IsAsciiDigit))
        {
            // Whole-token compare so "5" matches "5", "05", "005" but never "15" or "2016".
            return IsWanted(rest[0], number);
        }

        // Non-integer numbers ("1.5", "Annual 1"): the normalized words must open the remaining text.
        var wantedTokens = Normalize(number).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return rest.Count >= wantedTokens.Length && rest.Take(wantedTokens.Length).SequenceEqual(wantedTokens);
    }

    private static bool IsWanted(string token, string number) =>
        number.All(char.IsAsciiDigit) && token.All(char.IsAsciiDigit)
        && int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int have)
        && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int wanted)
        && have == wanted;

    private static string Normalize(string text) =>
        NonAlphanumeric.Replace(text.ToLowerInvariant(), " ").Trim();
}
