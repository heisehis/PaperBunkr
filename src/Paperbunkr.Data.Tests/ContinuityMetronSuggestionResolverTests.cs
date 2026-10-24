using System.Net;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

public sealed class ContinuityMetronSuggestionResolverTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_metron_suggestions_{Guid.NewGuid():N}.db");
    private DbContextOptions<PaperbunkrDbContext> Options => new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;

    public ContinuityMetronSuggestionResolverTests()
    {
        using var context = new PaperbunkrDbContext(Options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static MetronClient FakeMetron() => new("test", "test", ComicVineRequestPriority.High,
        new HttpClient(new StubHandler()));

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":900,"series":{"id":10,"name":"Metron Series","year_began":2001},"number":"1","universes":[{"id":21,"name":"Earth-616"}],"arcs":[],"characters":[],"teams":[],"credits":[]}""")
            });
    }

    [Fact]
    public async Task Scan_UsesStoredMetronIssueIdentity_AndDoesNotCreateMemberships()
    {
        using var context = new PaperbunkrDbContext(Options);
        var matched = new Series { Name = "Local Name" };
        matched.Issues.Add(new Issue { Number = "1" });
        var titleOnly = new Series { Name = "Earth-616 Adventures" };
        titleOnly.Issues.Add(new Issue { Number = "1" });
        context.Series.AddRange(matched, titleOnly);
        context.SaveChanges();
        context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId
        {
            EntityKind = ComicMetadataEntityKind.Issue,
            EntityId = matched.Issues.Single().Id,
            Provider = ComicProvider.Metron,
            ExternalId = "900",
        });
        context.SaveChanges();

        var results = await ContinuityMetronSuggestionResolver.GetSuggestionsAsync(context, FakeMetron(), CancellationToken.None);

        var suggestion = Assert.Single(results);
        Assert.Equal(matched.Id, suggestion.Series.Id);
        Assert.Equal(21, suggestion.MetronUniverseId);
        Assert.Null(suggestion.WikidataQid);
        Assert.Null(suggestion.FandomKey);
        Assert.Empty(context.Continuities);
        Assert.Empty(context.ContinuityMemberships);
    }

    [Fact]
    public async Task Dismissal_IsNamespacedToMetronUniverseAndSuppressesThatSuggestion()
    {
        using var context = new PaperbunkrDbContext(Options);
        var series = new Series { Name = "Local Name" };
        series.Issues.Add(new Issue { Number = "1" });
        context.Series.Add(series);
        context.SaveChanges();
        context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId
        {
            EntityKind = ComicMetadataEntityKind.Issue,
            EntityId = series.Issues.Single().Id,
            Provider = ComicProvider.Metron,
            ExternalId = "900",
        });
        context.SaveChanges();
        ContinuityMetronSuggestionResolver.Dismiss(context, series.Id, 21);

        var results = await ContinuityMetronSuggestionResolver.GetSuggestionsAsync(context, FakeMetron(), CancellationToken.None);

        Assert.Empty(results);
        var dismissal = Assert.Single(context.ContinuityMetronSuggestionDismissals);
        Assert.Equal(21, dismissal.MetronUniverseId);
    }

    [Fact]
    public async Task Scan_UsesStoredComicVineSeriesIdentity_WhenIssueIdentitiesAreMissing()
    {
        using var context = new PaperbunkrDbContext(Options);
        var series = new Series { Name = "Local Series" };
        series.Issues.Add(new Issue { Number = "1" });
        context.Series.Add(series);
        context.SaveChanges();
        context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId
        {
            EntityKind = ComicMetadataEntityKind.Series,
            EntityId = series.Id,
            Provider = ComicProvider.ComicVine,
            ExternalId = "12345",
        });
        context.SaveChanges();

        using var http = new HttpClient(new SeriesIdentityHandler());
        var metron = new MetronClient("test", "test", ComicVineRequestPriority.High, http);
        var result = await ContinuityMetronSeriesLookup.FindUniversesAsync(context, series.Id, metron, CancellationToken.None);

        var universe = Assert.Single(result.Universes);
        Assert.Equal(new MetronUniverseInfo(21, "Earth-616", null, null), universe);
        Assert.Equal(1, result.IssuesChecked);
    }

    private sealed class SeriesIdentityHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.PathAndQuery;
            string body = path switch
            {
                "/api/series/?cv_id=12345" => """{"results":[{"id":10,"series":"Metron Series","year_began":2001}],"next":null}""",
                "/api/series/10/issue_list/" => """{"results":[{"id":900,"number":"1"}],"next":null}""",
                "/api/issue/900/" => """{"id":900,"series":{"id":10,"name":"Metron Series","year_began":2001},"number":"1","universes":[{"id":21,"name":"Earth-616"}],"arcs":[],"characters":[],"teams":[],"credits":[]}""",
                _ => throw new InvalidOperationException($"Unexpected Metron request: {path}"),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
