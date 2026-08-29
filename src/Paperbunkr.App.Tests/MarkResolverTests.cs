using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="MarkResolver"/> - alias tables + bundled-asset resolution for the brand / metadata
/// iconography (docs/superpowers/specs/2026-08-28-brand-metadata-iconography-design.md). Runs under
/// <see cref="AvaloniaTestCollection"/> because the resolver reads <c>avares://</c> assets at
/// construction.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class MarkResolverTests
{
    private readonly MarkResolver _r = new();

    [Theory]
    [InlineData("AniList")]       // has a bundled SVG
    [InlineData("MyAnimeList")]
    [InlineData("ComicVine")]
    public void ResolveService_KnownWithAsset_IsSvg(string id)
    {
        var spec = _r.ResolveService(id);
        Assert.Equal(MarkKind.SvgAsset, spec.Kind);
        Assert.NotNull(spec.AssetPath);
    }

    [Theory]
    [InlineData("GrandComicsDatabase", "GCD")]
    [InlineData("Comic Book Reading Orders", "CBRO")]
    [InlineData("ReadThingsRight", "RTR")]
    public void ResolveService_NoAsset_FallsBackToLetterMark(string id, string expected)
    {
        var spec = _r.ResolveService(id);
        Assert.Equal(MarkKind.LetterMark, spec.Kind);
        Assert.Equal(expected, spec.Text);
    }

    [Theory]
    [InlineData("Marvel")]
    [InlineData("marvel comics")]
    [InlineData("MARVEL COMICS")]
    [InlineData("Marvel Worldwide")]
    public void ResolvePublisher_MarvelAliases_AllResolveToTheMarvelAsset(string input)
    {
        var spec = _r.ResolvePublisher(input);
        Assert.Equal(MarkKind.SvgAsset, spec.Kind);
        Assert.Contains("marvel.svg", spec.AssetPath);
    }

    [Fact]
    public void ResolvePublisher_KnownButNoAsset_IsColouredLetterMark()
    {
        var spec = _r.ResolvePublisher("Vertigo");
        Assert.Equal(MarkKind.LetterMark, spec.Kind);
        Assert.Equal("V", spec.Text);
        Assert.NotNull(spec.Background);
    }

    [Fact]
    public void ResolvePublisher_Unknown_IsPlainText()
    {
        var spec = _r.ResolvePublisher("Some Tiny Press Nobody Has Heard Of");
        Assert.Equal(MarkKind.Text, spec.Kind);
        Assert.Equal("Some Tiny Press Nobody Has Heard Of", spec.Text);
    }

    [Theory]
    [InlineData("Trade Paperback", "TPB")]
    [InlineData("tpb", "TPB")]
    [InlineData("One-Shot", "1-SHOT")]
    [InlineData("b&w", "B/W")]
    public void ResolveFormat_CeAliases_Resolve(string input, string expectedLabel)
    {
        var spec = _r.ResolveFormat(input);
        Assert.True(spec.Kind is MarkKind.Glyph or MarkKind.LetterMark);
        Assert.Equal(expectedLabel, spec.Text);
    }

    [Fact]
    public void ResolveFormat_Unknown_IsPlainText()
    {
        Assert.Equal(MarkKind.Text, _r.ResolveFormat("Foil Variant Director Special").Kind);
    }

    [Theory]
    [InlineData("Mature 17+", "mature-17")]
    [InlineData("mature17", "mature-17")]
    [InlineData("Adults Only 18+", "adults-only-18")]
    [InlineData("Teen", "teen")]
    [InlineData("everyone 10", "everyone-10")]
    public void ResolveAgeRating_KnownWithEsrbAsset_IsSvg(string input, string stem)
    {
        var spec = _r.ResolveAgeRating(input);
        Assert.Equal(MarkKind.SvgAsset, spec.Kind);
        Assert.Contains($"/{stem}.svg", spec.AssetPath);
    }

    [Theory]
    [InlineData("MA15+", "MA15+")]
    [InlineData("R18+", "R18+")]
    public void ResolveAgeRating_ChipTierValues_AreColouredLetterMarks(string input, string label)
    {
        var spec = _r.ResolveAgeRating(input);
        Assert.Equal(MarkKind.LetterMark, spec.Kind);
        Assert.Equal(label, spec.Text);
        Assert.NotNull(spec.Background);
    }

    [Fact]
    public void ResolveAgeRating_Unknown_IsPlainText()
    {
        Assert.Equal(MarkKind.Text, _r.ResolveAgeRating("Rated Awesome").Kind);
    }

    [Theory]
    [InlineData("ja", "jp")]
    [InlineData("JA", "jp")]
    [InlineData("en", "us")]
    [InlineData("zh-Hant", "tw")]
    [InlineData("pt_BR", "br")]
    public void ResolveLanguage_MapsToAFlag(string iso, string region)
    {
        var spec = _r.ResolveLanguage(iso);
        Assert.Equal(MarkKind.Flag, spec.Kind);
        Assert.Contains($"/{region}.svg", spec.AssetPath);
    }

    [Fact]
    public void ResolveLanguage_UnknownOrRegionless_IsUppercaseTextChip()
    {
        var spec = _r.ResolveLanguage("eo");
        Assert.Equal(MarkKind.Text, spec.Kind);
        Assert.Equal("EO", spec.Text);
    }

    [Fact]
    public void ResolveSpecial_MangaAndBw_ProduceMarks_OtherwiseEmpty()
    {
        var manga = new Issue { Series = new Series { ContentType = ContentType.Manga }, ColorMode = ColorMode.BlackAndWhite };
        var plain = new Issue { Series = new Series { ContentType = ContentType.Comic }, ColorMode = ColorMode.Color };

        Assert.Equal(2, _r.ResolveSpecial(manga).Count);
        Assert.Empty(_r.ResolveSpecial(plain));
    }

    [Fact]
    public void EmptyOrNullInputs_AreNone()
    {
        Assert.Equal(MarkKind.None, _r.ResolveService(null).Kind);
        Assert.Equal(MarkKind.None, _r.ResolvePublisher("").Kind);
        Assert.Equal(MarkKind.None, _r.ResolveFormat("  ").Kind);
        Assert.Equal(MarkKind.None, _r.ResolveLanguage(null).Kind);
    }
}
