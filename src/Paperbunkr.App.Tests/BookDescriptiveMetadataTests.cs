using cYo.Projects.ComicRack.Engine.IO.Provider.Books;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The descriptive fields each book format hands back (publisher, language, ISBN, series index, subjects, rating, description) -
/// docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §4. Real synthetic files via the shared fixtures.
/// </summary>
public class BookDescriptiveMetadataTests : IDisposable
{
    private readonly string _path;

    public BookDescriptiveMetadataTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookmeta_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        foreach (string ext in new[] { ".epub", ".fb2", ".mobi" })
        {
            if (File.Exists(_path + ext))
            {
                File.Delete(_path + ext);
            }
        }
    }

    [Fact]
    public void Epub_CalibreOpf_ReadsPublisherIsbnSubjectsIndexRatingAndDescription()
    {
        EpubFixture.Create(_path + ".epub", seriesName: "Dune Chronicles", extraMetadataXml: """
                <dc:publisher>Ace Books</dc:publisher>
                <dc:identifier>urn:isbn:978-0-441-17271-9</dc:identifier>
                <dc:subject>Science Fiction</dc:subject>
                <dc:subject>science fiction</dc:subject>
                <dc:subject>Classics</dc:subject>
                <dc:description>A desert planet.</dc:description>
                <meta name="calibre:series_index" content="2.5"/>
                <meta name="calibre:rating" content="7"/>

            """);

        using var source = new EpubBookSource(_path + ".epub");
        var m = source.Metadata;

        Assert.Equal("Dune Chronicles", m.SeriesName);
        Assert.Equal(2.5f, m.SeriesIndex);
        Assert.Equal("Ace Books", m.Publisher);
        Assert.Equal("9780441172719", m.ISBN);
        Assert.Equal("en", m.LanguageISO);
        Assert.Equal(new[] { "Science Fiction", "Classics" }, m.Genres);
        Assert.Equal("A desert planet.", m.Description);
        Assert.Equal(3.5f, m.Rating);
    }

    [Fact]
    public void Epub_Epub3Collection_ReadsSeriesAndGroupPosition()
    {
        EpubFixture.Create(_path + ".epub", extraMetadataXml: """
                <meta property="belongs-to-collection" id="c1">Dune Chronicles</meta>
                <meta refines="#c1" property="group-position">3</meta>

            """);

        using var source = new EpubBookSource(_path + ".epub");

        Assert.Equal("Dune Chronicles", source.Metadata.SeriesName);
        Assert.Equal(3f, source.Metadata.SeriesIndex);
    }

    [Fact]
    public void Epub_NoDescriptiveMetadata_LeavesFieldsNull()
    {
        EpubFixture.Create(_path + ".epub");

        using var source = new EpubBookSource(_path + ".epub");
        var m = source.Metadata;

        Assert.Null(m.Publisher);
        Assert.Null(m.ISBN);
        Assert.Null(m.Rating);
        Assert.Null(m.SeriesIndex);
        Assert.Empty(m.Genres);
    }

    [Fact]
    public void Epub_SeriesIndexWithoutSeries_IsIgnored()
    {
        EpubFixture.Create(_path + ".epub", extraMetadataXml: "    <meta name=\"calibre:series_index\" content=\"4\"/>\n");

        using var source = new EpubBookSource(_path + ".epub");

        Assert.Null(source.Metadata.SeriesName);
        Assert.Null(source.Metadata.SeriesIndex);
    }

    [Fact]
    public void Fb2_ReadsGenreLangAnnotationPublisherIsbnAndSequenceNumber()
    {
        Fb2Fixture.Create(
            _path + ".fb2",
            seriesName: "Saga",
            extraTitleInfo: "<genre>sf</genre><genre>adventure</genre><lang>en</lang><annotation><p>Blurb</p>\n<p>here.</p></annotation>",
            publishInfoXml: "<publish-info><publisher>Orbit</publisher><isbn>0441172717</isbn></publish-info>");

        using var source = new Fb2BookSource(_path + ".fb2");
        var m = source.Metadata;

        Assert.Equal("Saga", m.SeriesName);
        Assert.Equal(1f, m.SeriesIndex);
        Assert.Equal("Orbit", m.Publisher);
        Assert.Equal("0441172717", m.ISBN);
        Assert.Equal("en", m.LanguageISO);
        Assert.Equal(new[] { "sf", "adventure" }, m.Genres);
        Assert.Equal("Blurb here.", m.Description);
    }

    [Fact]
    public void Mobi_Exth_ReadsPublisherIsbnLanguageSubjectsAndDescription()
    {
        MobiFixture.Create(_path + ".mobi", extraExth: new (int, string)[]
        {
            (101, "Gollancz"),
            (103, "A blurb"),
            (104, "978-0-441-17271-9"),
            (105, "Fantasy"),
            (105, "Adventure"),
            (524, "en"),
        });

        using var source = new MobiBookSource(_path + ".mobi");
        var m = source.Metadata;

        Assert.Equal("Gollancz", m.Publisher);
        Assert.Equal("A blurb", m.Description);
        Assert.Equal("9780441172719", m.ISBN);
        Assert.Equal("en", m.LanguageISO);
        Assert.Equal(new[] { "Fantasy", "Adventure" }, m.Genres);
    }

    [Theory]
    [InlineData("urn:isbn:978-0-441-17271-9", "9780441172719")]
    [InlineData("ISBN 0-441-17271-7", "0441172717")]
    [InlineData("9780441172719", "9780441172719")]
    [InlineData("0306406152", "0306406152")]
    [InlineData("080442957X", "080442957X")]
    [InlineData("978-0-441-17271-8", null)] // bad check digit
    [InlineData("urn:uuid:00000000-0000-0000-0000-000000000001", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractIsbn_AcceptsOnlyValidChecksums(string? input, string? expected)
    {
        Assert.Equal(expected, BookMetadataParsing.ExtractIsbn(input));
    }

    [Theory]
    [InlineData("7", 3.5f)]
    [InlineData("10", 5f)]
    [InlineData("1", 0.5f)]
    [InlineData("0", null)]
    [InlineData("11", null)]
    [InlineData("abc", null)]
    [InlineData(null, null)]
    public void FromCalibreRating_HalvesToAZeroToFiveScale(string? input, float? expected)
    {
        Assert.Equal(expected, BookMetadataParsing.FromCalibreRating(input));
    }

    [Fact]
    public void ParseIndex_AcceptsDecimalsAndRejectsJunk()
    {
        Assert.Equal(2.5f, BookMetadataParsing.ParseIndex("2.5"));
        Assert.Equal(1f, BookMetadataParsing.ParseIndex(" 01 "));
        Assert.Null(BookMetadataParsing.ParseIndex("-1"));
        Assert.Null(BookMetadataParsing.ParseIndex("two"));
        Assert.Null(BookMetadataParsing.ParseIndex(null));
    }

    [Fact]
    public void CleanList_TrimsDedupesCaseInsensitively_AndSplitKeywordsUsesCommasAndSemicolons()
    {
        Assert.Equal(new[] { "A", "b" }, BookMetadataParsing.CleanList(new string?[] { " A ", "a", null, "", "b" }));
        Assert.Equal(new[] { "x", "y", "z" }, BookMetadataParsing.SplitKeywords("x, y; z, X"));
    }
}
