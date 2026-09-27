using System.Text.RegularExpressions;

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>
/// Port of CE's <c>cvdb.__cleanup_search_terms</c> (ComicVine Scraper plugin, verified directly against
/// source 2026-09-25) - the query massaging that runs before any series search so a title typed or parsed
/// slightly differently from ComicVine's own ("&amp;" vs "and", stray edition tags, "8" vs "eight") still
/// finds it. Applied to the automatic query and to whatever the user retypes in the review dialog alike,
/// as CE does.
/// </summary>
public static class SearchTermCleaner
{
    private static readonly Regex NoiseWords = new(@"\b(c2c|ctc|noads+|tbp)\b", RegexOptions.Compiled);
    private static readonly Regex Ampersand = new(" & ", RegexOptions.Compiled);
    private static readonly Regex WordChars = new(@"[\w':.\-]+", RegexOptions.Compiled);

    // Number words up to twenty, in CE's own map (utils.convert_number_words). The "1st" -> "first" and
    // "twelfth"/"eightteenth" special cases below are CE's, typo included.
    private static readonly (string Digits, string Word)[] NumberMap =
    [
        ("0", "zero"), ("1", "one"), ("2", "two"), ("3", "three"), ("4", "four"), ("5", "five"), ("6", "six"),
        ("7", "seven"), ("8", "eight"), ("9", "nine"), ("10", "ten"), ("11", "eleven"), ("12", "twelve"),
        ("13", "thirteen"), ("14", "fourteen"), ("15", "fifteen"), ("16", "sixteen"), ("17", "seventeen"),
        ("18", "eighteen"), ("19", "nineteen"), ("20", "twenty"), ("0th", "zeroth"), ("1rst", "first"),
        ("2nd", "second"), ("3rd", "third"), ("4th", "fourth"), ("5th", "fifth"), ("6th", "sixth"),
        ("7th", "seventh"), ("8th", "eighth"), ("9th", "ninth"), ("10th", "tenth"), ("11th", "eleventh"),
        ("12th", "twelveth"), ("13th", "thirteenth"), ("14th", "fourteenth"), ("15th", "fifteenth"),
        ("16th", "sixteenth"), ("17th", "seventeenth"), ("18th", "eighteenth"), ("19th", "nineteenth"),
        ("20th", "twentieth"),
    ];

    /// <param name="alternate">The retry form CE falls back to when the first search finds nothing:
    /// digits become words ("8" to "eight") or, if there were none to expand, words become digits.</param>
    public static string Clean(string terms, bool alternate)
    {
        string s = terms.ToLowerInvariant();
        s = Ampersand.Replace(s, " and ");
        s = NoiseWords.Replace(s, string.Empty);

        if (alternate)
        {
            string original = s;
            s = ConvertNumberWords(s, expand: true);
            if (s == original)
            {
                s = ConvertNumberWords(s, expand: false);
            }
        }

        return string.Join(' ', WordChars.Matches(s).Select(m => m.Value));
    }

    private static string ConvertNumberWords(string phrase, bool expand)
    {
        if (expand)
        {
            foreach (var (digits, word) in NumberMap)
            {
                phrase = Regex.Replace(phrase, $@"\b{digits}\b", word);
            }

            return Regex.Replace(phrase, @"\b1st\b", "first");
        }

        foreach (var (digits, word) in NumberMap)
        {
            phrase = Regex.Replace(phrase, $@"\b{word}\b", digits);
        }

        phrase = Regex.Replace(phrase, @"\btwelfth\b", "12th");
        return Regex.Replace(phrase, @"\beightteenth\b", "18th");
    }
}
