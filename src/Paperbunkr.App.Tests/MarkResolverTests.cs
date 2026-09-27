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
    [InlineData("Prowlarr")]      // the Connections "Download automation" rows
    [InlineData("qBittorrent")]
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
        // DSTLRY is in publisher-aliases.tsv but has neither a curated SVG nor a file in the CE pack.
        var spec = _r.ResolvePublisher("DSTLRY");
        Assert.Equal(MarkKind.LetterMark, spec.Kind);
        Assert.Equal("D", spec.Text);
        Assert.NotNull(spec.Background);
    }

    // --- CE publisher raster pack (docs/superpowers/specs/2026-09-25-publisher-icons-and-reader-
    //     textures-design.md §A2) ---

    [Fact]
    public void ResolvePublisher_NotInTableButInThePack_IsARaster()
    {
        var spec = _r.ResolvePublisher("Ablaze");
        Assert.Equal(MarkKind.Raster, spec.Kind);
        Assert.Contains("Ablaze", spec.AssetPath);
    }

    [Fact]
    public void ResolvePublisher_HashAliasFilename_ResolvesAndOpens()
    {
        // "12bis#12 Bis.png" - the '#' must survive the avares URI round-trip.
        var spec = _r.ResolvePublisher("12 Bis");
        Assert.Equal(MarkKind.Raster, spec.Kind);
        using var stream = Avalonia.Platform.AssetLoader.Open(new Uri(spec.AssetPath!));
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void ResolvePublisher_CuratedSvgStillWins_WhenThereIsNoYear()
    {
        Assert.Equal(MarkKind.SvgAsset, _r.ResolvePublisher("Marvel").Kind);
        Assert.Equal(MarkKind.SvgAsset, _r.ResolvePublisher("DC Comics").Kind);
    }

    [Theory]
    [InlineData(1975, "DC Comics(1974-1976)")]
    [InlineData(1990, "DC Comics(1977-2004)")]
    [InlineData(2012, "DC Comics(2011-2016)")]
    public void ResolvePublisher_WithAYear_PicksThatErasLogoOverTheSvg(int year, string fileStem)
    {
        var spec = _r.ResolvePublisher("DC Comics", year);
        Assert.Equal(MarkKind.Raster, spec.Kind);
        Assert.Contains(fileStem, Uri.UnescapeDataString(spec.AssetPath!));
    }

    [Fact]
    public void ResolvePublisher_DecemberTwentyTwentyFour_PicksTheNewDcLogo()
    {
        var december = _r.ResolvePublisher("DC Comics", 2024, 12);
        var november = _r.ResolvePublisher("DC Comics", 2024, 11);
        Assert.Contains("DC Comics(2024_12)", Uri.UnescapeDataString(december.AssetPath!));
        Assert.Contains("DC Comics(2016-2024_11)", Uri.UnescapeDataString(november.AssetPath!));
    }

    [Fact]
    public void ResolvePublisher_YearOutsideEveryEra_FallsBackToTheCuratedSvg()
    {
        Assert.Equal(MarkKind.SvgAsset, _r.ResolvePublisher("DC Comics", 1800).Kind);
    }

    [Fact]
    public void ResolvePublisher_DatedOnlyPublisher_WithoutAYear_StillGetsARasterNotAChip()
    {
        // The pack only has a dated "(2019 - 2021)" file for this one: no year -> newest era, not plain text.
        var spec = _r.ResolvePublisher("Comic Kairakuten BEAST");
        Assert.Equal(MarkKind.Raster, spec.Kind);
        Assert.Equal(MarkKind.Raster, _r.ResolvePublisher("Comic Kairakuten BEAST", 2020).Kind);
    }

    [Fact]
    public void ResolvePublisher_Unknown_IsPlainText()
    {
        var spec = _r.ResolvePublisher("Some Tiny Press Nobody Has Heard Of");
        Assert.Equal(MarkKind.Text, spec.Kind);
        Assert.Equal("Some Tiny Press Nobody Has Heard Of", spec.Text);
    }

    [Theory]
    [InlineData("Trade Paperback", "trade-paperback")]
    [InlineData("tpb", "trade-paperback")]
    [InlineData("One-Shot", "one-shot")]
    [InlineData("b&w", "black-and-white")]
    [InlineData("Annual", "annual")]
    [InlineData("year 0", "year-zero")]
    public void ResolveFormat_CeAliases_ResolveToPictogram(string input, string stem)
    {
        // Every comic-format row ships a hand-drawn SVG pictogram (via the tsv's `asset` column).
        // These carry their own category-hued colour, so - unlike a mono service/publisher mark -
        // they resolve with no Foreground tint and render as-is.
        var spec = _r.ResolveFormat(input);
        Assert.Equal(MarkKind.SvgAsset, spec.Kind);
        Assert.Equal($"avares://Paperbunkr.App/Assets/Marks/Formats/{stem}.svg", spec.AssetPath);
        Assert.Null(spec.Foreground);
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

    // --- Reading status / scan group (docs/superpowers/specs/2026-09-04-detail-screen-icons-
    //     and-glyphs-design.md §8) ---

    [Theory]
    [InlineData("Reading", "Reading")]
    [InlineData("reading", "Reading")]          // case-insensitive
    [InlineData("ReReading", "Re-reading")]
    [InlineData("Completed", "Completed")]
    [InlineData("Paused", "On Hold")]
    [InlineData("Dropped", "Dropped")]
    [InlineData("Planned", "Planned")]
    public void ResolveReadingStatus_KnownValue_IsColouredGlyph(string enumName, string label)
    {
        var spec = _r.ResolveReadingStatus(enumName);
        Assert.Equal(MarkKind.Glyph, spec.Kind);
        Assert.Equal(label, spec.Text);
        Assert.NotNull(spec.Glyph);
        Assert.StartsWith("#", spec.Foreground);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotAStatus")]
    [InlineData(null)]
    public void ResolveReadingStatus_UnknownOrGarbage_IsNone(string? value)
    {
        Assert.Equal(MarkKind.None, _r.ResolveReadingStatus(value).Kind);
    }

    [Fact]
    public void ResolveScanGroup_GroupName_IsGlyphWithTrimmedText()
    {
        var spec = _r.ResolveScanGroup("  TCB Scans  ");
        Assert.Equal(MarkKind.Glyph, spec.Kind);
        Assert.Equal("TCB Scans", spec.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ResolveScanGroup_Blank_IsNone(string? value)
    {
        Assert.Equal(MarkKind.None, _r.ResolveScanGroup(value).Kind);
    }

    // --- Book-format aliases (docs/superpowers/specs/2026-09-04-detail-screen-icons-and-glyphs-
    //     design.md Part 2 §B) - so the band's Format mark renders a glyph for EPUB/PDF/etc ---

    [Theory]
    [InlineData("EPUB")]
    [InlineData("epub")]
    [InlineData("PDF")]
    [InlineData("FB2")]
    [InlineData("MOBI")]
    [InlineData("azw3")]   // MOBI alias
    [InlineData("CBZ")]
    [InlineData("CBR")]
    public void ResolveFormat_BookFormats_RenderAGlyph(string format)
    {
        var spec = _r.ResolveFormat(format);
        Assert.NotEqual(MarkKind.None, spec.Kind);
        Assert.NotEqual(MarkKind.Text, spec.Kind);
        Assert.Equal(MarkKind.Glyph, spec.Kind);
        Assert.NotNull(spec.Glyph);
    }

    // --- Canonical age-rating list for the metadata editors' Age Rating dropdown
    //     (docs/superpowers/specs/2026-09-05-metadata-editor-affordances-design.md §4.3) ---

    [Fact]
    public void AgeRatingCanonicals_ContainsCanonicalSpellings_NotAliases()
    {
        Assert.Contains("Mature 17+", _r.AgeRatingCanonicals);
        Assert.Contains("Everyone", _r.AgeRatingCanonicals);
        Assert.DoesNotContain("m17", _r.AgeRatingCanonicals);   // an alias, not a canonical
    }
}
