using System.Text.Json;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests.Metadata;

/// <summary>AniList and MangaDex now fill the same normalized fields MangaBaka does, so the manga apply path treats all three alike.</summary>
public class ProviderFullExtractionTests
{
    [Fact]
    public async Task AniList_RequestsAndReturnsTheFieldsItNormalizes()
    {
        using var handler = new RequestHandler();
        using var client = new HttpClient(handler);
        var metadata = await new AniListMetadataProvider(client).GetAsync("1", CancellationToken.None);
        using var request = JsonDocument.Parse(handler.Body!);
        string query = request.RootElement.GetProperty("query").GetString()!;
        foreach (string field in new[] { "endDate { year month day }", "startDate { year month day }", "popularity", "idMal", "synonyms", "averageScore", "isAdult", "category" })
            Assert.Contains(field, query);
        Assert.Equal(new ProviderPublicationDate(2001, 5, 3), metadata!.PublicationStart);
        Assert.Equal(new ProviderPublicationDate(2020), metadata.PublicationEnd);
        Assert.Equal(12345, metadata.Popularity);
        Assert.Equal(82, metadata.Rating);
        Assert.Contains((ExternalMetadataProvider.MyAnimeList, "7"), metadata.CrossReferences!);
    }

    private sealed class RequestHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(System.Net.HttpStatusCode.OK) { Content = new StringContent("""
                {"data":{"Media":{"id":1,"title":{"romaji":"Title"},"startDate":{"year":2001,"month":5,"day":3},"endDate":{"year":2020},"popularity":12345,"averageScore":82,"idMal":7}}}
                """, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public void MangaBaka_RetainsUnknownSourceIdentifiers_WithoutTreatingMetadataAsAnId()
    {
        var dto = JsonSerializer.Deserialize<MangaBakaSeriesDto>("""
            {"id":1,"source":{"anime_news_network":{"id":42},"another":{"id":"abc"},"empty":{"id":null},"state":"active"}}
            """)!;
        var metadata = MangaBakaNormalizer.ToMediaMetadata(dto);
        Assert.Equal("42", metadata.AdditionalIdentifiers!["anime_news_network"]);
        Assert.Equal("abc", metadata.AdditionalIdentifiers["another"]);
        Assert.Equal(2, metadata.AdditionalIdentifiers.Count);
    }

    [Fact]
    public void ReportedDetails_UsesLatestSnapshot_AndPreservesPartialDates()
    {
        var metadata = new ExternalMediaMetadata("1", "Title", null, null, null, null, null,
            PublicationStart: new(2000, 2), PublicationEnd: new(2020), Popularity: 100);
        var snapshots = new[] {
            new ExternalMetadataSnapshot { Id = 1, Provider = ExternalMetadataProvider.AniList, Payload = JsonSerializer.Serialize(metadata with { Popularity = 1 }) },
            new ExternalMetadataSnapshot { Id = 2, Provider = ExternalMetadataProvider.AniList, Payload = JsonSerializer.Serialize(metadata) },
            new ExternalMetadataSnapshot { Provider = ExternalMetadataProvider.MangaBaka, Payload = "broken" },
        };
        var rows = ProviderReportedDetails.Read(snapshots);
        Assert.Contains(rows, r => r.Label.Contains("Publication start") && r.Value == "2000-02");
        Assert.Contains(rows, r => r.Label.Contains("Publication end") && r.Value == "2020");
        Assert.Single(rows.Where(r => r.Label.Contains("Popularity")));
        Assert.Contains(rows, r => r.Label.Contains("Popularity") && r.Value == "100");
    }

    [Fact]
    public void AniList_TagRanksBecomeWeights_StaffRolesSplitAuthorsFromArtists_AndTheScoreAndRatingCarryOver()
    {
        var media = JsonSerializer.Deserialize<AniListMediaDto>("""
            {"id":1,"title":{"romaji":"Berserk","english":"Berserk"},"genres":["Action","Ecchi"],"averageScore":93,"isAdult":false,"idMal":2,
             "synonyms":["ベルセルク"],
             "tags":[{"name":"Gore","rank":95,"isMediaSpoiler":false,"category":"Theme-Other"},{"name":"Revenge","rank":50,"isMediaSpoiler":true,"category":"Theme-Drama"}],
             "staff":{"edges":[{"role":"Story & Art","node":{"name":{"full":"Kentaro Miura"}}},{"role":"Story","node":{"name":{"full":"Writer Only"}}},{"role":"Translator","node":{"name":{"full":"Nobody"}}}]}}
            """)!;

        var md = AniListNormalizer.ToMediaMetadata(media);

        Assert.Equal(IssueTagWeight.Core, md.WeightedTags!.Single(t => t.Name == "Gore").Weight);
        Assert.Equal("Theme", md.WeightedTags!.Single(t => t.Name == "Gore").Category);
        Assert.True(md.WeightedTags!.Single(t => t.Name == "Revenge").IsSpoiler);
        Assert.True(md.WeightedTags!.Single(t => t.Name == "Action").IsGenre);
        Assert.Equal(new[] { "Kentaro Miura", "Writer Only" }, md.Authors);
        Assert.Equal(new[] { "Kentaro Miura" }, md.Artists);
        Assert.Equal("suggestive", md.ContentRating);
        Assert.Equal(93, md.Rating);
        Assert.Contains("ベルセルク", md.AlternateTitles!);
        Assert.Contains((ExternalMetadataProvider.MyAnimeList, "2"), md.CrossReferences!);
    }

    [Fact]
    public void AniListAdultFlag_DoesNotClaimPornographicContent()
    {
        var dto = JsonSerializer.Deserialize<AniListMediaDto>("""{"id":1,"title":{"romaji":"Title"},"isAdult":true}""")!;
        var metadata = AniListNormalizer.ToMediaMetadata(dto);
        Assert.Equal("adult", metadata.ContentRating);
        Assert.Equal("Adults Only 18+", Paperbunkr.Data.Metadata.Manga.MangaProviderApply.AgeRatingFrom(metadata.ContentRating));
        dto.IsAdult = false;
        Assert.Null(AniListNormalizer.ToMediaMetadata(dto).ContentRating);
    }

    [Fact]
    public void MangaDex_MapsRatingChaptersAuthorsArtistsAndAlternateTitles()
    {
        var dto = JsonSerializer.Deserialize<MangaDexMangaDto>("""
            {"id":"abc","attributes":{"title":{"en":"Title"},"altTitles":[{"ja":"タイトル"},{"fr":"Titre"}],"contentRating":"erotica","lastChapter":"120.5","lastVolume":"12",
             "tags":[{"attributes":{"name":{"en":"Action"},"group":"genre"}},{"attributes":{"name":{"en":"School Life"},"group":"theme"}}]},
             "relationships":[{"type":"author","attributes":{"name":"A. Writer"}},{"type":"artist","attributes":{"name":"B. Artist"}}]}
            """)!;

        var md = MangaDexNormalizer.ToMediaMetadata(dto);

        Assert.Equal("erotica", md.ContentRating);
        Assert.Equal(120, md.ChapterCount);
        Assert.Equal(12, md.VolumeCount);
        Assert.Equal(new[] { "A. Writer" }, md.Authors);
        Assert.Equal(new[] { "B. Artist" }, md.Artists);
        Assert.Equal(2, md.AlternateTitles!.Count);
        Assert.True(md.WeightedTags!.Single(t => t.Name == "Action").IsGenre);
        Assert.Equal("Theme", md.WeightedTags!.Single(t => t.Name == "School Life").Category);
    }
}
