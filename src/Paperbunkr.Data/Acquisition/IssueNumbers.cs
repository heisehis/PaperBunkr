using System.Globalization;
using System.Text.RegularExpressions;

namespace Paperbunkr.Data.Acquisition;

/// <summary>Issue numbers are text ("5", "05", "#5", "1.5", "Annual 1"); this is the one place that decides when two are the same issue.</summary>
public static class IssueNumbers
{
    public static bool Equal(string? a, string? b)
    {
        var x = Clean(a);
        var y = Clean(b);
        if (x.Length == 0 || y.Length == 0)
        {
            return false;
        }

        if (int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out int nx) &&
            int.TryParse(y, NumberStyles.None, CultureInfo.InvariantCulture, out int ny))
        {
            return nx == ny; // "5" == "05" == "005"
        }

        return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
    }

    private static string Clean(string? number) => (number ?? string.Empty).Trim().TrimStart('#').Trim();
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
