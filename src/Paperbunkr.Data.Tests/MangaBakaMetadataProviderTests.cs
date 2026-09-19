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
/// copies of a real, live `api.mangabaka.org/v1/series/search?q=One+Piece` response, captured
/// 2026-09-18 during the `v2`→`v1` migration (docs/superpowers/specs/2026-09-18-external-metadata-
/// full-extraction-design.md §6) - `v1` returns the *same* rich shape for both search and get-by-id
/// (unlike `v2`'s search/get split this file's fixtures previously modeled), with a reliable flat
/// `title`/`native_title`/`romanized_title`, a real `canonical_url`, and `total_chapters`/
/// `final_volume` as strings, not ints.
/// </summary>
public class MangaBakaMetadataProviderTests
{
    private const string SearchResponseJson = """
        {
          "status": 200,
          "pagination": { "count": 1, "next": null, "previous": null },
          "data": [
            {
              "id": 377,
              "title": "ONE PIECE",
              "native_title": "ONE PIECE",
              "romanized_title": "ONE PIECE",
              "canonical_url": "https://mangabaka.org/manga/377/ONE-PIECE",
              "titles": [
                { "language": "ja-Latn", "traits": ["native"], "title": "ONE PIECE", "is_primary": true }
              ],
              "status": "releasing", "type": "manga", "year": 1997,
              "total_chapters": "1193", "final_volume": "115",
              "authors": ["Eiichirou Oda"], "artists": ["Eiichirou Oda"],
              "genres": ["action", "adventure", "shounen"],
              "tags_v2": [
                { "name": "Pirates", "name_path": "Setting > Pirates", "is_genre": false, "is_spoiler": false },
                { "name": "Time Skip", "name_path": "Time Skip", "is_genre": false, "is_spoiler": false },
                { "name": "Spoiler Tag", "name_path": "Plot > Spoiler Tag", "is_genre": false, "is_spoiler": true }
              ],
              "source": {
                "anilist": { "id": 30013 },
                "manga_updates": { "id": "pb8uwds" }
              },
              "cover": { "raw": { "url": "https://images.mangabaka.dev/one-piece.jpg" } }
            }
          ]
        }
        """;

    private const string GetByIdResponseJson = """
        {
          "status": 200,
          "data": {
            "id": 708,
            "title": "Kagurabachi",
            "native_title": "カグラバチ",
            "romanized_title": "Kagurabachi",
            "canonical_url": "https://mangabaka.org/manga/708/Kagurabachi",
            "titles": [
              { "language": "ja", "traits": ["native"], "title": "カグラバチ", "is_primary": true },
              { "language": "en", "traits": ["official"], "title": "Kagurabachi", "is_primary": true }
            ],
            "description": "Young Chihiro spends his days training under his famous swordsmith father.",
            "status": "releasing",
            "type": "manga",
            "total_chapters": "128",
            "final_volume": "10"
          }
        }
        """;

    private const string NoTitleAtAllResponseJson = """
        { "status": 200, "data": { "id": 1, "status": "unknown" } }
        """;

    /// <summary>Get-by-id shape carrying every field this test class's rich-field assertions need
    /// - a separate fixture from <see cref="GetByIdResponseJson"/> so that one stays a minimal
    /// "just the basics" case.</summary>
    private const string GetByIdRichResponseJson = """
        {
          "status": 200,
          "data": {
            "id": 377,
            "title": "ONE PIECE",
            "canonical_url": "https://mangabaka.org/manga/377/ONE-PIECE",
            "status": "releasing", "type": "manga", "year": 1997,
            "total_chapters": "1193", "final_volume": "115",
            "authors": ["Eiichirou Oda"], "artists": ["Eiichirou Oda"],
            "genres": ["action", "adventure", "shounen"],
            "tags_v2": [
              { "name": "Pirates", "name_path": "Setting > Pirates", "is_genre": false, "is_spoiler": false },
              { "name": "Time Skip", "name_path": "Time Skip", "is_genre": false, "is_spoiler": false },
              { "name": "Spoiler Tag", "name_path": "Plot > Spoiler Tag", "is_genre": false, "is_spoiler": true }
            ],
            "source": {
              "anilist": { "id": 30013 },
              "manga_updates": { "id": "pb8uwds" }
            },
            "cover": { "raw": { "url": "https://images.mangabaka.dev/one-piece.jpg" } }
          }
        }
        """;

    [Fact]
    public async Task SearchAsync_ParsesFlatTitleAndCanonicalUrl()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, SearchResponseJson)));

        var results = await provider.SearchAsync("one piece", CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("377", results[0].ExternalId);
        Assert.Equal("ONE PIECE", results[0].Title);
        Assert.Equal("https://mangabaka.org/manga/377/ONE-PIECE", results[0].Url);
    }

    [Fact]
    public async Task GetAsync_ParsesRichFieldsAddedForFullExtraction()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, GetByIdRichResponseJson)));

        ExternalMediaMetadata? metadata = await provider.GetAsync("377", CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Equal("https://images.mangabaka.dev/one-piece.jpg", metadata!.CoverImageUrl);
        Assert.Equal("Eiichirou Oda", metadata.Creator); // author == artist here, deduped to one name
        Assert.Equal(1997, metadata.PublicationYear);
        Assert.Equal("manga", metadata.PublicationFormat);
        Assert.Equal(1193, metadata.ChapterCount);
        Assert.Equal(115, metadata.VolumeCount);
        Assert.Equal(new[] { "action", "adventure", "shounen" }, metadata.GenreTags);
        Assert.NotNull(metadata.OtherTags);
        Assert.DoesNotContain(metadata.OtherTags!, t => t.Value == "Spoiler Tag"); // is_spoiler: true excluded
        Assert.Contains(metadata.OtherTags!, t => t.Value == "Pirates" && t.Category == "Setting");
        Assert.NotNull(metadata.CrossReferences);
        Assert.Contains(metadata.CrossReferences!, r => r.Provider == ExternalMetadataProvider.AniList && r.ExternalId == "30013");
        Assert.Contains(metadata.CrossReferences!, r => r.Provider == ExternalMetadataProvider.MangaUpdates && r.ExternalId == "pb8uwds");
    }

    [Fact]
    public async Task GetAsync_ParsesFlatTitleFieldsAndStringChapterVolumeCounts()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, GetByIdResponseJson)));

        ExternalMediaMetadata? metadata = await provider.GetAsync("708", CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Equal("708", metadata!.ExternalId);
        Assert.Equal("Kagurabachi", metadata.Title);
        Assert.Equal("https://mangabaka.org/manga/708/Kagurabachi", metadata.Url);
        Assert.Equal("Kagurabachi", metadata.TitleEnglish);
        Assert.Equal("Kagurabachi", metadata.TitleRomaji);
        Assert.Equal("カグラバチ", metadata.TitleNative);
        Assert.Equal("releasing", metadata.Status);
        Assert.Equal(128, metadata.ChapterCount);
        Assert.Equal(10, metadata.VolumeCount);
        Assert.StartsWith("Young Chihiro", metadata.Description);
        Assert.Null(metadata.Creator); // no authors/artists field in this fixture, confirmed absent for MangaBaka generally
        Assert.Null(metadata.Demographic); // confirmed absent from the v1 schema entirely
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
    // --- GetCoverCandidatesAsync (docs/superpowers/specs/2026-09-18-external-metadata-full-
    // extraction-design.md §2) ---

    private const string ImagesResponseJson = """
        {
          "status": 200,
          "pagination": { "count": 2, "next": null, "previous": null, "page": 1, "limit": 24 },
          "data": [
            {
              "type": "banner",
              "note": "Banner from Weekly Shounen Jump website",
              "image": {
                "raw": { "url": "https://images.mangabaka.dev/full-banner.jpg" },
                "x150": { "x1": "https://cdn.mangabaka.dev/thumb-banner.jpg" }
              }
            },
            {
              "type": "volume",
              "note": null,
              "image": { "raw": { "url": "https://images.mangabaka.dev/full-volume.jpg" } }
            }
          ]
        }
        """;

    [Fact]
    public async Task GetCoverCandidatesAsync_ParsesCandidates_ThumbnailFallsBackToRawWhenNoX150()
    {
        var provider = CreateProvider(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, ImagesResponseJson)));

        var candidates = await provider.GetCoverCandidatesAsync("377", CancellationToken.None);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("https://cdn.mangabaka.dev/thumb-banner.jpg", candidates[0].ThumbnailUrl);
        Assert.Equal("https://images.mangabaka.dev/full-banner.jpg", candidates[0].FullUrl);
        Assert.Equal("banner", candidates[0].Type);
        Assert.Equal("Banner from Weekly Shounen Jump website", candidates[0].Source);
        Assert.Equal("https://images.mangabaka.dev/full-volume.jpg", candidates[1].ThumbnailUrl); // no x150 - falls back to raw
    }

    // --- GetRelationsAsync (docs/superpowers/specs/2026-09-18-external-metadata-full-extraction-
    // design.md §5) - relationships_v2 is embedded in the plain get-by-id response; each relation
    // needs a follow-up GetAsync call to resolve a display title. ---

    private const string RelationshipsGetByIdResponseJson = """
        {
          "status": 200,
          "data": {
            "id": 377,
            "title": "ONE PIECE",
            "relationships_v2": [
              { "to_series_id": 40135, "relation_type": "prequel" }
            ]
          }
        }
        """;

    private const string TargetGetByIdResponseJson = """
        {
          "status": 200,
          "data": { "id": 40135, "title": "Prequel Series", "canonical_url": "https://mangabaka.org/manga/40135/Prequel" }
        }
        """;

    [Fact]
    public async Task GetRelationsAsync_ResolvesTargetTitleViaFollowUpCall_AndMapsRelationType()
    {
        var provider = CreateProvider(new StubHandler((request, _) =>
        {
            string url = request.RequestUri!.ToString();
            return url.Contains("/series/377")
                ? JsonResponse(HttpStatusCode.OK, RelationshipsGetByIdResponseJson)
                : JsonResponse(HttpStatusCode.OK, TargetGetByIdResponseJson);
        }));

        var relations = await provider.GetRelationsAsync("377", CancellationToken.None);

        var relation = Assert.Single(relations);
        Assert.Equal("40135", relation.TargetExternalId);
        Assert.Equal("Prequel Series", relation.TargetTitle);
        Assert.Equal("https://mangabaka.org/manga/40135/Prequel", relation.TargetUrl);
        Assert.Equal(ExternalMetadataProvider.MangaBaka, provider.ProviderKey); // sanity - same provider instance served both calls
        Assert.Equal(RelationType.Prequel, relation.Type);
    }

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
