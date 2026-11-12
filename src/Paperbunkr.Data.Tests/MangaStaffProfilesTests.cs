using System.Text.Json.Nodes;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// <see cref="MangaStaffProfiles"/> parsing and name matching on canned AniList and MangaDex replies (never the live sites), and the ComicVine
/// location path (2026-11-11 follow-up to docs/superpowers/specs/2026-11-10-series-structure-and-discovery-design.md §3.3).
/// </summary>
public class MangaStaffProfilesTests
{
    private const string AniListReply = """
        {"data": {"Media": {"staff": {"nodes": [
          {"id": 96881, "name": {"full": "Eiichiro Oda", "native": "尾田栄一郎", "alternative": ["Oda Eiichirou"]},
           "image": {"large": "https://s4.anilist.co/file/anilistcdn/staff/large/n96881.png"}, "description": "Creator of __One Piece__."},
          {"id": 1, "name": {"full": "Someone Else", "native": null, "alternative": []},
           "image": {"large": "https://s4.anilist.co/file/anilistcdn/staff/large/default.jpg"}, "description": null}
        ]}}}}
        """;

    private const string MangaDexReply = """
        {"result": "ok", "data": {"id": "a1c7c817", "relationships": [
          {"id": "f5f1", "type": "author", "attributes": {"name": "Oda Eiichiro", "imageUrl": null, "biography": {"en": "Japanese manga artist."}}},
          {"id": "f5f1", "type": "artist", "attributes": {"name": "Oda Eiichiro", "imageUrl": null, "biography": {"en": "Japanese manga artist."}}},
          {"id": "c0v3", "type": "cover_art", "attributes": {"fileName": "cover.jpg"}}
        ]}}
        """;

    [Fact]
    public void AniList_ParsesNamesImageAndBio_AndMatchesTheNativeOrderName()
    {
        var staff = MangaStaffProfiles.ParseAniList(JsonNode.Parse(AniListReply));

        Assert.Equal(2, staff.Count);
        var oda = MangaStaffProfiles.Match(staff, "Oda Eiichiro"); // the index holds family name first
        Assert.NotNull(oda);
        Assert.Equal("https://s4.anilist.co/file/anilistcdn/staff/large/n96881.png", oda!.ImageUrl);
        Assert.Equal("Creator of __One Piece__.", oda.Description);
        Assert.Equal(new[] { "尾田栄一郎", "Oda Eiichirou" }, oda.Aliases);
        Assert.Same(oda, MangaStaffProfiles.Match(staff, "尾田栄一郎"));
        Assert.Null(MangaStaffProfiles.Match(staff, "Someone Else")!.ImageUrl); // AniList's default.jpg placeholder is no image
    }

    [Fact]
    public void MangaDex_ParsesAuthorsAndArtistsOnce_AndIgnoresCoverArt()
    {
        var staff = MangaStaffProfiles.ParseMangaDex(JsonNode.Parse(MangaDexReply));

        var oda = Assert.Single(staff);
        Assert.Null(oda.Profile.ImageUrl);
        Assert.Equal("Japanese manga artist.", oda.Profile.Description);
        Assert.NotNull(MangaStaffProfiles.Match(staff, "Eiichiro Oda"));
    }

    [Theory]
    [InlineData("Eiichiro Oda", "eiichiro  oda", true)]
    [InlineData("Eiichiro Oda", "Oda Eiichiro", true)]
    [InlineData("Lee Jong-hwan", "Lee Jong hwan", true)]
    [InlineData("Eiichiro Oda", "Eiichiro", false)]
    [InlineData("REDICE Studio", "Redice Studio", true)]
    public void SameName_IgnoresCaseSpacingPunctuationAndTwoPartOrder(string a, string b, bool same) =>
        Assert.Equal(same, MangaStaffProfiles.SameName(a, b));

    [Fact]
    public void Match_TwoDifferentPeopleWithTheSameName_IsNoMatch()
    {
        var staff = new[]
        {
            new MangaStaffProfiles.StaffMember(new[] { "Kim Min" }, new EntityProfile("a", null, Array.Empty<string>())),
            new MangaStaffProfiles.StaffMember(new[] { "Kim Min" }, new EntityProfile("b", null, Array.Empty<string>())),
        };

        Assert.Null(MangaStaffProfiles.Match(staff, "Kim Min"));
    }

    [Fact]
    public void ComicVine_HasALocationResource_MetronDoesNot()
    {
        Assert.Equal(("location", "4020"), EntityProfileParser.ComicVineResource(ComicMetadataEntityKind.Location));
        Assert.Null(EntityProfileParser.MetronPath(ComicMetadataEntityKind.Location));
    }
}
