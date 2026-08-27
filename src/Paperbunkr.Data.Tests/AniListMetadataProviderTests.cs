using System.Net;
using System.Net.Http;
using System.Text;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="AniListMetadataProvider"/> (docs/superpowers/specs/2026-08-18-metadata-
/// model-phase5b-anilist-adapter-design.md) entirely against a fake <see cref="HttpMessageHandler"/>
/// - no real network calls, consistent with AniList's own terms against hammering the live API even
/// for CI runs. Rate limiting is exercised indirectly (a single call per test never trips the
/// min-interval gate); the header/Retry-After parsing itself doesn't need a live retry loop to verify.
/// </summary>
public class AniListMetadataProviderTests
{
    private const string SearchResponseJson = """
        {
          "data": {
            "Page": {
              "media": [
                { "id": 30013, "title": { "romaji": "One Piece", "english": "One Piece" }, "siteUrl": "https://anilist.co/manga/30013" },
                { "id": 74347, "title": { "romaji": "Kimetsu no Yaiba", "english": null }, "siteUrl": "https://anilist.co/manga/74347" }
              ]
            }
          }
        }
        """;

    private const string GetByIdResponseJson = """
        {
          "data": {
            "Media": {
              "id": 30013,
              "title": { "romaji": "One Piece", "english": "One Piece" },
              "siteUrl": "https://anilist.co/manga/30013",
              "description": "Gol D. Roger was known as the Pirate King...",
              "status": "RELEASING",
              "chapters": null,
              "volumes": null
            }
          }
        }
        """;

    private const string PartialNullResponseJson = """
        {
          "data": {
            "Media": {
              "id": 1,
              "title": { "romaji": "Some Manga", "english": null },
              "siteUrl": null,
              "description": null,
              "status": "FINISHED",
              "chapters": 5,
              "volumes": 1
            }
          }
        }
        """;

    private const string GraphQlErrorResponseJson = """
        {
          "data": null,
          "errors": [ { "message": "Not Found." } ]
        }
        """;

    [Fact]
    public async Task SearchAsync_ParsesResults_PreferringEnglishTitleOverRomaji()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, SearchResponseJson)));

        var results = await provider.SearchAsync("one piece", CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("30013", results[0].ExternalId);
        Assert.Equal("One Piece", results[0].Title);
        Assert.Equal("https://anilist.co/manga/30013", results[0].Url);
    }

    [Fact]
    public async Task SearchAsync_FallsBackToRomajiTitle_WhenEnglishTitleIsNull()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, SearchResponseJson)));

        var results = await provider.SearchAsync("kimetsu", CancellationToken.None);

        Assert.Equal("Kimetsu no Yaiba", results[1].Title);
    }

    [Fact]
    public async Task GetAsync_ParsesFullMediaMetadata()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, GetByIdResponseJson)));

        ExternalMediaMetadata? metadata = await provider.GetAsync("30013", CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Equal("30013", metadata!.ExternalId);
        Assert.Equal("One Piece", metadata.Title);
        Assert.Equal("https://anilist.co/manga/30013", metadata.Url);
        Assert.Equal("RELEASING", metadata.Status);
        Assert.StartsWith("Gol D. Roger", metadata.Description);
    }

    [Fact]
    public async Task GetAsync_HandlesPartiallyNullFields_WithoutThrowing()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, PartialNullResponseJson)));

        ExternalMediaMetadata? metadata = await provider.GetAsync("1", CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Equal("Some Manga", metadata!.Title);
        Assert.Null(metadata.Url);
        Assert.Null(metadata.Description);
        Assert.Equal(5, metadata.ChapterCount);
    }

    [Fact]
    public async Task GetAsync_NonIntegerExternalId_ReturnsNullWithoutCallingAniList()
    {
        bool called = false;
        var provider = CreateProvider(new StubHandler((_, _) =>
        {
            called = true;
            return JsonResponse(HttpStatusCode.OK, GetByIdResponseJson);
        }));

        ExternalMediaMetadata? metadata = await provider.GetAsync("not-a-number", CancellationToken.None);

        Assert.Null(metadata);
        Assert.False(called);
    }

    [Fact]
    public async Task GetAsync_GraphQlErrorsArray_ReturnsNull_EvenWithHttp200()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, GraphQlErrorResponseJson)));

        ExternalMediaMetadata? metadata = await provider.GetAsync("999999", CancellationToken.None);

        Assert.Null(metadata);
    }

    [Fact]
    public async Task SearchAsync_TooManyRequests_ThrowsProviderUnavailable()
    {
        var provider = CreateProvider(new StubHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.Add("Retry-After", "30");
            return response;
        }));

        await Assert.ThrowsAsync<MetadataProviderUnavailableException>(() => provider.SearchAsync("anything", CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_ServiceUnavailable_ThrowsProviderUnavailable()
    {
        // AniList's own documented shape for a severe outage: HTTP 403 with a GraphQL error body.
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.Forbidden, GraphQlErrorResponseJson)));

        await Assert.ThrowsAsync<MetadataProviderUnavailableException>(() => provider.SearchAsync("anything", CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_NetworkFailure_ThrowsProviderUnavailable()
    {
        var provider = CreateProvider(new StubHandler((_, _) => throw new HttpRequestException("connection refused")));

        await Assert.ThrowsAsync<MetadataProviderUnavailableException>(() => provider.SearchAsync("anything", CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_SuccessfulCallWithZeroMedia_ReturnsEmpty_DoesNotThrow()
    {
        // The genuine "no such series" case - a successful call whose Page.media is an empty array,
        // not a null/failed response - must stay distinguishable from the failure cases above.
        const string emptyResultsJson = """{ "data": { "Page": { "media": [] } } }""";
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, emptyResultsJson)));

        var results = await provider.SearchAsync("some nonexistent title", CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public void ProviderKey_IsAniList()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, SearchResponseJson)));

        Assert.Equal(ExternalMetadataProvider.AniList, provider.ProviderKey);
    }

    private static AniListMetadataProvider CreateProvider(HttpMessageHandler handler) =>
        new(new HttpClient(handler));

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
