using System.Text.RegularExpressions;

namespace Paperbunkr.Data.Acquisition;

/// <summary>Issue numbers are text ("5", "05", "#5", "1.5", "Annual 1"); this is the one place that decides when two are the same issue.</summary>
public static class IssueNumbers
{
    /// <summary>Forwards to <see cref="ReadingLists.Matching.IssueNumberKey"/>, which owns the rules.</summary>
    public static bool Equal(string? a, string? b) => ReadingLists.Matching.IssueNumberKey.Same(a, b);
}

/// <summary>Series-name comparison for matching a placeholder's series to a ComicVine volume: letters and digits only, no leading "the".</summary>
public static class SeriesNames
{
    private static readonly Regex NonAlphanumeric = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);
    private static readonly Regex Stopwords = new(@"\b(the|and)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Key(string? name) =>
        NonAlphanumeric.Replace(Stopwords.Replace((name ?? string.Empty).ToLowerInvariant(), " "), " ").Trim();

    public static bool Same(string? a, string? b)
    {
        var x = Key(a);
        return x.Length > 0 && x == Key(b);
    }
}
