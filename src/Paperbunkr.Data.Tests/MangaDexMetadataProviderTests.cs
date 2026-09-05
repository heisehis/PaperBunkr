using System.Net;
using System.Net.Http;
using System.Text;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="MangaDexMetadataProvider"/> (docs/superpowers/specs/2026-08-19-metadata-
/// model-second-provider-mangadex-design.md) entirely against a fake <see cref="HttpMessageHandler"/>
/// - no real network calls, same rationale as <see cref="AniListMetadataProviderTests"/>.
/// </summary>
public class MangaDexMetadataProviderTests
{
    private const string SearchResponseJson = """
        {
          "data": [
            {
              "id": "a1c7c817-4e59-43b7-9365-09675a149a6f",
              "attributes": {
                "title": { "en": "One Piece" },
                "altTitles": [ { "ja": "ワンピース" } ],
                "status": "ongoing",
                "tags": []
              }
            },
            {
              "id": "801513ba-a712-498c-8f57-cae55b38cc92",
              "attributes": {
                "title": { "ja-ro": "Kimetsu no Yaiba" },
                "altTitles": [],
                "status": "completed",
                "tags": []
              }
            }
          ]
        }
        """;

    private const string GetByIdResponseJson = """
        {
          "data": {
            "id": "a1c7c817-4e59-43b7-9365-09675a149a6f",
            "attributes": {
              "title": { "en": "One Piece", "ja": "ワンピース" },
              "altTitles": [ { "ja-ro": "Wan Pisu" } ],
              "description": { "en": "Gol D. Roger was known as the Pirate King...", "fr": "Une description en francais" },
              "status": "ongoing",
              "tags": [
                { "attributes": { "name": { "en": "Action" }, "group": "genre" } },
                { "attributes": { "name": { "en": "School Life" }, "group": "theme" } },
                { "attributes": { "name": { "en": "Adventure" }, "group": "genre" } }
              ]
            }
          }
        }
        """;

    private const string PartialNullResponseJson = """
        {
          "data": {
            "id": "1",
            "attributes": {
              "title": {},
              "altTitles": [ { "en": "Some Manga" } ],
              "status": "hiatus",
              "tags": []
            }
          }
        }
        """;

    [Fact]
    public async Task SearchAsync_ParsesResults_PreferringEnglishTitle()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, SearchResponseJson)));

        var results = await provider.SearchAsync("one piece", CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("a1c7c817-4e59-43b7-9365-09675a149a6f", results[0].ExternalId);
        Assert.Equal("One Piece", results[0].Title);
        Assert.Equal("https://mangadex.org/title/a1c7c817-4e59-43b7-9365-09675a149a6f", results[0].Url);
    }

    [Fact]
    public async Task SearchAsync_FallsBackToRomajiTitle_WhenNoEnglishTitle()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, SearchResponseJson)));

        var results = await provider.SearchAsync("kimetsu", CancellationToken.None);

        Assert.Equal("Kimetsu no Yaiba", results[1].Title);
    }

    [Fact]
    public async Task GetAsync_ParsesFullMediaMetadata_IncludingLocalizedTitlesAndGenreOnlyTags()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, GetByIdResponseJson)));

        ExternalMediaMetadata? metadata = await provider.GetAsync("a1c7c817-4e59-43b7-9365-09675a149a6f", CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Equal("One Piece", metadata!.Title);
        Assert.Equal("https://mangadex.org/title/a1c7c817-4e59-43b7-9365-09675a149a6f", metadata.Url);
        Assert.Equal("ongoing", metadata.Status);
        Assert.StartsWith("Gol D. Roger", metadata.Description);
        Assert.Equal("One Piece", metadata.TitleEnglish);
        Assert.Equal("ワンピース", metadata.TitleNative);
        Assert.Equal("Wan Pisu", metadata.TitleRomaji);
        Assert.Equal("Action, Adventure", metadata.Genre);
    }

    [Fact]
    public async Task GetAsync_FallsBackToAltTitles_WhenPrimaryTitleMapIsEmpty()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, PartialNullResponseJson)));

        ExternalMediaMetadata? metadata = await provider.GetAsync("1", CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Equal("Some Manga", metadata!.Title);
        Assert.Null(metadata.Description);
        Assert.Null(metadata.Genre);
        Assert.Null(metadata.ChapterCount);
        Assert.Null(metadata.VolumeCount);
    }

    [Fact]
    public async Task GetAsync_BlankExternalId_ReturnsNullWithoutCallingMangaDex()
    {
        bool called = false;
        var provider = CreateProvider(new StubHandler((_, _) =>
        {
            called = true;
            return JsonResponse(HttpStatusCode.OK, GetByIdResponseJson);
        }));

        ExternalMediaMetadata? metadata = await provider.GetAsync("  ", CancellationToken.None);

        Assert.Null(metadata);
        Assert.False(called);
    }

    [Fact]
    public async Task GetAsync_NotFound_ReturnsNull()
    {
        var provider = CreateProvider(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound)));

        ExternalMediaMetadata? metadata = await provider.GetAsync("does-not-exist", CancellationToken.None);

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
    public async Task SearchAsync_NetworkFailure_ThrowsProviderUnavailable()
    {
        var provider = CreateProvider(new StubHandler((_, _) => throw new HttpRequestException("connection refused")));

        await Assert.ThrowsAsync<MetadataProviderUnavailableException>(() => provider.SearchAsync("anything", CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_SuccessfulCallWithZeroResults_ReturnsEmpty_DoesNotThrow()
    {
        const string emptyResultsJson = """{ "data": [] }""";
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, emptyResultsJson)));

        var results = await provider.SearchAsync("some nonexistent title", CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public void ProviderKey_IsMangaDex()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, SearchResponseJson)));

        Assert.Equal(ExternalMetadataProvider.MangaDex, provider.ProviderKey);
    }

    private static MangaDexMetadataProvider CreateProvider(HttpMessageHandler handler) =>
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
