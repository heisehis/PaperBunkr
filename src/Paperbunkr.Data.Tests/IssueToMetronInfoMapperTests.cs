using System.Text;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO.Provider.XmlInfo;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.CeMigration;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises MetronInfo.xml writing and id import (docs/superpowers/specs/2026-10-05-metroninfo-write-
/// back-design.md): <see cref="IssueToMetronInfoMapper"/>, <see cref="MetronIdContext"/>, the v1.1
/// model additions and <see cref="EmbeddedMetronIds"/>. The real in-archive write is covered by
/// App.Tests' MetadataFileWriteBackServiceTests.
/// </summary>
public class IssueToMetronInfoMapperTests : IDisposable
{
    // Shaped like the schema repo's v1.1 Sample.xml, cut down to what these tests read.
    private const string SampleV11 = """
        <?xml version="1.0" encoding="UTF-8"?>
        <MetronInfo xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="MetronInfo.xsd">
            <IDS>
                <ID source="Metron" primary="true">290431</ID>
                <ID source="Comic Vine">12345</ID>
                <ID source="Grand Comics Database">543</ID>
                <ID source="MangaDex">8b34f37a-0181-4f0b-8ce3-01217e9a602c</ID>
            </IDS>
            <Publisher id="12345">
                <Name>DC Comics</Name>
                <Imprint id="1234">Vertigo</Imprint>
            </Publisher>
            <Series id="65478" lang="en">
                <Name>Justice League</Name>
                <Volume>2</Volume>
                <Format>Single Issue</Format>
            </Series>
            <Number>1</Number>
            <AlternativeNumber>783</AlternativeNumber>
            <Stories>
                <Story id="12">Justice League, Part One</Story>
                <Story>Justice League, Part Two</Story>
            </Stories>
            <Prices>
                <Price country="US">3.99</Price>
            </Prices>
            <CoverDate>2011-10-01</CoverDate>
            <StoreDate>2011-08-31</StoreDate>
            <Universes>
                <Universe id="24"><Name>Prime Earth</Name><Designation>Earth 0</Designation></Universe>
            </Universes>
            <Characters>
                <Character id="45678">Aquaman</Character>
                <Character>Batman</Character>
            </Characters>
            <GTIN>
                <ISBN>1234567890123</ISBN>
                <UPC>76194130593600111</UPC>
            </GTIN>
            <AgeRating>Teen</AgeRating>
            <CommunityRating>
                <AverageRating>4.25</AverageRating>
                <RatingCount>12</RatingCount>
            </CommunityRating>
            <URLs>
                <URL primary="true">https://metron.cloud/issue/justice-league-2011-1/</URL>
            </URLs>
            <Credits>
                <Credit>
                    <Creator id="123">Geoff Johns</Creator>
                    <Roles><Role id="1">Writer</Role></Roles>
                </Credit>
            </Credits>
            <LastModified>2024-07-03T12:11:45Z</LastModified>
        </MetronInfo>
        """;

    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public IssueToMetronInfoMapperTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_metroninfo_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static MetronInfo ParseSample() => MetronInfo.TryRead(new MemoryStream(Encoding.UTF8.GetBytes(SampleV11.Trim())))!;

    private static MetronInfo RoundTrip(MetronInfo document) => MetronInfo.TryRead(new MemoryStream(document.ToArray()))!;

    private static Issue SampleIssue() => new()
    {
        Series = new Series { Name = "Kilo Station" },
        Number = "5",
        Title = "Docking",
        Writer = "Jane Writer, Sam Both",
        Penciller = "Sam Both",
        CoverArtist = "Cover Person",
        Characters = "Aquaman, Batman",
        Year = 2022,
        Month = 3,
    };

    private static MetronInfo Map(Issue issue, MetronIdContext? ids = null, MetronInfo? onto = null)
    {
        var document = onto ?? new MetronInfo();
        IssueToMetronInfoMapper.Apply(issue, document, ids ?? MetronIdContext.Empty);
        return RoundTrip(document);
    }

    [Fact]
    public void Model_ReadsTheV11Elements()
    {
        var document = ParseSample();

        Assert.Equal("783", document.AlternativeNumber);
        Assert.Equal(4.25m, document.CommunityRating.AverageRating);
        Assert.Equal(12, document.CommunityRating.RatingCount);
        Assert.Equal("76194130593600111", document.Gtin.Upc);
        Assert.Equal("1234567890123", document.Gtin.Isbn);
    }

    [Fact]
    public void Reader_MapsTheV11ElementsToComicInfo()
    {
        var info = new MetronInfoProvider().ToXml(ParseSample());

        Assert.Equal("783", info.AlternateNumber);
        Assert.Equal(4.25f, info.CommunityRating);
    }

    [Fact]
    public void Apply_GroupsCreditsPerCreator_WithEveryRole()
    {
        var document = Map(SampleIssue());

        Assert.Equal(new[] { "Jane Writer", "Sam Both", "Cover Person" }, document.Credits.Select(c => c.Creator.Value));
        var both = document.Credits.Single(c => c.Creator.Value == "Sam Both");
        Assert.Equal(new[] { RoleValues.Writer, RoleValues.Penciller }, both.Roles.Select(r => r.Value));
        Assert.Equal(RoleValues.Cover, document.Credits.Single(c => c.Creator.Value == "Cover Person").Roles.Single().Value);
    }

    [Fact]
    public void Apply_WritesWhatTheIssueHolds()
    {
        var issue = SampleIssue();
        issue.AlternateNumber = "783";
        issue.Upc = "76194130593600111";
        issue.CommunityRating = 4.5f;
        issue.CommunityRatingCount = 9;
        issue.AgeRating = "Teen Plus";
        issue.Format = "TPB";
        issue.StoryArc = "Origin, The New 52";
        issue.StoryArcNumber = "2";
        issue.MergeFrom(IssueTagField.Genre, new[] { "Sci-Fi" });

        var document = Map(issue);

        Assert.Equal("Kilo Station", document.Series.Name);
        Assert.Equal(FormatType.TradePaperback, document.Series.Format);
        Assert.Equal("5", document.Number);
        Assert.Equal("783", document.AlternativeNumber);
        Assert.Equal("Docking", document.Stories.Single().Value);
        Assert.Equal("76194130593600111", document.Gtin.Upc);
        Assert.Equal(4.5m, document.CommunityRating.AverageRating);
        Assert.Equal(9, document.CommunityRating.RatingCount);
        Assert.Equal(AgeRatingType.TeenPlus, document.AgeRating);
        Assert.Equal("Sci-Fi", document.Genres.Single().Value);
        Assert.Equal(new[] { "Origin", "The New 52" }, document.Arcs.Select(a => a.Name));
        Assert.Equal(2, document.Arcs[0].Number);
        Assert.False(document.Arcs[1].NumberSpecified);
        Assert.Equal(new[] { "Aquaman", "Batman" }, document.Characters.Select(c => c.Value));
    }

    [Fact]
    public void Apply_OmitsTheV11ElementsWhenThereIsNothingToWrite()
    {
        string xml = Encoding.UTF8.GetString(Map(SampleIssue()).ToArray());

        Assert.DoesNotContain("AlternativeNumber", xml);
        Assert.DoesNotContain("CommunityRating", xml);
        Assert.DoesNotContain("LastModified", xml);
        Assert.DoesNotContain("GTIN", xml);
    }

    [Theory]
    [InlineData(2022, 3, 17, "2022-03-17")]
    [InlineData(2022, 3, null, "2022-03-01")]
    [InlineData(2022, 2, 31, "2022-02-01")]
    public void Apply_CoverDate_NeedsYearAndMonth_DayDefaultsToTheFirst(int year, int month, int? day, string expected)
    {
        var issue = SampleIssue();
        (issue.Year, issue.Month, issue.Day) = (year, month, day);

        var document = Map(issue);

        Assert.True(document.CoverDateSpecified);
        Assert.Equal(expected, document.CoverDate.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void Apply_CoverDate_OmittedWithoutAMonth()
    {
        var issue = SampleIssue();
        issue.Month = null;

        Assert.False(Map(issue).CoverDateSpecified);
    }

    [Fact]
    public void Apply_Ids_PrimaryIsTheScrapeSource_AndIdAttributesFollowIt()
    {
        var issue = SampleIssue();
        issue.GcdIssueId = 543;
        var ids = new MetronIdContext
        {
            Primary = ComicProvider.Metron,
            IssueIds = new Dictionary<ComicProvider, string> { [ComicProvider.Metron] = "290431", [ComicProvider.ComicVine] = "12345" },
            SeriesId = "65478",
            CharacterIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["aquaman"] = "45678" },
            CreatorIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Jane Writer"] = "123" },
        };

        var document = Map(issue, ids);

        Assert.Equal(3, document.Ids.Count);
        var primary = Assert.Single(document.Ids, id => id.PrimarySpecified && id.Primary);
        Assert.Equal(InformationSource.Metron, primary.Source);
        Assert.Equal("290431", primary.Value);
        Assert.Equal("543", document.Ids.Single(id => id.Source == InformationSource.GrandComicsDatabase).Value);
        Assert.Equal("65478", document.Series.Id);
        Assert.Equal("45678", document.Characters.Single(c => c.Value == "Aquaman").Id);
        Assert.Null(document.Characters.Single(c => c.Value == "Batman").Id);
        Assert.Equal("123", document.Credits.Single(c => c.Creator.Value == "Jane Writer").Creator.Id);
    }

    [Fact]
    public void Apply_NoPrimary_WritesNoIdAttributesOfItsOwn()
    {
        var ids = new MetronIdContext { IssueIds = new Dictionary<ComicProvider, string> { [ComicProvider.ComicVine] = "12345" } };

        var document = Map(SampleIssue(), ids);

        Assert.DoesNotContain(document.Ids, id => id.PrimarySpecified && id.Primary);
        Assert.Null(document.Series.Id);
        Assert.All(document.Characters, c => Assert.Null(c.Id));
    }

    [Fact]
    public void Apply_OntoAnExistingDocument_KeepsWhatWeDoNotHold()
    {
        var issue = SampleIssue();
        issue.Series = new Series { Name = "Justice League" };
        issue.Publisher = "DC Comics";

        var document = Map(issue, onto: ParseSample());

        Assert.Equal(3.99m, document.Prices.Single().Value);
        Assert.Equal("Prime Earth", document.Universes.Single().Name);
        Assert.True(document.StoreDateSpecified);
        Assert.Equal("Docking", document.Stories[0].Value);
        Assert.Equal("Justice League, Part Two", document.Stories[1].Value);
        Assert.False(document.LastModifiedSpecified);

        // We hold no ids for this book, so the file's own - and its primary - stand.
        Assert.Equal(4, document.Ids.Count);
        Assert.Equal(InformationSource.Metron, document.Ids.Single(id => id.PrimarySpecified && id.Primary).Source);
        Assert.Equal("65478", document.Series.Id);
        Assert.Equal("12345", document.Publisher.Id);
        Assert.Equal("45678", document.Characters.Single(c => c.Value == "Aquaman").Id);
    }

    [Fact]
    public void Apply_OntoAnExistingDocument_OurPrimaryReplacesTheirs()
    {
        var ids = new MetronIdContext
        {
            Primary = ComicProvider.ComicVine,
            IssueIds = new Dictionary<ComicProvider, string> { [ComicProvider.ComicVine] = "999" },
        };

        var document = Map(SampleIssue(), ids, onto: ParseSample());

        var primary = Assert.Single(document.Ids, id => id.PrimarySpecified && id.Primary);
        Assert.Equal(InformationSource.ComicVine, primary.Source);
        Assert.Equal("999", primary.Value);
        Assert.Equal("290431", document.Ids.Single(id => id.Source == InformationSource.Metron).Value); // theirs, kept
        Assert.Null(document.Characters.Single(c => c.Value == "Aquaman").Id); // a Metron id, not Comic Vine's
    }

    [Fact]
    public void Apply_KeepsAtMostOnePrimaryUrl()
    {
        var existing = new MetronInfo();
        existing.UrLs.Add(new UrlType { Value = "https://a.example/", Primary = true, PrimarySpecified = true });
        existing.UrLs.Add(new UrlType { Value = "https://b.example/", Primary = true, PrimarySpecified = true });

        var document = Map(SampleIssue(), onto: existing);

        Assert.Single(document.UrLs, u => u.PrimarySpecified && u.Primary);
    }

    [Theory]
    [InlineData("Mature 17+", AgeRatingType.Mature)]
    [InlineData("everyone", AgeRatingType.Everyone)]
    [InlineData("Adults Only 18+", AgeRatingType.Adult)]
    [InlineData("Rating Pending", AgeRatingType.Unknown)]
    [InlineData(null, AgeRatingType.Unknown)]
    public void MapAgeRating_FollowsTheSchemaMatrix(string? text, AgeRatingType expected)
    {
        Assert.Equal(expected, IssueToMetronInfoMapper.MapAgeRating(text));
    }

    [Fact]
    public void Snapshot_DiffersWhenAMetronInfoOnlyFieldChanges()
    {
        var issue = SampleIssue();
        var before = MetadataFileFieldSnapshot.Capture(issue);
        issue.Upc = "76194130593600111";

        Assert.True(MetadataFileFieldSnapshot.Differ(before, MetadataFileFieldSnapshot.Capture(issue)));
    }

    [Fact]
    public void FromDatabase_NarrowsEntityIdsToThePrimaryProvider()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = "Kilo Station" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", Characters = "Aquaman, Batman", Writer = "Jane Writer", MetadataSource = ComicProvider.Metron };
        context.Issues.Add(issue);
        context.SaveChanges();

        int aquaman = CharacterResolver.GetOrCreate(context, "Aquaman").Id;
        context.ComicMetadataExternalIds.AddRange(
            new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Issue, EntityId = issue.Id, Provider = ComicProvider.Metron, ExternalId = "290431" },
            new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Issue, EntityId = issue.Id, Provider = ComicProvider.ComicVine, ExternalId = "12345" },
            new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Series, EntityId = series.Id, Provider = ComicProvider.Metron, ExternalId = "65478" },
            new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Character, EntityId = aquaman, Provider = ComicProvider.Metron, ExternalId = "45678" },
            new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Character, EntityId = aquaman, Provider = ComicProvider.ComicVine, ExternalId = "4005-1" });
        context.SaveChanges();

        var ids = MetronIdContext.FromDatabase(context, issue);

        Assert.Equal(ComicProvider.Metron, ids.Primary);
        Assert.Equal(2, ids.IssueIds.Count);
        Assert.Equal("65478", ids.SeriesId);
        Assert.Equal("45678", ids.CharacterIds["aquaman"]);
        Assert.False(ids.CharacterIds.ContainsKey("Batman"));
        Assert.Empty(ids.CreatorIds);
    }

    [Fact]
    public void FromDatabase_NoIdForTheScrapeSource_HasNoPrimary()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = "Kilo Station" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", MetadataSource = ComicProvider.Metron };
        context.Issues.Add(issue);
        context.SaveChanges();

        Assert.Null(MetronIdContext.FromDatabase(context, issue).Primary);
    }

    [Fact]
    public void EmbeddedIds_FromDocument_ReadsOurSourcesOnly()
    {
        var ids = EmbeddedMetronIds.FromDocument(ParseSample())!;

        Assert.Equal(290431, ids.IssueIds[ComicProvider.Metron]);
        Assert.Equal(12345, ids.IssueIds[ComicProvider.ComicVine]);
        Assert.Equal(543, ids.GcdIssueId);
        Assert.Equal(ComicProvider.Metron, ids.Primary);
        Assert.Equal(65478, ids.PrimarySeriesId);
    }

    [Fact]
    public void EmbeddedIds_ApplyTo_LinksIssueAndSeries_ButNeverReplacesAnExistingLink()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = "Justice League" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1" };
        context.Issues.Add(issue);
        context.SaveChanges();
        context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Issue, EntityId = issue.Id, Provider = ComicProvider.ComicVine, ExternalId = "777" });
        context.SaveChanges();

        EmbeddedMetronIds.FromDocument(ParseSample())!.ApplyTo(context, issue);

        var links = context.ComicMetadataExternalIds.ToList();
        Assert.Equal("290431", links.Single(l => l.EntityKind == ComicMetadataEntityKind.Issue && l.Provider == ComicProvider.Metron).ExternalId);
        Assert.Equal("777", links.Single(l => l.EntityKind == ComicMetadataEntityKind.Issue && l.Provider == ComicProvider.ComicVine).ExternalId);
        Assert.Equal("65478", links.Single(l => l.EntityKind == ComicMetadataEntityKind.Series).ExternalId);
        Assert.Equal(543, context.Issues.Single().GcdIssueId);
    }

    [Fact]
    public void EmbeddedIds_ApplyTo_SeriesFiledUnderAnotherName_GetsNoSeriesLink()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = "Something Else Entirely" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1" };
        context.Issues.Add(issue);
        context.SaveChanges();

        EmbeddedMetronIds.FromDocument(ParseSample())!.ApplyTo(context, issue);

        Assert.DoesNotContain(context.ComicMetadataExternalIds, l => l.EntityKind == ComicMetadataEntityKind.Series);
    }
}
