using System.Net;
using System.Net.Http;
using System.Text;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="MangaBakaMetadataProvider"/> entirely against a fake
/// <see cref="HttpMessageHandler"/> - no real network calls, same "no live network calls"
/// precedent as <see cref="AniListMetadataProviderTests"/>. Response shapes below are trimmed
/// copies of real, live `api.mangabaka.org/v2/series/search` and `series/{id}` responses (re-
/// fetched a second time after a first capture appeared to show a flat top-level "title" on the
/// search endpoint that didn't reproduce - the fixture below reflects the confirmed-live shape:
/// neither endpoint has a flat "title", both return only "titles", so resolving a display title
/// always goes through <see cref="MangaBakaNormalizer.ResolveDisplayTitle"/>'s fallback chain).
/// </summary>
public class MangaBakaMetadataProviderTests
{
    private const string SearchResponseJson = """
        {
          "status": 200,
          "pagination": { "count": 1, "next": null, "previous": null },
          "data": [
            {
              "id": 708,
              "titles": [
                { "language": "ja", "traits": ["native"], "title": "カグラバチ", "is_primary": true },
                { "language": "en", "traits": ["official"], "title": "Kagurabachi", "is_primary": true }
              ],
              "status": "releasing", "total_chapters": 128, "final_volume": 10
            }
          ]
        }
        """;

    private const string GetByIdResponseJson = """
        {
          "status": 200,
          "data": {
            "id": 708,
            "titles": [
              { "language": "ja", "traits": ["native"], "title": "カグラバチ", "is_primary": true },
              { "language": "ja-Latn", "traits": ["native"], "title": "Kagurabachi", "is_primary": true },
              { "language": "en", "traits": ["official"], "title": "Kagurabachi", "is_primary": true }
            ],
            "description": "Young Chihiro spends his days training under his famous swordsmith father.",
            "status": "releasing",
            "total_chapters": 128,
            "final_volume": 10
          }
        }
        """;

    private const string NoTitleAtAllResponseJson = """
        { "status": 200, "data": { "id": 1, "status": "unknown" } }
        """;

    [Fact]
    public async Task SearchAsync_NoFlatTitleField_ResolvesDisplayTitleFromTitlesArray()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, SearchResponseJson)));

        var results = await provider.SearchAsync("kagurabachi", CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("708", results[0].ExternalId);
        Assert.Equal("Kagurabachi", results[0].Title); // English entry, preferred by ResolveDisplayTitle
        Assert.Null(results[0].Url); // no canonical page URL field exists in the live API response
    }

    [Fact]
    public async Task GetAsync_ParsesTitlesArray_PreferringEnglish()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, GetByIdResponseJson)));

        ExternalMediaMetadata? metadata = await provider.GetAsync("708", CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Equal("708", metadata!.ExternalId);
        Assert.Equal("Kagurabachi", metadata.Title);
        Assert.Equal("Kagurabachi", metadata.TitleEnglish);
        Assert.Equal("Kagurabachi", metadata.TitleRomaji);
        Assert.Equal("カグラバチ", metadata.TitleNative);
        Assert.Equal("releasing", metadata.Status);
        Assert.Equal(128, metadata.ChapterCount);
        Assert.Equal(10, metadata.VolumeCount);
        Assert.StartsWith("Young Chihiro", metadata.Description);
    }

    [Fact]
    public async Task GetAsync_NoTitleFieldAtAll_FallsBackToUntitled_WithoutThrowing()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, NoTitleAtAllResponseJson)));

        ExternalMediaMetadata? metadata = await provider.GetAsync("1", CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Equal("Untitled", metadata!.Title);
    }

    [Fact]
    public async Task GetAsync_NonIntegerExternalId_ReturnsNullWithoutCallingMangaBaka()
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
    public async Task SearchAsync_NotFound_ReturnsEmpty_WithoutThrowing()
    {
        var provider = CreateProvider(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var results = await provider.SearchAsync("anything", CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_NetworkFailure_ReturnsEmpty_WithoutThrowing()
    {
        var provider = CreateProvider(new StubHandler((_, _) => throw new HttpRequestException("connection refused")));

        var results = await provider.SearchAsync("anything", CancellationToken.None);

        Assert.Empty(results);
    }

    /// <summary>
    /// Real bug fixed live: <see cref="MangaBakaMetadataProvider"/>'s rate limiter is instance-
    /// scoped, so every call site constructing a fresh instance per call (this app's usual pattern)
    /// silently defeated it. <see cref="MangaBakaMetadataProvider.Shared"/> exists so every real
    /// call site (`DetailTabsViewModel`) shares one instance/one rate-limit clock instead - this
    /// just confirms the field resolves to the same object every access, matching
    /// <see cref="AniListMetadataProviderTests"/>'s own precedent of not writing a real-delay timing
    /// test for rate-limiting behavior (see that test class's own doc comment).
    /// </summary>
    [Fact]
    public void Shared_IsTheSameInstanceAcrossAccesses()
    {
        Assert.Same(MangaBakaMetadataProvider.Shared, MangaBakaMetadataProvider.Shared);
    }

    [Fact]
    public void ProviderKey_IsMangaBaka()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, SearchResponseJson)));

        Assert.Equal(ExternalMetadataProvider.MangaBaka, provider.ProviderKey);
    }

    private static MangaBakaMetadataProvider CreateProvider(HttpMessageHandler handler) =>
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
