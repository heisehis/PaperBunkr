using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Tests;

/// <summary>What ComicVine and Metron can tell role detection. Neither API has a role field (ComicVine's documented issue and story-arc fields
/// contain none); what they do give is each issue's story title and a one-line summary, which matter most for issues the library does not own
/// yet - those have no title of their own.</summary>
public sealed class ProviderRoleSignalTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_provider_roles_{Guid.NewGuid():N}.db");
    private readonly PaperbunkrDbContext _context;

    public ProviderRoleSignalTests()
    {
        _context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    // -- parsing --

    [Fact]
    public void ComicVine_KeepsTheIssueNameAndDeck_AsTitleAndSummary()
    {
        var node = JsonNode.Parse("""
            {"id":1,"issue_number":"4","volume":{"name":"Amazing Spider-Man"},"cover_date":"2020-05-01","store_date":"2020-04-08",
             "image":{"small_url":"http://x/y.jpg"},"name":"Aftermath, Part 1","deck":"  The fallout begins.  "}
            """)!;

        var issue = ComicVineSource.ParseArcIssue(node);

        Assert.Equal("Amazing Spider-Man", issue.Series);
        Assert.Equal("4", issue.Number);
        Assert.Equal(2020, issue.Year);
        Assert.Equal("Aftermath, Part 1", issue.Title);
        Assert.Equal("The fallout begins.", issue.Summary);
    }

    [Fact]
    public void ComicVine_TreatsABlankNameOrDeckAsNone()
    {
        var node = JsonNode.Parse("""{"id":1,"issue_number":"4","volume":{"name":"X"},"name":"  ","deck":null}""")!;

        var issue = ComicVineSource.ParseArcIssue(node);

        Assert.Null(issue.Title);
        Assert.Null(issue.Summary);
    }

    [Theory]
    [InlineData("""{"series":{"name":"Thor"},"number":"2","cover_date":"2019-01-01","name":["Prologue","Part Two"]}""", "Prologue; Part Two")]
    [InlineData("""{"series":{"name":"Thor"},"number":"2","cover_date":"2019-01-01","name":"Epilogue"}""", "Epilogue")]
    [InlineData("""{"series":{"name":"Thor"},"number":"2","cover_date":"2019-01-01"}""", null)]
    public void Metron_ReadsAStoryTitleWhenTheRowHasOne_AndNoneWhenItDoesNot(string json, string? expectedTitle)
    {
        var issue = MetronSource.ParseArcIssue(JsonNode.Parse(json)!);

        Assert.Equal("Thor", issue.Series);
        Assert.Equal(2019, issue.Year);
        Assert.Equal(expectedTitle, issue.Title);
    }

    // -- detector --

    private static MemberRoleFacts Facts(string? summary = null, string? title = null, string? series = "Amazing Spider-Man") =>
        new(null, title, series, "5", null, "Amazing Spider-Man", 2, 10, summary);

    [Fact]
    public void ASummaryThatMentionsARoleWord_IsOnlyEverAWeakSuggestion()
    {
        var suggestion = MemberRoleDetector.Detect(Facts(summary: "Peter deals with the aftermath of the attack."));

        Assert.Equal(EventMembershipRole.Aftermath, suggestion!.Role);
        Assert.Equal(RoleConfidence.Low, suggestion.Confidence);
        Assert.Contains("Summary", suggestion.Reason);
    }

    [Fact]
    public void ATitleBeatsASummary_AndASummaryBeatsTheSeriesName()
    {
        Assert.Equal(EventMembershipRole.Epilogue, MemberRoleDetector.Detect(Facts(summary: "the aftermath", title: "Epilogue"))!.Role);
        var summaryOverSeries = MemberRoleDetector.Detect(Facts(summary: "a tie-in tale", series: "Prelude"));
        Assert.Equal(EventMembershipRole.TieIn, summaryOverSeries!.Role);
    }

    // -- end to end: an issue the library does not own has no title of its own --

    [Fact]
    public async Task ATitleFromTheProvider_LetsRoleDetectionWorkOnAnIssueTheLibraryDoesNotOwn()
    {
        var source = new FakeReadingListSource("Fake", new[]
        {
            new ArcIssue("Amazing Spider-Man", "1", 2020, null),
            new ArcIssue("Amazing Spider-Man", "2", 2020, null, Title: "Aftermath, Part 1"),
            new ArcIssue("Amazing Spider-Man", "3", 2020, null, Summary: "Deals with the epilogue of the war."),
        }, null);

        var list = await ArcReadingListBuilder.CreateFromArcAsync(_context, source, new ArcSearchResult("a", "Arc", null, null, 3), CancellationToken.None);

        var items = list.Items.OrderBy(i => i.SortOrder).ToList();
        Assert.Null(items[0].Role);
        Assert.Equal(EventMembershipRole.Aftermath, items[1].Role);                    // a placeholder: only the provider knew its title
        Assert.Equal(RoleAssignmentSource.Auto, items[1].RoleSource);
        Assert.Null(items[2].Role);                                                      // a summary is only a suggestion
        Assert.Equal(EventMembershipRole.Epilogue, items[2].SuggestedRole);
    }

    [Fact]
    public async Task TheLibrarysOwnTitle_WinsOverTheProvidersOne()
    {
        var series = _context.Series.Add(new Series { Name = "Amazing Spider-Man", SortName = "Amazing Spider-Man" }).Entity;
        _context.SaveChanges();
        _context.Issues.Add(new Issue { SeriesId = series.Id, Number = "2", Year = 2020, Title = "Just A Story" });
        _context.SaveChanges();
        var source = new FakeReadingListSource("Fake", new[]
        {
            new ArcIssue("Amazing Spider-Man", "2", 2020, null, Title: "Aftermath, Part 1"),
        }, null);

        var list = await ArcReadingListBuilder.CreateFromArcAsync(_context, source, new ArcSearchResult("a", "Arc", null, null, 1), CancellationToken.None);

        Assert.Null(Assert.Single(list.Items).Role);
    }
}
