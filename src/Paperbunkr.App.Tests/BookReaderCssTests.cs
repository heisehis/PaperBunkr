using Paperbunkr.App.Services.Reader;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

public class BookReaderCssTests
{
    private static BookReaderCssInput Default(bool justify = false, bool hyphenate = false, bool publisher = false) =>
        new("Georgia, Cambria, serif", 17, 1.6, 0, 0, 10, 40, "#FFFFFF", "#000000", justify, hyphenate, publisher);

    /// <summary>The output before the extraction, character for character - existing readers must not change.</summary>
    private const string PreExtractionDefault = """
        :root {
          --pb-font-family: Georgia, Cambria, serif;
          --pb-font-size: 17px;
          --pb-line-height: 1.6;
          --pb-letter-spacing: 0px;
          --pb-word-spacing: 0px;
          --pb-paragraph-spacing: 10px;
          --pb-page-margin: 40px;
          --pb-bg: #FFFFFF;
          --pb-fg: #000000;
        }
        html, body { background: var(--pb-bg) !important; }
        #pb-content {
          font-family: var(--pb-font-family) !important;
          font-size: var(--pb-font-size) !important;
          line-height: var(--pb-line-height) !important;
          letter-spacing: var(--pb-letter-spacing) !important;
          word-spacing: var(--pb-word-spacing) !important;
          color: var(--pb-fg) !important;
          padding: var(--pb-page-margin) !important;
        }
        #pb-content p, #pb-content h1, #pb-content h2, #pb-content h3 {
          color: var(--pb-fg) !important;
          margin: 0 0 var(--pb-paragraph-spacing) 0 !important;
        }
        """;

    [Fact]
    public void DefaultSettings_MatchTheOutputBeforeTheExtraction() =>
        Assert.Equal(PreExtractionDefault.ReplaceLineEndings("\n"), BookReaderCss.Build(Default()));

    [Fact]
    public void Justify_AddsTheParagraphRule()
    {
        var css = BookReaderCss.Build(Default(justify: true));
        Assert.Contains("text-align: justify", css);
        Assert.DoesNotContain("hyphens", css);
    }

    [Fact]
    public void Hyphenate_AddsTheHyphensRule()
    {
        var css = BookReaderCss.Build(Default(hyphenate: true));
        Assert.Contains("hyphens: auto", css);
        Assert.DoesNotContain("text-align", css);
    }

    [Fact]
    public void PublisherStyles_DropsTheForcedTypographyButKeepsColoursAndMargin()
    {
        var css = BookReaderCss.Build(Default(justify: true, hyphenate: true, publisher: true));

        Assert.DoesNotContain("font-family: var", css);
        Assert.DoesNotContain("font-size: var", css);
        Assert.DoesNotContain("line-height: var", css);
        Assert.DoesNotContain("letter-spacing: var", css);
        Assert.DoesNotContain("margin: 0 0", css);
        Assert.DoesNotContain("justify", css);
        Assert.DoesNotContain("hyphens", css);
        Assert.Contains("color: var(--pb-fg) !important", css);
        Assert.Contains("padding: var(--pb-page-margin) !important", css);
        Assert.Contains("--pb-bg: #FFFFFF", css);
    }

    [Fact]
    public void FontStack_CustomFontNamesTheFontThenFallsBackToSerif() =>
        Assert.Equal("\"Bookerly\", Georgia, Cambria, serif", BookReaderCss.FontStack(BookFontFamilyOption.Custom, "Bookerly"));

    [Fact]
    public void FontStack_CustomWithoutAName_IsSerif() =>
        Assert.Equal("Georgia, Cambria, serif", BookReaderCss.FontStack(BookFontFamilyOption.Custom, "  "));

    [Fact]
    public void FontStack_StripsCharactersThatCouldBreakOutOfTheRule() =>
        Assert.Equal("\"Evil  body\", Georgia, Cambria, serif", BookReaderCss.FontStack(BookFontFamilyOption.Custom, "Evil\"; }<> body"));

    [Fact]
    public void FontStack_NamedOptionsAreUnchanged()
    {
        Assert.Equal("'Segoe UI', Arial, sans-serif", BookReaderCss.FontStack(BookFontFamilyOption.Sans, "x"));
        Assert.Equal("Consolas, monospace", BookReaderCss.FontStack(BookFontFamilyOption.Mono, null));
        Assert.Equal("OpenDyslexic, Georgia, Cambria, serif", BookReaderCss.FontStack(BookFontFamilyOption.OpenDyslexic, null));
    }

    [Theory]
    [InlineData("fr", "fr")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData(null, "en")]
    [InlineData("", "en")]
    [InlineData("x\" onload=\"", "en")]
    public void LanguageAttribute_FallsBackToEnglish(string? iso, string expected) =>
        Assert.Equal(expected, BookReaderCss.LanguageAttribute(iso));
}
