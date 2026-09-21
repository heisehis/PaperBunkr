using System.Globalization;
using System.Text.RegularExpressions;

namespace Paperbunkr.Daemon.Indexers;

/// <summary>One round of queries. <see cref="Sanitized"/> is false for the strict (exact-name) pass.</summary>
public sealed record QueryPass(bool Sanitized, IReadOnlyList<string> Queries);

/// <summary>
/// Builds the search text for a wanted issue (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-
/// design.md §5). Two passes, tried in order by <see cref="ReleaseSearcher"/>:
/// <list type="number">
///   <item><b>Strict</b> - the exact series name, punctuation intact, so "X-Men", "Spider-Man" and "+Anima"
///   are searched as written.</item>
///   <item><b>Sanitized</b> - a Mylar-style alias with punctuation and stopwords stripped; only used when the
///   strict pass finds nothing acceptable, and only when it actually differs from the strict text.</item>
/// </list>
/// Each pass crosses the name with the issue-number variants (unpadded, 2- and 3-digit zero padding), then a
/// year form and a volume form.
/// </summary>
public static class QueryBuilder
{
    private static readonly Regex Punctuation = new(@"[:,'’""!?.()\[\]+]", RegexOptions.Compiled);
    private static readonly Regex Separators = new(@"[-–—/&]", RegexOptions.Compiled);
    private static readonly Regex Stopwords = new(@"\b(the|and)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    public static IReadOnlyList<QueryPass> Build(IndexerQuery query)
    {
        var numbers = NumberVariants(query.IssueNumber);
        var strictName = Collapse(query.SeriesName);
        var passes = new List<QueryPass> { new(false, Compose(strictName, numbers, query)) };

        var sanitized = Sanitize(query.SeriesName);
        if (sanitized.Length > 0 && !sanitized.Equals(strictName, StringComparison.OrdinalIgnoreCase))
        {
            passes.Add(new QueryPass(true, Compose(sanitized, numbers, query)));
        }

        return passes;
    }

    /// <summary>"5" -> 5, 05, 005; "005" -> the same; "1.5" and "Annual 1" are left exactly as given.</summary>
    public static IReadOnlyList<string> NumberVariants(string issueNumber)
    {
        var text = issueNumber.Trim().TrimStart('#').Trim();
        if (text.Length > 0 && text.All(char.IsAsciiDigit) && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int n))
        {
            return new[] { n.ToString(CultureInfo.InvariantCulture), n.ToString("00", CultureInfo.InvariantCulture), n.ToString("000", CultureInfo.InvariantCulture) }
                .Distinct().ToList();
        }

        return new[] { text };
    }

    public static string Sanitize(string name)
    {
        var text = Separators.Replace(name, " ");
        text = Punctuation.Replace(text, string.Empty);
        text = Stopwords.Replace(text, " ");
        return Collapse(text);
    }

    private static List<string> Compose(string name, IReadOnlyList<string> numbers, IndexerQuery query)
    {
        var queries = numbers.Select(n => $"{name} {n}").ToList();

        if (query.Year is int year)
        {
            queries.Add($"{name} {numbers[0]} {year}");
        }

        if (query.Volume is int volume)
        {
            queries.Add($"{name} v{volume} {numbers[0]}");
        }

        return queries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Collapse(string text) => Spaces.Replace(text.Trim(), " ");
}
