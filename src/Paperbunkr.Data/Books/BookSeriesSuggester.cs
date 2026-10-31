using System.Globalization;
using System.Text.RegularExpressions;

namespace Paperbunkr.Data.Books;

public readonly record struct BookSeriesSuggestion(string SeriesName, float? Index);

/// <summary>
/// Guesses a series from a title alone (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §4, item 13).
/// A guess is only ever <i>shown</i> as a "Suggested series" chip in Book Properties and never assigned automatically; a series a
/// file declares itself (calibre / EPUB3 collection / FB2 sequence) goes through the applier instead. Needs an explicit marker
/// ("Book 3", "Vol. 2", "#4", "(Series #2)", "Book 3 of Series") so plain titles such as "The Hobbit" or "Apollo 13" never match.
/// </summary>
public static class BookSeriesSuggester
{
    private const string Number = @"(?<n>\d+(?:\.\d+)?)";

    private static readonly Regex[] Patterns =
    {
        // "Dune Messiah (Dune Chronicles #2)" / "(Dune Chronicles, Book 2)"
        new(@"\((?<s>[^()]{2,}?)\s*,?\s*(?:#|book\s+|vol(?:ume)?\.?\s*)" + Number + @"\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // "Book 3 of The Dune Chronicles" / "Volume 2 of Foo"
        new(@"^\s*(?:book|vol(?:ume)?\.?|part)\s+" + Number + @"\s+of\s+(?:the\s+)?(?<s>.{2,})$", RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // "The Dune Chronicles, Book 2: Messiah" / "Foo - Vol. 3" / "Foo Book 3"
        new(@"^(?<s>.{2,}?)\s*[,:\-–—]?\s+(?:book|vol(?:ume)?\.?|part)\s+" + Number + @"\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // "Foo #3" / "Foo #3: Subtitle"
        new(@"^(?<s>.{2,}?)\s*,?\s+#" + Number + @"\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    /// <param name="existingSeriesNames">Names already in the library; a case-insensitive match returns the existing spelling.</param>
    public static BookSeriesSuggestion? Suggest(string? title, IEnumerable<string>? existingSeriesNames = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        foreach (var pattern in Patterns)
        {
            var match = pattern.Match(title.Trim());
            if (!match.Success)
            {
                continue;
            }

            string series = Clean(match.Groups["s"].Value);
            if (series.Length < 2 || !series.Any(char.IsLetter))
            {
                continue;
            }

            float? index = float.TryParse(match.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? n : null;
            string? existing = existingSeriesNames?.FirstOrDefault(e => string.Equals(e, series, StringComparison.OrdinalIgnoreCase));
            return new BookSeriesSuggestion(existing ?? series, index);
        }

        return null;
    }

    private static string Clean(string raw) => raw.Trim().Trim(',', ':', '-', '–', '—', ' ');
}
