using System.Net;
using System.Net.Http;
using System.Text;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// The two type signals the content-type classifier needed that the providers never fetched (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md):
/// AniList's <c>countryOfOrigin</c> and MangaDex's <c>originalLanguage</c>. Fake <see cref="HttpMessageHandler"/>, no network.
/// </summary>
public class ProviderTypeSignalTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _json;

        public StubHandler(string json) => _json = json;

        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_json, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task AniList_AsksForCountryOfOrigin_AndSurfacesIt()
    {
        var handler = new StubHandler("""
            { "data": { "Media": { "id": 105778, "title": { "romaji": "Na Honjaman Level Up", "english": "Solo Leveling" },
              "siteUrl": "https://anilist.co/manga/105778", "format": "MANGA", "countryOfOrigin": "KR" } } }
            """);
        var provider = new AniListMetadataProvider(new HttpClient(handler));

        var metadata = await provider.GetAsync("105778", CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Equal("KR", metadata!.CountryOfOrigin);
        Assert.Equal("MANGA", metadata.PublicationFormat);
        Assert.Contains("countryOfOrigin", handler.LastRequestBody);
        Assert.Equal(ContentType.Manhwa, ProviderContentTypeMapper.Map(ExternalMetadataProvider.AniList, metadata).Type);
    }

    [Fact]
    public async Task MangaDex_SurfacesOriginalLanguage()
    {
        var handler = new StubHandler("""
            { "data": { "id": "32d76d19-8a05-4db0-9fc2-e0b0648fe9d0", "attributes": {
              "title": { "en": "Solo Leveling" }, "altTitles": [], "status": "completed", "tags": [], "originalLanguage": "ko" } } }
            """);
        var provider = new MangaDexMetadataProvider(new HttpClient(handler));

        var metadata = await provider.GetAsync("32d76d19-8a05-4db0-9fc2-e0b0648fe9d0", CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Equal("ko", metadata!.OriginalLanguage);
        Assert.Equal(ContentType.Manhwa, ProviderContentTypeMapper.Map(ExternalMetadataProvider.MangaDex, metadata).Type);
    }

    [Fact]
    public async Task MetadataProviderEvidenceSource_SearchesScoresFetchesAndMaps()
    {
        var provider = new FakeProvider();
        var source = new MetadataProviderEvidenceSource(provider);

        var evidence = await source.GetEvidenceAsync(new[] { "Solo Leveling" }, CancellationToken.None);

        Assert.NotNull(evidence);
        Assert.Equal(ContentType.Manhwa, evidence!.Type);
        Assert.Equal("manhwa", evidence.Raw);
        Assert.Equal(1.0, evidence.Score);
        Assert.Equal("2", evidence.ExternalId);   // the better-scoring search result, not the first
    }

    [Fact]
    public async Task MetadataProviderEvidenceSource_ReturnsNothingBelowTheReviewThreshold()
    {
        var source = new MetadataProviderEvidenceSource(new FakeProvider());

        Assert.Null(await source.GetEvidenceAsync(new[] { "Completely Unrelated Title" }, CancellationToken.None));
    }

    private sealed class FakeProvider : IMetadataProvider
    {
        public ExternalMetadataProvider ProviderKey => ExternalMetadataProvider.MangaBaka;

        public Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MetadataSearchResult>>(new[]
            {
                new MetadataSearchResult("1", "Solo Leveling Ragnarok", null),
                new MetadataSearchResult("2", "Solo Leveling", null),
            });

        public Task<ExternalMediaMetadata?> GetAsync(string externalId, CancellationToken cancellationToken) =>
            Task.FromResult<ExternalMediaMetadata?>(new ExternalMediaMetadata(externalId, "Solo Leveling", null, null, null, null, null, PublicationFormat: "manhwa"));
    }
}
