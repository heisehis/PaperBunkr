using System.Text.RegularExpressions;

namespace Paperbunkr.Data.ReadingLists.Matching;

/// <summary>
/// The one place that decides when two issue numbers are the same issue (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md
/// §5.1, decision Q16). Same: <c>1</c>, <c>001</c>, <c>#1</c>; and <c>½</c>, <c>1/2</c>, <c>0.5</c>; ignoring case and spacing.
/// Different, on purpose: <c>1</c> / <c>1.AU</c> / <c>1A</c> / <c>Annual 1</c>, and <c>1.1</c> / <c>1.10</c> (point issues count up, so a
/// trailing zero is not padding).
/// </summary>
public static partial class IssueNumberKey
{
    /// <summary>The comparison key; empty when the number is blank. Two numbers are the same issue exactly when their keys are equal and not empty.</summary>
    public static string Of(string? number)
    {
        string s = Whitespace().Replace((number ?? string.Empty).Trim().TrimStart('#').Trim(), " ").ToLowerInvariant();
        if (s.Length == 0)
        {
            return string.Empty;
        }

        s = s.Replace("½", "1/2").Replace("¼", "1/4").Replace("¾", "3/4");
        switch (s)
        {
            case "1/2": return "0.5";
            case "1/4": return "0.25";
            case "3/4": return "0.75";
        }

        var numeric = LeadingNumber().Match(s);
        string rest = numeric.Success ? numeric.Groups["rest"].Value : string.Empty;
        if (!numeric.Success || (numeric.Groups["whole"].Length == 0 && !rest.StartsWith('.')))
        {
            return s;       // "annual 1", "-1": not a number with something after it
        }

        string whole = numeric.Groups["whole"].Value.TrimStart('0');
        return (whole.Length == 0 ? "0" : whole) + numeric.Groups["rest"].Value;
    }

    public static bool Same(string? a, string? b)
    {
        string x = Of(a);
        return x.Length > 0 && x == Of(b);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // The digits before anything else: "007" / "01.5" / "001a" / "1.au" / ".5" (no whole part).
    [GeneratedRegex(@"^(?<whole>\d*)(?<rest>(\.\d+)?([^\d.].*|\..*)?)$")]
    private static partial Regex LeadingNumber();
}
