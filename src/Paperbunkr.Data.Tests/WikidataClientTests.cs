using System.Net;
using System.Net.Http;
using System.Text;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="WikidataClient"/> (docs/superpowers/specs/2026-09-17-storyevent-continuity-
/// autopopulate-design.md, Phase 2) entirely against a fake <see cref="HttpMessageHandler"/> - no
/// real network calls, same posture as <see cref="AniListMetadataProviderTests"/>.
/// </summary>
public class WikidataClientTests
{
    private const string SearchResponseJson = """
        {
          "search": [
            { "id": "Q79037", "label": "Spider-Man" },
            { "id": "Q42", "label": "Douglas Adams" }
          ]
        }
        """;

    // Real shape: Spider-Man, P31 -> comics character (Q1114461), P1080 -> Earth-616 (Q2246088).
    private const string SpiderManEntityJson = """
        {
          "entities": {
            "Q79037": {
              "labels": { "en": { "value": "Spider-Man" } },
              "claims": {
                "P31": [ { "mainsnak": { "datavalue": { "value": { "id": "Q1114461" } } } } ],
                "P1080": [ { "mainsnak": { "datavalue": { "value": { "id": "Q2246088" } } } } ]
              }
            }
          }
        }
        """;

    [Fact]
    public async Task SearchEntitiesAsync_ParsesIdAndLabel()
    {
        var client = CreateClient(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, SearchResponseJson)));

        var results = await client.SearchEntitiesAsync("Spider-Man", CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("Q79037", results[0].Qid);
        Assert.Equal("Spider-Man", results[0].Label);
    }

    [Fact]
    public async Task GetEntityAsync_ParsesLabelInstanceOfAndUniverseProperties()
    {
        var client = CreateClient(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, SpiderManEntityJson)));

        var entity = await client.GetEntityAsync("Q79037", CancellationToken.None);

        Assert.NotNull(entity);
        Assert.Equal("Spider-Man", entity!.Label);
        Assert.Contains("Q1114461", entity.InstanceOfQids);
        Assert.Equal("Q2246088", entity.FromNarrativeUniverseQid);
        Assert.Null(entity.TakesPlaceInFictionalUniverseQid);
    }

    [Fact]
    public async Task GetEntityAsync_UnknownEntity_ReturnsNull()
    {
        var client = CreateClient(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, """{ "entities": {} }""")));

        Assert.Null(await client.GetEntityAsync("Q0", CancellationToken.None));
    }

    [Fact]
    public async Task TooManyRequests_WaitsForRetryAfterThenSucceeds()
    {
        int callCount = 0;
        var client = CreateClient(new StubHandler((_, _) =>
        {
            callCount++;
            if (callCount == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return response;
            }

            return JsonResponse(HttpStatusCode.OK, SearchResponseJson);
        }));

        var results = await client.SearchEntitiesAsync("Spider-Man", CancellationToken.None);

        Assert.Equal(2, callCount);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task NetworkFailure_ReturnsEmptyNotThrow()
    {
        var client = CreateClient(new StubHandler((_, _) => throw new HttpRequestException("connection refused")));

        var results = await client.SearchEntitiesAsync("Spider-Man", CancellationToken.None);

        Assert.Empty(results);
    }

    private static WikidataClient CreateClient(HttpMessageHandler handler) => new(new HttpClient(handler));

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request, cancellationToken));
    }
}
