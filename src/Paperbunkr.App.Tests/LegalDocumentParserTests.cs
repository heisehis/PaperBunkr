using System.IO;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="LegalDocumentParser"/> (docs/superpowers/specs/2026-09-07-about-redesign-
/// design.md). Unlike <see cref="ChangelogParserTests"/>, this deliberately also parses the real
/// bundled files (LICENSE, PRIVACY.md, TERMS.md, COMICVINE_NOTICE.md) - they change far less often
/// than CHANGELOG.md, and catching a parser gap against real content matters more here than fixture
/// independence.
/// </summary>
public class LegalDocumentParserTests
{
    [Fact]
    public void Parse_Heading1_StripsHashAndTagsKind()
    {
        var blocks = LegalDocumentParser.Parse("# Privacy Notice");

        Assert.Single(blocks);
        Assert.Equal(LegalBlockKind.Heading1, blocks[0].Kind);
        Assert.Equal("Privacy Notice", blocks[0].Runs[0].Text);
    }

    [Fact]
    public void Parse_Heading2_StripsHashesAndTagsKind()
    {
        var blocks = LegalDocumentParser.Parse("## 1. Data stays on your infrastructure");

        Assert.Single(blocks);
        Assert.Equal(LegalBlockKind.Heading2, blocks[0].Kind);
        Assert.Equal("1. Data stays on your infrastructure", blocks[0].Runs[0].Text);
    }

    [Fact]
    public void Parse_Blockquote_StripsMarkerAndTagsKind()
    {
        var blocks = LegalDocumentParser.Parse("> Template, not legal advice.");

        Assert.Single(blocks);
        Assert.Equal(LegalBlockKind.Quote, blocks[0].Kind);
        Assert.Equal("Template, not legal advice.", blocks[0].Runs[0].Text);
    }

    [Fact]
    public void Parse_Bullet_StripsMarkerAndTagsKind()
    {
        var blocks = LegalDocumentParser.Parse("- Securing your deployment.");

        Assert.Single(blocks);
        Assert.Equal(LegalBlockKind.Bullet, blocks[0].Kind);
        Assert.Equal("Securing your deployment.", blocks[0].Runs[0].Text);
    }

    [Fact]
    public void Parse_PlainLine_IsParagraph()
    {
        var blocks = LegalDocumentParser.Parse("By downloading, installing, or running Paperbunkr, you agree.");

        Assert.Single(blocks);
        Assert.Equal(LegalBlockKind.Paragraph, blocks[0].Kind);
    }

    [Fact]
    public void Parse_MixedBoldAndPlainText_ProducesMultipleRuns()
    {
        var blocks = LegalDocumentParser.Parse("Do not **share** your key with others.");

        var runs = blocks[0].Runs;
        Assert.Equal(3, runs.Count);
        Assert.False(runs[0].Bold);
        Assert.Equal("Do not ", runs[0].Text);
        Assert.True(runs[1].Bold);
        Assert.Equal("share", runs[1].Text);
        Assert.False(runs[2].Bold);
        Assert.Equal(" your key with others.", runs[2].Text);
    }

    [Fact]
    public void Parse_InlineCode_TagsRunAsCode()
    {
        var blocks = LegalDocumentParser.Parse("See `LICENSE` for the full text.");

        var runs = blocks[0].Runs;
        Assert.Equal(3, runs.Count);
        Assert.True(runs[1].Code);
        Assert.False(runs[1].Bold);
        Assert.Equal("LICENSE", runs[1].Text);
    }

    [Fact]
    public void Parse_BlankLines_ProduceNoBlocks()
    {
        var blocks = LegalDocumentParser.Parse("First line.\n\n\nSecond line.");

        Assert.Equal(2, blocks.Count);
    }

    [Theory]
    [InlineData("LICENSE")]
    [InlineData("PRIVACY.md")]
    [InlineData("TERMS.md")]
    [InlineData("COMICVINE_NOTICE.md")]
    public void Parse_RealBundledDocument_ProducesNonEmptyBlockList(string fileName)
    {
        string repoRoot = FindRepoRoot();
        string path = Path.Combine(repoRoot, fileName);
        Assert.True(File.Exists(path), $"Expected {path} to exist for this smoke test.");

        var blocks = LegalDocumentParser.Parse(File.ReadAllText(path));

        Assert.NotEmpty(blocks);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LICENSE")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
