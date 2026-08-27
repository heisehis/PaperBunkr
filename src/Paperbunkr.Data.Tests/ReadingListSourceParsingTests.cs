using System.Text.Json.Nodes;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises each Tier-2 adapter's pure HTML/JSON-LD parsing (docs/superpowers/specs/2026-08-22-cbl-
/// manager-arc-lookup-design.md §6) against literal fixtures shaped like the real markup documented
/// in <c>_reference/CBLManager/docs/api-notes.md</c> - no live network.
/// </summary>
public class ReadingListSourceParsingTests
{
    // --- ReadThingsRightSource: range expansion, half-issues, year splitting ---

    [Fact]
    public void ReadThingsRight_ExpandsIssueRangeIntoOneArcIssuePerNumber()
    {
        string html = "<ol><li>Astro City (1995) #1-3</li></ol>";
        var issues = ReadThingsRightSource.ParseIssuesFromHtml(html);

        Assert.Equal(3, issues.Count);
        Assert.All(issues, i => Assert.Equal("Astro City", i.Series));
        Assert.All(issues, i => Assert.Equal(1995, i.Year));
        Assert.Equal(["1", "2", "3"], issues.Select(i => i.Number).ToArray());
    }

    [Fact]
    public void ReadThingsRight_KeepsHalfIssueSingular()
    {
        string html = "<ol><li>Wizard Presents Astro City (1996) #1/2</li></ol>";
        var issues = ReadThingsRightSource.ParseIssuesFromHtml(html);

        var issue = Assert.Single(issues);
        Assert.Equal("1/2", issue.Number);
        Assert.Equal("Wizard Presents Astro City", issue.Series);
        Assert.Equal(1996, issue.Year);
    }

    [Fact]
    public void ReadThingsRight_ExcludesAnnotationProseThatMentionsAnIssueNumberMidSentence()
    {
        string html = "<ul><li>Issue #13 picks up where the 1999 Annual leaves off, setting up the next arc.</li></ul>";
        var issues = ReadThingsRightSource.ParseIssuesFromHtml(html);

        Assert.Empty(issues); // trailing prose after the number fails the end-of-string anchor
    }

    // --- ComicBookReadingOrdersSource: dual markup styles ---

    [Fact]
    public void ComicBookReadingOrders_ParsesParagraphWrappedIssues()
    {
        string html = "<p>Sinestro Corps War #1</p><p><span style=\"color: #008000;\">Green Lantern #21</span></p>";
        var issues = ComicBookReadingOrdersSource.ParseIssuesFromHtml(html);

        Assert.Equal(2, issues.Count);
        Assert.Equal("Sinestro Corps War", issues[0].Series);
        Assert.Equal("1", issues[0].Number);
        Assert.Equal("Green Lantern", issues[1].Series);
    }

    [Fact]
    public void ComicBookReadingOrders_ParsesBareSpanRunsWithNoPerIssueWrapper()
    {
        string html = "<span style=\"color: #ff0000;\">Absolute Power #1 (2024)</span><br /><span style=\"color: #ff0000;\">Absolute Power #2 (2024)</span><br />";
        var issues = ComicBookReadingOrdersSource.ParseIssuesFromHtml(html);

        Assert.Equal(2, issues.Count);
        Assert.Equal("Absolute Power", issues[0].Series);
        Assert.Equal("1", issues[0].Number);
        Assert.Equal(2024, issues[0].Year);
    }

    [Fact]
    public void ComicBookReadingOrders_DropsBlueAnnotationSpansEntirely()
    {
        string html = "<span style=\"color: #0000ff;\">Takes place during Absolute Power #3</span><p>Real Series #1</p>";
        var issues = ComicBookReadingOrdersSource.ParseIssuesFromHtml(html);

        var issue = Assert.Single(issues);
        Assert.Equal("Real Series", issue.Series);
    }

    // --- ReadingOrdersNetSource: RSC-payload regex, no year extraction ---

    [Fact]
    public void ReadingOrdersNet_ExtractsSeriesAndNumberFromEscapedTitleField()
    {
        string html = """{"id":7434,\"date\":\"2019-02-06\",\"pages\":32,\"title\":\"Avengers #14\",\"event_id\":330}""";
        var issues = ReadingOrdersNetSource.ParseIssuesFromHtml(html);

        var issue = Assert.Single(issues);
        Assert.Equal("Avengers", issue.Series);
        Assert.Equal("14", issue.Number);
        Assert.Equal(0, issue.Year); // deliberately never extracted - see the source's own doc comment
    }

    // --- ComicArcSource: schema.org JSON-LD ItemList ---

    [Fact]
    public void ComicArc_ParsesItemListIssuesInOrder()
    {
        var itemList = JsonNode.Parse("""
            {
              "@type": "ItemList",
              "name": "Absolute Batman Reading Order",
              "numberOfItems": 2,
              "itemListElement": [
                {"@type": "ListItem", "position": 1, "name": "Absolute Batman 2025 Annual #1"},
                {"@type": "ListItem", "position": 2, "name": "Absolute Batman: Ark-M #1"}
              ]
            }
            """);

        var issues = ComicArcSource.ParseIssuesFromItemList(itemList);

        Assert.Equal(2, issues.Count);
        Assert.Equal("Absolute Batman 2025 Annual", issues[0].Series);
        Assert.Equal("1", issues[0].Number);
        Assert.Equal("Absolute Batman: Ark-M", issues[1].Series);
    }

    [Fact]
    public void ComicArc_ReturnsEmptyWhenItemListMissing()
    {
        var article = JsonNode.Parse("""{"@type": "Article", "description": "no item list here"}""");
        Assert.Empty(ComicArcSource.ParseIssuesFromItemList(article));
    }
}
