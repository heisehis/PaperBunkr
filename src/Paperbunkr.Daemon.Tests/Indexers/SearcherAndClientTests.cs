using System.Net;
using System.Net.Http;
using Paperbunkr.Daemon.Indexers;

namespace Paperbunkr.Daemon.Tests.Indexers;

public class ReleaseSearcherTests
{
    private sealed class FakeIndexer(Func<string, IReadOnlyList<IndexerRelease>> respond) : IIndexerClient
    {
        public List<string> Queries { get; } = new();

        public Task<IReadOnlyList<IndexerRelease>> SearchAsync(string queryText, CancellationToken cancellationToken)
        {
            Queries.Add(queryText);
            return Task.FromResult(respond(queryText));
        }

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken) => Task.FromResult(ConnectionTestResult.Ok("ok"));
    }

    private static IndexerRelease R(string title, string? guid = null, int seeders = 10) =>
        new() { Title = title, DownloadUrl = "magnet:" + (guid ?? title), Guid = guid ?? title, SizeBytes = 50 * 1024 * 1024, Seeders = seeders };

    [Fact]
    public async Task StrictPassRunsFirst_AndASanitizedQueryIsNeverSent_WhenTheStrictPassSucceeds()
    {
        var indexer = new FakeIndexer(q => q == "X-Men 5" ? new[] { R("X-Men 005 (2026)") } : Array.Empty<IndexerRelease>());
        var searcher = new ReleaseSearcher(indexer, new ScoringOptions());

        var found = await searcher.SearchAsync(new IndexerQuery("X-Men", "5"), CancellationToken.None);

        Assert.Single(found);
        Assert.Equal(new[] { "X-Men 5" }, indexer.Queries);           // stopped at the first accepted hit
        Assert.DoesNotContain(indexer.Queries, q => q.Contains("X Men"));
    }

    [Fact]
    public async Task FallsBackToTheSanitizedAlias_OnlyWhenTheStrictPassYieldsNothingAcceptable()
    {
        // Strict text finds only an unrelated release; the alias text finds the real one.
        var indexer = new FakeIndexer(q => q.StartsWith("Amazing Spider Man") ? new[] { R("Amazing Spider Man 005 (2026)") } : new[] { R("Totally Different 5") });
        var searcher = new ReleaseSearcher(indexer, new ScoringOptions());

        var found = await searcher.SearchAsync(new IndexerQuery("Amazing Spider-Man", "5"), CancellationToken.None);

        Assert.Single(found);
        Assert.Contains(indexer.Queries, q => q.StartsWith("Amazing Spider-Man "));   // strict tried first
        Assert.Contains(indexer.Queries, q => q.StartsWith("Amazing Spider Man "));   // then the alias
        Assert.True(indexer.Queries.FindIndex(q => q.StartsWith("Amazing Spider-Man ")) < indexer.Queries.FindIndex(q => q.StartsWith("Amazing Spider Man ")));
    }

    [Fact]
    public async Task TriesNumberVariants_UntilOneIsAccepted()
    {
        var indexer = new FakeIndexer(q => q == "Batman 005" ? new[] { R("Batman 005 (2026)") } : Array.Empty<IndexerRelease>());
        var searcher = new ReleaseSearcher(indexer, new ScoringOptions());

        var found = await searcher.SearchAsync(new IndexerQuery("Batman", "5"), CancellationToken.None);

        Assert.Single(found);
        Assert.Equal(new[] { "Batman 5", "Batman 05", "Batman 005" }, indexer.Queries);
    }

    [Fact]
    public async Task ReturnsNothing_WhenNoPassProducesAnAcceptedRelease()
    {
        var indexer = new FakeIndexer(_ => new[] { R("Unrelated Book 1") });
        var searcher = new ReleaseSearcher(indexer, new ScoringOptions());

        Assert.Empty(await searcher.SearchAsync(new IndexerQuery("Batman", "5"), CancellationToken.None));
    }

    [Fact]
    public async Task Results_AreDeduplicatedByGuid_SortedBestFirst()
    {
        var indexer = new FakeIndexer(_ => new[] { R("Batman 005 (2026)", "a", seeders: 5), R("Batman 005 (2026) dup", "a", seeders: 50), R("Batman 005 (2026) other", "b", seeders: 20) });
        var searcher = new ReleaseSearcher(indexer, new ScoringOptions());

        var found = await searcher.SearchAsync(new IndexerQuery("Batman", "5"), CancellationToken.None);

        Assert.Equal(2, found.Count);
        Assert.Equal(new[] { "a", "b" }, found.Select(f => f.Release.Guid));   // "a" kept at its best score (50 seeders)
        Assert.True(found[0].Score >= found[1].Score);
    }
}

public class ProwlarrSearchClientTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var (status, body) = respond(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static (ProwlarrSearchClient Client, FakeHandler Handler) Make(Func<HttpRequestMessage, (HttpStatusCode, string)> respond)
    {
        var handler = new FakeHandler(respond);
        return (new ProwlarrSearchClient("http://prowlarr.local:9696/", "SECRET", new HttpClient(handler)), handler);
    }

    private const string Results = """
        [
          {"guid":"g1","title":"Spawn 263 (2016) (digital)","size":52428800,"seeders":12,"leechers":3,"indexer":"IndexerA","publishDate":"2026-09-16T10:00:00Z","downloadUrl":"http://p/dl/1","magnetUrl":"magnet:?xt=urn:btih:AAA","protocol":"torrent"},
          {"guid":"g2","title":"Spawn 263 nzb","size":1,"seeders":null,"indexer":"IndexerB","downloadUrl":"http://p/dl/2","protocol":"usenet"},
          {"guid":"g3","title":"Spawn 263 torrent-file","size":40000000,"seeders":4,"indexer":"IndexerC","downloadUrl":"http://p/dl/3","magnetUrl":null,"protocol":"torrent"},
          {"guid":"g4","title":"","downloadUrl":"http://p/dl/4","protocol":"torrent"}
        ]
        """;

    [Fact]
    public async Task Search_SendsKeyInHeader_NotInTheUrl_AndEscapesTheQuery()
    {
        var (client, handler) = Make(_ => (HttpStatusCode.OK, "[]"));

        await client.SearchAsync("X-Men & co 5", CancellationToken.None);

        var request = handler.Requests[0];
        Assert.Equal("SECRET", request.Headers.GetValues("X-Api-Key").Single());
        var url = request.RequestUri!.OriginalString;
        Assert.DoesNotContain("SECRET", url);
        Assert.StartsWith("http://prowlarr.local:9696/api/v1/search?query=X-Men%20%26%20co%205", url);
        Assert.Contains("categories=7030", url);
    }

    [Fact]
    public async Task Search_KeepsOnlyTorrents_PrefersMagnet_AndSkipsUnusableRows()
    {
        var (client, _) = Make(_ => (HttpStatusCode.OK, Results));

        var found = await client.SearchAsync("Spawn 263", CancellationToken.None);

        Assert.Equal(new[] { "g1", "g3" }, found.Select(f => f.Guid));           // usenet and the blank-title row are dropped
        Assert.Equal("magnet:?xt=urn:btih:AAA", found[0].DownloadUrl);           // magnet preferred over the .torrent URL
        Assert.Equal("http://p/dl/3", found[1].DownloadUrl);                     // falls back to downloadUrl when there is no magnet
        Assert.Equal(12, found[0].Seeders);
        Assert.Equal(50L * 1024 * 1024, found[0].SizeBytes);
        Assert.Equal("IndexerA", found[0].Indexer);
    }

    [Fact]
    public async Task Search_MapsHttpFailures_ToIndexerException()
    {
        var (unauthorized, _) = Make(_ => (HttpStatusCode.Unauthorized, ""));
        var ex = await Assert.ThrowsAsync<IndexerException>(() => unauthorized.SearchAsync("x", CancellationToken.None));
        Assert.Contains("API key", ex.Message);

        var (broken, _) = Make(_ => (HttpStatusCode.InternalServerError, "boom"));
        await Assert.ThrowsAsync<IndexerException>(() => broken.SearchAsync("x", CancellationToken.None));

        var (garbage, _) = Make(_ => (HttpStatusCode.OK, "<html>not json</html>"));
        await Assert.ThrowsAsync<IndexerException>(() => garbage.SearchAsync("x", CancellationToken.None));

        var (notAList, _) = Make(_ => (HttpStatusCode.OK, """{"message":"hi"}"""));
        await Assert.ThrowsAsync<IndexerException>(() => notAList.SearchAsync("x", CancellationToken.None));
    }

    [Fact]
    public async Task TestConnection_ReportsVersion_BadKey_AndNonProwlarrAddresses()
    {
        var (ok, handler) = Make(_ => (HttpStatusCode.OK, """{"appName":"Prowlarr","version":"1.30.2.4939"}"""));
        var good = await ok.TestConnectionAsync(CancellationToken.None);
        Assert.True(good.Success);
        Assert.Contains("1.30.2.4939", good.Message);
        Assert.EndsWith("/api/v1/system/status", handler.Requests[0].RequestUri!.AbsolutePath);

        var (denied, _) = Make(_ => (HttpStatusCode.Unauthorized, ""));
        var bad = await denied.TestConnectionAsync(CancellationToken.None);
        Assert.False(bad.Success);
        Assert.Contains("API key", bad.Message);

        var (notProwlarr, _) = Make(_ => (HttpStatusCode.OK, "<html>router login</html>"));
        Assert.False((await notProwlarr.TestConnectionAsync(CancellationToken.None)).Success);
    }
}
