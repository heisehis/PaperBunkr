using System.Globalization;
using System.Linq;
using System.Text;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.Reader;

/// <summary>Everything <see cref="BookReaderCss.Build"/> needs, as plain values so the CSS is testable without the WebView or Avalonia brushes.</summary>
public sealed record BookReaderCssInput(
    string FontFamily,
    double FontSize,
    double LineHeight,
    double LetterSpacing,
    double WordSpacing,
    double ParagraphSpacing,
    double PageMargin,
    string Background,
    string Foreground,
    bool Justify = false,
    bool Hyphenate = false,
    bool UsePublisherStyles = false,
    bool Paper = false,
    string? Accent = null,
    string? UiFont = null);

/// <summary>
/// The reading pane's typography layer (docs/superpowers/specs/2026-09-02-books-reflow-reader-webview-redesign-design.md Step 6, extended by
/// docs/superpowers/specs/2026-10-27-books-slice3b-reader-typography-aids-design.md §3): <c>--pb-*</c> custom properties plus the
/// <c>!important</c> rules that force them onto the chapter, because bare variables would not beat an EPUB's own hard-coded colours and
/// fonts. With the book's own styling on, the forced font family, size, line height, spacing, justify and hyphenation rules are left out
/// so the chapter's markup styling shows; theme colours and the page margin still apply. With every new switch off the output is
/// unchanged from before the extraction (pinned by <c>BookReaderCssTests</c>).
/// </summary>
public static class BookReaderCss
{
    private const string SerifStack = "Georgia, Cambria, serif";

    public static string FontStack(BookFontFamilyOption option, string? customFont) => option switch
    {
        BookFontFamilyOption.Sans => "'Segoe UI', Arial, sans-serif",
        BookFontFamilyOption.Mono => "Consolas, monospace",
        BookFontFamilyOption.OpenDyslexic => "OpenDyslexic, Georgia, Cambria, serif",
        BookFontFamilyOption.Custom when CleanFontName(customFont) is { } name => $"\"{name}\", {SerifStack}",
        _ => SerifStack,
    };

    /// <summary>The <c>lang</c> the chapter document declares (hyphenation picks its dictionary from it); "en" when the book names none or something unusable.</summary>
    public static string LanguageAttribute(string? languageIso)
    {
        if (string.IsNullOrWhiteSpace(languageIso))
        {
            return "en";
        }

        string trimmed = languageIso.Trim();
        return trimmed.Length <= 12 && trimmed.All(c => char.IsAsciiLetter(c) || c == '-') ? trimmed : "en";
    }

    public static string Build(BookReaderCssInput i)
    {
        static string Num(double value) => value.ToString(CultureInfo.InvariantCulture);

        var css = new StringBuilder();
        css.Append(":root {\n")
            .Append("  --pb-font-family: ").Append(i.FontFamily).Append(";\n")
            .Append("  --pb-font-size: ").Append(Num(i.FontSize)).Append("px;\n")
            .Append("  --pb-line-height: ").Append(Num(i.LineHeight)).Append(";\n")
            .Append("  --pb-letter-spacing: ").Append(Num(i.LetterSpacing)).Append("px;\n")
            .Append("  --pb-word-spacing: ").Append(Num(i.WordSpacing)).Append("px;\n")
            .Append("  --pb-paragraph-spacing: ").Append(Num(i.ParagraphSpacing)).Append("px;\n")
            .Append("  --pb-page-margin: ").Append(Num(i.PageMargin)).Append("px;\n")
            .Append("  --pb-bg: ").Append(i.Background).Append(";\n")
            .Append("  --pb-fg: ").Append(i.Foreground).Append(";\n")
            .Append("}\n")
            .Append("html, body { background: var(--pb-bg) !important; }\n")
            .Append("#pb-content {\n");

        if (!i.UsePublisherStyles)
        {
            css.Append("  font-family: var(--pb-font-family) !important;\n")
                .Append("  font-size: var(--pb-font-size) !important;\n")
                .Append("  line-height: var(--pb-line-height) !important;\n")
                .Append("  letter-spacing: var(--pb-letter-spacing) !important;\n")
                .Append("  word-spacing: var(--pb-word-spacing) !important;\n");
        }

        css.Append("  color: var(--pb-fg) !important;\n")
            .Append("  padding: var(--pb-page-margin) !important;\n")
            .Append("}\n")
            .Append("#pb-content p, #pb-content h1, #pb-content h2, #pb-content h3 {\n")
            .Append("  color: var(--pb-fg) !important;\n");

        if (!i.UsePublisherStyles)
        {
            css.Append("  margin: 0 0 var(--pb-paragraph-spacing) 0 !important;\n");
        }

        css.Append('}');

        if (!i.UsePublisherStyles)
        {
            if (i.Justify)
            {
                css.Append("\n#pb-content p { text-align: justify !important; }");
            }

            if (i.Hyphenate)
            {
                css.Append("\n#pb-content { hyphens: auto !important; -webkit-hyphens: auto !important; overflow-wrap: break-word; }");
            }
        }

        // The paper layer is opt-in, so the output with every switch off stays what BookReaderCssTests pin.
        if (i.Paper)
        {
            css.Append('\n').Append(PaperCss);
        }

        // The in-page pills (jump-back, auto-scroll, read aloud) take the app's accent and UI font (books redesign §5.1).
        if (!string.IsNullOrEmpty(i.Accent))
        {
            css.Append("\n:root { --pb-accent: ").Append(i.Accent).Append("; }");
        }

        if (!string.IsNullOrEmpty(i.UiFont))
        {
            css.Append("\n:root { --pb-ui-font: ").Append(i.UiFont).Append("; }");
        }

        return css.ToString();
    }

    /// <summary>The page as a sheet of paper on a slightly darker desk (books redesign §5.1): <c>#pb-paper</c> holds the measured
    /// <c>#pb-viewport</c> (the pager clips that one, so the paper's shadow and page edges live on the wrapper), page-edge stacks either side
    /// (widths set by <c>pbSetEdges</c>, paged mode only) and the folio under it.</summary>
    public const string PaperCss = """
        :root { --pb-desk: color-mix(in srgb, var(--pb-bg) 90%, black); }
        html, body { background: var(--pb-desk) !important; }
        #pb-paper { position: relative; box-sizing: border-box; width: min(720px, calc(100vw - 48px)); height: calc(100vh - 48px);
          margin: 24px auto 0; background: var(--pb-bg); box-shadow: 0 10px 30px rgba(0, 0, 0, 0.28); }
        #pb-paper #pb-viewport { width: 100%; height: 100%; }
        #pb-edge-left, #pb-edge-right, #pb-folio, #pb-notes { display: block; }
        #pb-edge-left, #pb-edge-right { position: absolute; top: 3px; bottom: 3px; width: 0;
          background: repeating-linear-gradient(90deg, color-mix(in srgb, var(--pb-fg) 18%, var(--pb-bg)) 0 1px, var(--pb-bg) 1px 3px); }
        #pb-edge-left { right: 100%; }
        #pb-edge-right { left: 100%; }
        #pb-folio { position: fixed; left: 0; right: 0; bottom: 4px; text-align: center; font: 11px/16px var(--pb-ui-font, system-ui);
          color: var(--pb-fg); opacity: 0.45; pointer-events: none; }
        #pb-notes { position: fixed; top: 0; left: calc(50% + min(360px, 50vw - 24px) + 32px); right: 24px; pointer-events: none; }
        .pb-margin-note { position: absolute; left: 0; right: 0; padding: 4px 0 4px 8px; border-left: 2px solid var(--pb-note, var(--pb-accent, #888));
          font: 11px/15px var(--pb-ui-font, system-ui); color: var(--pb-fg); opacity: 0.75; pointer-events: auto; cursor: pointer; }
        .pb-margin-note b { font-weight: 600; }
        """;

    private static string? CleanFontName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var cleaned = new string(name.Where(c => c is not ('"' or '\\' or ';' or '{' or '}' or '<' or '>' or '\n' or '\r')).ToArray()).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }
}
