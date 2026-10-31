using System.Net;
using System.Text.RegularExpressions;

namespace Paperbunkr.App.Services.Books;

/// <summary>
/// Turns a book description as metadata sources deliver it (Markdown from Open Library, HTML from Google Books and EPUB OPF files) into
/// plain readable text for Book Detail's flap copy and the Books spread's quote: headings keep their words, emphasis loses its markers,
/// links keep their text, reference lines ("[1]: https://…") go, bullets become "•". Never throws; text without markup comes back trimmed.
/// </summary>
public static partial class SynopsisText
{
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string s = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // HTML: line breaks and block ends become newlines, every other tag goes, entities decode.
        if (HtmlTag().IsMatch(s))
        {
            s = HtmlBreak().Replace(s, "\n");
            s = HtmlBlockEnd().Replace(s, "\n\n");
            s = HtmlListItem().Replace(s, "\n- ");
            s = HtmlTag().Replace(s, string.Empty);
        }

        s = WebUtility.HtmlDecode(s);

        s = ReferenceDefinition().Replace(s, string.Empty);
        s = ReferenceLink().Replace(s, "${text}");
        s = InlineLink().Replace(s, "${text}");
        s = AutoLink().Replace(s, "${url}");
        s = Heading().Replace(s, "${text}");
        s = Rule().Replace(s, string.Empty);
        s = Bullet().Replace(s, "• ");
        s = Emphasis().Replace(s, "${inner}");
        s = Emphasis().Replace(s, "${inner}"); // nested, e.g. "***x***" read as "**" around "*x*"
        s = Underscores().Replace(s, "${inner}");
        s = Backslash().Replace(s, "${ch}");

        s = TrailingSpace().Replace(s, "\n");
        s = BlankRuns().Replace(s, "\n\n");
        return s.Trim();
    }

    [GeneratedRegex(@"</?[a-zA-Z][^>]*>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlBreak();

    [GeneratedRegex(@"</(p|div|h[1-6]|ul|ol|blockquote)>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlBlockEnd();

    [GeneratedRegex(@"<li[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlListItem();

    [GeneratedRegex(@"^[ \t]*\[[^\]\n]+\]:[ \t]*\S.*$\n?", RegexOptions.Multiline)]
    private static partial Regex ReferenceDefinition();

    [GeneratedRegex(@"\[(?<text>[^\]\n]+)\]\[[^\]\n]*\]")]
    private static partial Regex ReferenceLink();

    [GeneratedRegex(@"\[(?<text>[^\]\n]+)\]\([^)\s]+(?:\s+""[^""]*"")?\)")]
    private static partial Regex InlineLink();

    [GeneratedRegex(@"<(?<url>https?://[^>\s]+)>")]
    private static partial Regex AutoLink();

    [GeneratedRegex(@"^[ \t]*#{1,6}[ \t]+(?<text>.*?)[ \t]*#*[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^[ \t]*([-*_])([ \t]*\1){2,}[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Rule();

    [GeneratedRegex(@"^[ \t]*[-*+][ \t]+", RegexOptions.Multiline)]
    private static partial Regex Bullet();

    // One to three asterisks around a run that neither starts nor ends with a space; the markers must not touch a word on the outside,
    // so "2*3*4" stays as it is.
    [GeneratedRegex(@"(?<![\w*])(?<m>\*{1,3})(?<inner>[^\s*](?:[^\n]*?[^\s*])?)\k<m>(?![\w*])")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"(?<![\w_])(?<m>_{1,3})(?<inner>[^\s_](?:[^\n]*?[^\s_])?)\k<m>(?![\w_])")]
    private static partial Regex Underscores();

    [GeneratedRegex(@"\\(?<ch>[\\`*_{}\[\]()#+\-.!])")]
    private static partial Regex Backslash();

    [GeneratedRegex(@"[ \t]+\n")]
    private static partial Regex TrailingSpace();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankRuns();
}
