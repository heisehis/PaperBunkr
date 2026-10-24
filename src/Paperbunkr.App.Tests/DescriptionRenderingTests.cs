using System.Linq;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

/// <summary>Descriptions on the comic and manga detail screens: markers and provider HTML render instead of showing raw, and links are links.</summary>
public class DescriptionRenderingTests
{
    [Fact]
    public void BoldItalicAndUnderlineMarkersBecomeStyledRuns()
    {
        var runs = MarkdownLite.ParseDescription("A **bold** and *italic* and <u>under</u> word").Single().Runs;
        Assert.Contains(runs, r => r.Style == MdRunStyle.Bold && r.Text == "bold");
        Assert.Contains(runs, r => r.Style == MdRunStyle.Italic && r.Text == "italic");
        Assert.Contains(runs, r => r.Style == MdRunStyle.Underline && r.Text == "under");
        Assert.DoesNotContain(runs, r => r.Text.Contains('*') || r.Text.Contains('<'));
    }

    [Fact]
    public void ProviderHtmlAndBbCodeRender_AndOtherTagsAreDropped()
    {
        var blocks = MarkdownLite.ParseDescription("<p><b>Bold</b> [i]it[/i] [u]u[/u]<br>Second &amp; line <span class=\"x\">kept</span></p>");
        Assert.Equal(2, blocks.Count);
        Assert.Contains(blocks[0].Runs, r => r.Style == MdRunStyle.Bold && r.Text == "Bold");
        Assert.Contains(blocks[0].Runs, r => r.Style == MdRunStyle.Italic && r.Text == "it");
        Assert.Contains(blocks[0].Runs, r => r.Style == MdRunStyle.Underline && r.Text == "u");
        Assert.Equal("Second & line kept", blocks[1].PlainText);
    }

    [Fact]
    public void EachLineIsItsOwnParagraph()
    {
        Assert.Equal(3, MarkdownLite.ParseDescription("one\ntwo\r\n\nthree").Count);
    }

    [Fact]
    public void MarkdownLinksAndBareAddressesAreLinks_WithoutTrailingPunctuation()
    {
        var runs = MarkdownLite.ParseDescription("See [the wiki](https://example.org/w) or https://example.org/x.").Single().Runs;
        var links = runs.Where(r => r.Style == MdRunStyle.Link).ToList();
        Assert.Equal(2, links.Count);
        Assert.Equal("https://example.org/w", links[0].Target);
        Assert.Equal("the wiki", links[0].Text);
        Assert.Equal("https://example.org/x", links[1].Target);
    }

    [Fact]
    public void BlankDescriptionHasNoBlocks()
    {
        Assert.Empty(MarkdownLite.ParseDescription(null));
        Assert.Empty(MarkdownLite.ParseDescription("  \n "));
    }

    [Fact]
    public void MetronLookupResultSaysWhatHappened()
    {
        Assert.Contains("could not be reached", new MetronUniverseLookupResult(false, [], 0).Describe());
        Assert.Contains("could be matched", new MetronUniverseLookupResult(true, [], 0).Describe());
        Assert.Contains("no universe", new MetronUniverseLookupResult(true, [], 2).Describe());
        Assert.Equal("Added to \"Earth-616\".", new MetronUniverseLookupResult(true, ["Earth-616"], 1).Describe());
    }
}
