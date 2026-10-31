using System.Net;
using System.Text.RegularExpressions;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Calibre;

/// <summary>Pure mapping from Calibre's values to Paperbunkr's (docs/superpowers/specs/2026-10-27-books-slice4a-calibre-import-design.md §3).</summary>
public static class CalibreMapper
{
    /// <summary>Preference when a book has several formats: one Paperbunkr book per Calibre book.</summary>
    private static readonly string[] FormatOrder = ["EPUB", "FB2", "MOBI", "AZW3", "AZW", "PDF"];

    public static (CalibreFormat Format, BookFormat BookFormat)? ChooseFormat(IEnumerable<CalibreFormat> formats)
    {
        var list = formats.ToList();
        foreach (string wanted in FormatOrder)
        {
            var match = list.FirstOrDefault(f => string.Equals(f.Format, wanted, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return (match, wanted switch
                {
                    "EPUB" => BookFormat.Epub,
                    "FB2" => BookFormat.Fb2,
                    "PDF" => BookFormat.Pdf,
                    _ => BookFormat.Mobi,
                });
            }
        }

        return null;
    }

    /// <summary><c>{root}/{books.path}/{data.name}.{format}</c>; Calibre stores the path with forward slashes.</summary>
    public static string ResolveFilePath(string libraryRoot, CalibreBook book, CalibreFormat format) =>
        Path.Combine(libraryRoot, Path.Combine(book.RelativePath.Split('/', '\\')), format.FileName + "." + format.Format.ToLowerInvariant());

    public static string? CoverPath(string libraryRoot, CalibreBook book) =>
        book.HasCover ? Path.Combine(libraryRoot, Path.Combine(book.RelativePath.Split('/', '\\')), "cover.jpg") : null;

    /// <summary>Calibre stores a comma inside an author name as "|".</summary>
    public static string? JoinAuthors(IEnumerable<string> authors)
    {
        var names = authors.Select(a => a.Replace('|', ',').Trim()).Where(a => a.Length > 0).ToList();
        return names.Count == 0 ? null : string.Join(", ", names);
    }

    /// <summary>Calibre's rating is 0-10 (two points a star); Paperbunkr's is 0.5-5.</summary>
    public static float? MapRating(int rating10) => rating10 <= 0 ? null : BookRating.Normalize(rating10 / 2f);

    private static readonly Regex ParagraphBreaks = new(@"</(p|div|h[1-6])>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LineBreaks = new(@"</(li|tr)>|<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"[ \t ]+", RegexOptions.Compiled);
    private static readonly Regex BlankLines = new(@"\n\s*\n\s*", RegexOptions.Compiled);

    /// <summary>Calibre comments are HTML; the Summary is plain text with paragraph breaks kept.</summary>
    public static string? HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        string text = LineBreaks.Replace(ParagraphBreaks.Replace(html, "\n\n"), "\n");
        text = Tags.Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text).Replace("\r", string.Empty);
        text = Spaces.Replace(text, " ");
        text = BlankLines.Replace(text, "\n\n").Trim();
        return text.Length == 0 ? null : text;
    }

    private static readonly Dictionary<string, string> LanguageCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eng"] = "en", ["fra"] = "fr", ["fre"] = "fr", ["deu"] = "de", ["ger"] = "de", ["spa"] = "es", ["ita"] = "it",
        ["por"] = "pt", ["nld"] = "nl", ["dut"] = "nl", ["rus"] = "ru", ["jpn"] = "ja", ["zho"] = "zh", ["chi"] = "zh",
        ["kor"] = "ko", ["pol"] = "pl", ["swe"] = "sv", ["nor"] = "no", ["dan"] = "da", ["fin"] = "fi", ["tur"] = "tr",
        ["ara"] = "ar", ["heb"] = "he", ["hin"] = "hi", ["ces"] = "cs", ["cze"] = "cs", ["ell"] = "el", ["gre"] = "el",
        ["lat"] = "la", ["ukr"] = "uk", ["hun"] = "hu",
    };

    /// <summary>Calibre keeps ISO 639-2 codes ("eng"); Paperbunkr uses the two-letter form.</summary>
    public static string? MapLanguage(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        string trimmed = code.Trim();
        return trimmed.Length == 2 ? trimmed.ToLowerInvariant() : LanguageCodes.GetValueOrDefault(trimmed, trimmed.ToLowerInvariant());
    }

    public static BookFieldValues ToFieldValues(CalibreBook book) => new()
    {
        Publisher = book.Publisher,
        LanguageISO = MapLanguage(book.Language),
        ISBN = book.Isbn,
        SeriesName = book.Series,
        SeriesIndex = book.SeriesIndex,
        Summary = HtmlToText(book.Comments),
        Rating = MapRating(book.Rating10),
        Tags = book.Tags,
    };

    /// <summary>Lower-case letters and digits only, for the "same title and author" match.</summary>
    public static string NormalizeForMatch(string? text) =>
        new string((text ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
