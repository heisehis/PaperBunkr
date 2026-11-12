using System.Text.Json.Nodes;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// <see cref="EntityProfileParser"/> (docs/superpowers/specs/2026-11-10-series-structure-and-discovery-plan.md D1.3) on canned Metron and ComicVine
/// records. The field names follow the docs; whether Metron's live records carry them is checked by hand during the build (spec §9).
/// </summary>
public class EntityProfileParserTests
{
    [Fact]
    public void Metron_ReadsImageDescriptionAndAliasList()
    {
        var root = JsonNode.Parse("""
            {"id": 1, "name": "Storm", "alias": ["Ororo Munroe", " ororo munroe ", ""],
             "desc": "<p>A mutant who can control the weather.</p>", "image": "https://static.metron.cloud/storm.jpg"}
            """);

        var profile = EntityProfileParser.FromMetron(root);

        Assert.NotNull(profile);
        Assert.Equal("https://static.metron.cloud/storm.jpg", profile!.ImageUrl);
        Assert.Equal("A mutant who can control the weather.", profile.Description);
        Assert.Equal(new[] { "Ororo Munroe" }, profile.Aliases);
    }

    [Fact]
    public void Metron_MissingFieldsAreNull_AndARecordWithoutANameIsNull()
    {
        var profile = EntityProfileParser.FromMetron(JsonNode.Parse("""{"id": 2, "name": "Cyclops", "image": null, "desc": ""}"""));

        Assert.NotNull(profile);
        Assert.Null(profile!.ImageUrl);
        Assert.Null(profile.Description);
        Assert.Empty(profile.Aliases);
        Assert.Null(EntityProfileParser.FromMetron(JsonNode.Parse("""{"detail": "Not found."}""")));
    }

    [Fact]
    public void ComicVine_PrefersTheMediumImage_OverTheMultiMegabyteOriginal()
    {
        var root = JsonNode.Parse("""
            {"results": {"name": "Supergirl", "image": {"original_url": "https://cv/original.jpg", "super_url": "https://cv/super.jpg", "medium_url": "https://cv/medium.jpg"}}}
            """);

        Assert.Equal("https://cv/medium.jpg", EntityProfileParser.FromComicVine(root)!.ImageUrl);
    }

    [Fact]
    public void ComicVine_ReadsResultsImageDeckAndNewlineAliases()
    {
        var root = JsonNode.Parse("""
            {"status_code": 1, "results": {"name": "Storm", "deck": "Weather-controlling mutant.",
             "aliases": "Ororo Munroe\r\nWindrider\n", "image": {"original_url": "https://comicvine.gamespot.com/a/storm.jpg"}}}
            """);

        var profile = EntityProfileParser.FromComicVine(root);

        Assert.NotNull(profile);
        Assert.Equal("https://comicvine.gamespot.com/a/storm.jpg", profile!.ImageUrl);
        Assert.Equal("Weather-controlling mutant.", profile.Description);
        Assert.Equal(new[] { "Ororo Munroe", "Windrider" }, profile.Aliases);
    }

    [Fact]
    public void ComicVine_BlankPlaceholderImageIsNoImage()
    {
        var root = JsonNode.Parse("""{"results": {"name": "Nobody", "image": {"original_url": "https://comicvine.gamespot.com/a/uploads/original/11122/111222211/img/blank.png"}}}""");

        Assert.Null(EntityProfileParser.FromComicVine(root)!.ImageUrl);
    }

    [Theory]
    [InlineData(ComicMetadataEntityKind.Character, "character", "character", "4005")]
    [InlineData(ComicMetadataEntityKind.Team, "team", "team", "4060")]
    [InlineData(ComicMetadataEntityKind.Creator, "creator", "person", "4040")]
    public void Paths_PerKind(ComicMetadataEntityKind kind, string metron, string cvResource, string cvPrefix)
    {
        Assert.Equal(metron, EntityProfileParser.MetronPath(kind));
        Assert.Equal((cvResource, cvPrefix), EntityProfileParser.ComicVineResource(kind));
    }

    [Fact]
    public void Paths_KindsWithoutProfiles_AreNull()
    {
        Assert.Null(EntityProfileParser.MetronPath(ComicMetadataEntityKind.Location));
        Assert.Null(EntityProfileParser.ComicVineResource(ComicMetadataEntityKind.Publisher));
    }
}
