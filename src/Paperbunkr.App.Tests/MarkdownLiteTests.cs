using System.IO;
using System.Linq;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="MarkdownLite"/> (docs/superpowers/specs/2026-09-26-about-polish-design.md §1), the one parser behind the
/// changelog and the legal viewer. Like the parser it replaced, it also parses the real bundled files: they are what users see.
/// </summary>
public class MarkdownLiteTests
{
    [Theory]
    [InlineData("# Privacy Notice", MdBlockKind.Heading1, "Privacy Notice")]
    [InlineData("## 1. Your data", MdBlockKind.Heading2, "1. Your data")]
    [InlineData("### Added", MdBlockKind.Heading3, "Added")]
    public void Parse_Headings_StripHashesAndTagKind(string line, MdBlockKind kind, string text)
    {
        var block = Assert.Single(MarkdownLite.Parse(line));

        Assert.Equal(kind, block.Kind);
        Assert.Equal(text, block.PlainText);
    }

    [Fact]
    public void Parse_HardWrappedParagraph_JoinsIntoOneBlock()
    {
        var block = Assert.Single(MarkdownLite.Parse("Paperbunkr is open-source software distributed under its project license. These\nTerms govern your use."));

        Assert.Equal(MdBlockKind.Paragraph, block.Kind);
        Assert.Equal("Paperbunkr is open-source software distributed under its project license. These Terms govern your use.", block.PlainText);
    }

    [Fact]
    public void Parse_HardWrappedBullet_JoinsContinuationLines()
    {
        var blocks = MarkdownLite.Parse("- **Comic acquisition.** A want-list built on\n  Prowlarr and qBittorrent.\n- Second item.");

        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(MdBlockKind.Bullet, b.Kind));
        Assert.Equal("Comic acquisition. A want-list built on Prowlarr and qBittorrent.", blocks[0].PlainText);
        Assert.Equal(MdRunStyle.Bold, blocks[0].Runs[0].Style);
    }

    [Fact]
    public void Parse_MultiLineQuote_IsOneBlock()
    {
        var block = Assert.Single(MarkdownLite.Parse("> First line of the quote\n> and its second line."));

        Assert.Equal(MdBlockKind.Quote, block.Kind);
        Assert.Equal("First line of the quote and its second line.", block.PlainText);
    }

    [Fact]
    public void Parse_HeadingEndsTheParagraphAboveIt()
    {
        var blocks = MarkdownLite.Parse("Some text\n## Next");

        Assert.Equal(new[] { MdBlockKind.Paragraph, MdBlockKind.Heading2 }, blocks.Select(b => b.Kind));
    }

    [Fact]
    public void Parse_BlankLines_SeparateParagraphs()
    {
        Assert.Equal(2, MarkdownLite.Parse("First.\n\n\nSecond.").Count);
    }

    [Fact]
    public void Parse_FencedBlock_IsPreformattedAndVerbatim()
    {
        var blocks = MarkdownLite.Parse("Intro\n\n```\nPaperbunkr 0.7.3-beta\n  indented **not bold**\n```\nAfter");

        Assert.Equal(new[] { MdBlockKind.Paragraph, MdBlockKind.Preformatted, MdBlockKind.Paragraph }, blocks.Select(b => b.Kind));
        Assert.Equal("Paperbunkr 0.7.3-beta\n  indented **not bold**", blocks[1].PlainText);
    }

    [Fact]
    public void Parse_NumberedItem_KeepsItsNumber()
    {
        var blocks = MarkdownLite.Parse("1. First\n2. Second");

        Assert.Equal(new[] { "1. First", "2. Second" }, blocks.Select(b => b.PlainText));
    }

    [Fact]
    public void Parse_HorizontalRule_IsDropped()
    {
        Assert.Equal(2, MarkdownLite.Parse("Above\n\n---\n\nBelow").Count);
    }

    [Fact]
    public void Preformatted_KeepsTheWholeTextAsOneBlock()
    {
        var block = Assert.Single(MarkdownLite.Preformatted("  GNU AFFERO GENERAL PUBLIC LICENSE\r\n     Version 3\r\n"));

        Assert.Equal(MdBlockKind.Preformatted, block.Kind);
        Assert.Equal("  GNU AFFERO GENERAL PUBLIC LICENSE\n     Version 3", block.PlainText);
    }

    [Fact]
    public void ParseInline_BoldItalicCodeAndLink()
    {
        var runs = MarkdownLite.ParseInline("A **bold** and *italic* with `code` and [the notice](COMICVINE_NOTICE.md).");

        Assert.Equal(
            new[]
            {
                ("A ", MdRunStyle.Plain), ("bold", MdRunStyle.Bold), (" and ", MdRunStyle.Plain), ("italic", MdRunStyle.Italic),
                (" with ", MdRunStyle.Plain), ("code", MdRunStyle.Code), (" and ", MdRunStyle.Plain), ("the notice", MdRunStyle.Link),
                (".", MdRunStyle.Plain),
            },
            runs.Select(r => (r.Text, r.Style)));
        Assert.Equal("COMICVINE_NOTICE.md", runs[7].Target);
    }

    [Fact]
    public void ParseInline_AutolinkAndUnderscoreItalic()
    {
        var runs = MarkdownLite.ParseInline("See <https://metron.cloud> or _this_.");

        Assert.Equal(MdRunStyle.Link, runs[1].Style);
        Assert.Equal("https://metron.cloud", runs[1].Text);
        Assert.Equal("https://metron.cloud", runs[1].Target);
        Assert.Equal(MdRunStyle.Italic, runs[3].Style);
    }

    [Fact]
    public void ParseInline_CodeInsideLinkText_IsStripped()
    {
        var run = Assert.Single(MarkdownLite.ParseInline("[`LICENSE`](LICENSE)"));

        Assert.Equal("LICENSE", run.Text);
        Assert.Equal(MdRunStyle.Link, run.Style);
    }

    [Theory]
    [InlineData("snake_case_name stays literal")]
    [InlineData("2*3*4 stays literal")]
    [InlineData("an unmatched **marker stays")]
    [InlineData("a lone ` backtick")]
    public void ParseInline_NonMarkup_StaysPlain(string text)
    {
        var run = Assert.Single(MarkdownLite.ParseInline(text));

        Assert.Equal(MdRunStyle.Plain, run.Style);
        Assert.Equal(text, run.Text);
    }

    [Theory]
    [InlineData("PRIVACY.md")]
    [InlineData("TERMS.md")]
    [InlineData("COMICVINE_NOTICE.md")]
    [InlineData("THIRD-PARTY-NOTICES.md")]
    [InlineData("CHANGELOG.md")]
    public void Parse_RealBundledDocument_HasNoLeftoverMarkup(string fileName)
    {
        string path = Path.Combine(FindRepoRoot(), fileName);
        Assert.True(File.Exists(path), $"Expected {path} to exist.");

        var blocks = MarkdownLite.Parse(File.ReadAllText(path));

        Assert.NotEmpty(blocks);
        foreach (var run in blocks.Where(b => b.Kind != MdBlockKind.Preformatted).SelectMany(b => b.Runs).Where(r => r.Style != MdRunStyle.Code))
        {
            Assert.DoesNotContain("**", run.Text);
            Assert.DoesNotContain("](", run.Text);
        }
    }

    internal static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !(File.Exists(Path.Combine(dir.FullName, "CHANGELOG.md")) && Directory.Exists(Path.Combine(dir.FullName, "src"))))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
