using System.Text.RegularExpressions;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Metadata.ScanCredits;

/// <summary>
/// Finds the scanner or release group named in a comic's file name (docs/superpowers/specs/2026-10-11-scan-credits-design.md), the job of ComicRack's "Scan Information From
/// Filename" script. Release names put the group last, in brackets: <c>Saga 054 (2018) (Digital) (Zone-Empire).cbz</c>. So the bracketed pieces are read from the end, skipping the
/// ones that are only noise (a year, "Digital", "of 4", a language) and the ones that merely repeat something the book already says about itself; failing that, a known group
/// name anywhere in the name is used. Nothing is guessed: no candidate means no result.
/// </summary>
public static class ScanCreditDetector
{
    private static readonly Regex Bracketed = new(@"[\(\[]([^()\[\]]*)[\)\]]", RegexOptions.Compiled);
    private static readonly Regex Word = new(@"[^\s_\-|/,.&]+", RegexOptions.Compiled);
    private static readonly Regex Parenthetical = new(@"\(([^()]*)\)", RegexOptions.Compiled);

    /// <summary>
    /// The scanner named in <paramref name="fileName"/> (with or without its extension), or null. <paramref name="knownValues"/> are things the book already says about itself
    /// (series, title, publisher...): a bracketed piece equal to one of them is a part of the name, not a credit.
    /// </summary>
    public static string? Detect(string? fileName, IReadOnlyCollection<string> knownValues, ScanCreditCatalog? catalog = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        catalog ??= ScanCreditCatalog.Default;
        string name = StripComicExtension(fileName);
        var known = new HashSet<string>(knownValues.Select(v => v.Trim()).Where(v => v.Length > 0), StringComparer.OrdinalIgnoreCase);

        foreach (string candidate in Candidates(name).Reverse())
        {
            string? credit = Credit(candidate, catalog);
            if (credit is not null && !known.Contains(credit) && !known.Contains(candidate.Trim()))
            {
                return credit;
            }
        }

        // No usable bracket: a known group name written into the file name on its own.
        foreach (string scanner in catalog.Scanners)
        {
            if (Regex.IsMatch(name, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(scanner)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return scanner;
            }
        }

        return null;
    }

    /// <summary>The scanner for <paramref name="issue"/>'s file, checked against the book's own text fields. Needs <see cref="Issue.Series"/> loaded.</summary>
    public static string? Detect(Issue issue, ScanCreditCatalog? catalog = null) =>
        string.IsNullOrEmpty(issue.FilePath) ? null : Detect(Path.GetFileName(issue.FilePath), KnownValuesOf(issue), catalog);

    /// <summary>
    /// Adds <paramref name="credit"/> to a comma-separated Scan Information value, unless it is already there (ignoring case); the entries come back sorted, as the script leaves them.
    /// Returns null when nothing would change.
    /// </summary>
    public static string? Merge(string? scanInformation, string credit)
    {
        var entries = (scanInformation ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (entries.Contains(credit, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        entries.Add(credit);
        entries.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join(", ", entries);
    }

    /// <summary>Everything the book says about itself in text: each text field, the entries of its list fields, and anything in parentheses inside its series and titles.</summary>
    public static IReadOnlyCollection<string> KnownValuesOf(Issue issue)
    {
        var values = new List<string>();
        foreach (Func<Issue, string> selector in SmartListCatalog.TextSelectors
                     .Where(p => p.Key is not (SmartListField.File or SmartListField.FullPath or SmartListField.Directory or SmartListField.ScanInformation or SmartListField.Continuity))
                     .Select(p => p.Value))
        {
            string value = selector(issue);
            if (value.Length == 0)
            {
                continue;
            }

            values.Add(value);
            values.AddRange(value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        foreach (string? titled in new[] { issue.Series?.Name, issue.Title, issue.AlternateSeries })
        {
            values.AddRange(Parenthetical.Matches(titled ?? string.Empty).Select(m => m.Groups[1].Value));
        }

        return values;
    }

    /// <summary>The bracketed pieces of the name in order, then the tail after a final double underscore (<c>Name 001__Group</c>), another way groups sign a file.</summary>
    private static IEnumerable<string> Candidates(string name)
    {
        foreach (Match match in Bracketed.Matches(name))
        {
            yield return match.Groups[1].Value;
        }

        int doubleUnderscore = name.LastIndexOf("__", StringComparison.Ordinal);
        if (doubleUnderscore >= 0 && doubleUnderscore + 2 < name.Length)
        {
            string tail = name[(doubleUnderscore + 2)..];
            if (!tail.Contains('(') && !tail.Contains('['))
            {
                yield return tail;
            }
        }
    }

    /// <summary>
    /// The credit inside a bracketed piece: the piece with the noise words trimmed from both ends ("digital-Empire" is "Empire"), or null when nothing but noise is left, it is
    /// shorter than two characters, or it has no letter in it.
    /// </summary>
    private static string? Credit(string candidate, ScanCreditCatalog catalog)
    {
        var words = Word.Matches(candidate).Where(w => !catalog.IsNoiseToken(w.Value)).ToList();
        if (words.Count == 0)
        {
            return null;
        }

        int start = words[0].Index;
        int end = words[^1].Index + words[^1].Length;
        string credit = candidate[start..end].Trim();

        // A known group name is kept whole even when it starts with a noise word ("digital-Empire").
        string? knownName = catalog.Scanners.FirstOrDefault(s => string.Equals(s, candidate.Trim(' ', '_', ','), StringComparison.OrdinalIgnoreCase));
        if (knownName is not null)
        {
            return knownName;
        }

        return credit.Length >= 2 && credit.Any(char.IsLetter) ? credit : null;
    }

    private static string StripComicExtension(string fileName)
    {
        string extension = Path.GetExtension(fileName);
        return extension.Length is > 1 and <= 5 && extension.Skip(1).All(char.IsLetterOrDigit) ? fileName[..^extension.Length] : fileName;
    }
}
