using System.Security.Cryptography;
using System.Text.Json;
using Paperbunkr.Data.Gcd;

namespace Paperbunkr.Data.Tests.Gcd;

/// <summary>The dump → extract build and the read-only store over it (docs/superpowers/specs/2026-09-27-gcd-data-design.md §1-§2).</summary>
public class GcdExtractTests
{
    [Fact]
    public void Extract_KeepsComicSeriesAndRealIssues_Only()
    {
        using var dump = new FakeGcdDump()
            .Series(1, "The Incredible Hulk", 1968, "Marvel")
            .Series(2, "Deleted Series", 1990, "Marvel", deleted: true)
            .Series(3, "A Prose Magazine", 1990, "Marvel", isComics: false)
            .Issue(10, 1, "1", "1968-04-00")
            .Issue(11, 1, "1", variantOf: 10)
            .Issue(12, 1, "2", deleted: true)
            .Issue(20, 2, "1");
        using var store = dump.Open();

        Assert.Equal(new[] { 1 }, store.GetSeries(new[] { 1, 2, 3 }).Select(s => s.Id));
        Assert.Equal(new[] { 10 }, store.IssuesOf(1).Select(i => i.Id));
        Assert.Empty(store.IssuesOf(2));
        Assert.Equal("2026-09-15", store.DumpDate);
    }

    [Fact]
    public void Extract_KeysNamesLikeTheMatcher()
    {
        using var dump = new FakeGcdDump().Series(1, "The Incredible Hulk", 1968, "Marvel");
        using var store = dump.Open();

        var hit = Assert.Single(store.FindSeriesByKey(GcdMatcher.NameKey("Incredible Hulk (1968)")));
        Assert.Equal("Marvel", hit.Publisher);
        Assert.Equal(1968, hit.YearBegan);
        Assert.Equal("en", hit.Language);
    }

    [Fact]
    public void Extract_CarriesBondTypeNames()
    {
        using var dump = new FakeGcdDump()
            .Series(1, "Hulk", 1968, "Marvel").Series(2, "Hulk", 2008, "Marvel").Series(3, "Hulk", 2014, "Marvel")
            .Bond(1, 2, 2).Bond(2, 3, 7);
        using var store = dump.Open();

        var bonds = store.BondsFor(new[] { 2 }).OrderBy(b => b.OriginId).ToList();
        Assert.Equal(2, bonds.Count);
        Assert.Equal("major_name_numbering_continues", bonds[0].BondType);
        Assert.Equal((2, 3, "reboot"), (bonds[1].OriginId, bonds[1].TargetId, bonds[1].BondType));
    }

    [Fact]
    public void Extract_WritesZipAndManifestThatMatch()
    {
        using var dump = new FakeGcdDump().Series(1, "Hulk", 1968, "Marvel");
        var manifest = dump.BuildExtract("https://example.invalid/gcd.zip");

        string zip = Path.Combine(dump.OutDir, "gcd-2026-09-15.zip");
        Assert.True(File.Exists(zip));
        using (var stream = File.OpenRead(zip))
        {
            Assert.Equal(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), manifest.Sha256);
        }

        Assert.Equal(new FileInfo(zip).Length, manifest.SizeBytes);
        var onDisk = JsonSerializer.Deserialize<GcdManifest>(File.ReadAllText(Path.Combine(dump.OutDir, "gcd-data.json")));
        Assert.Equal(manifest, onDisk);
        Assert.Equal(GcdExtractor.SchemaVersion, onDisk!.SchemaVersion);
        Assert.Equal("https://example.invalid/gcd.zip", onDisk.Url);
    }

    [Fact]
    public void DateKeys_PreferOnSale_ThenKeyDate()
    {
        using var dump = new FakeGcdDump()
            .Series(1, "Hulk", 1968, "Marvel")
            .Issue(10, 1, "1", keyDate: "1968-04-00", onSaleDate: "1968-01-12")
            .Issue(11, 1, "2", keyDate: "1968-06-00")
            .Issue(12, 1, "3");
        using var store = dump.Open();

        var keys = store.DateKeysFor(new[] { 10, 11, 12 });
        Assert.Equal(196801, keys[10]);
        Assert.Equal(196806, keys[11]);
        Assert.False(keys.ContainsKey(12));
    }

    [Fact]
    public void TryOpen_RefusesMissingOrUnknownSchema()
    {
        Assert.Null(GcdDataStore.TryOpen(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.sqlite")));

        using var dump = new FakeGcdDump().Series(1, "Hulk", 1968, "Marvel");
        dump.BuildExtract();
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dump.ExtractPath};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE meta SET value = '999' WHERE key = 'schema_version'";
            cmd.ExecuteNonQuery();
        }

        Assert.Null(GcdDataStore.TryOpen(dump.ExtractPath));
    }

    [Theory]
    [InlineData("001", "1")]
    [InlineData("0", "0")]
    [InlineData("½", "1/2")]
    [InlineData(" # 12 ", "12")]
    [InlineData("12 [157]", "12")]
    [InlineData("[nn]", "nn")]
    [InlineData("Annual 1", "annual1")]
    public void NormalizeNumber(string input, string expected) => Assert.Equal(expected, GcdMatcher.NormalizeNumber(input));

    [Theory]
    [InlineData("Marvel Comics", "Marvel")]
    [InlineData("Marvel Worldwide Inc.", "Marvel")]
    [InlineData("DC Comics", "DC")]
    [InlineData("Dark Horse Comics", "Dark Horse")]
    public void PublisherKeys_Agree(string a, string b) => Assert.Equal(GcdMatcher.PublisherKey(a), GcdMatcher.PublisherKey(b));

    [Fact]
    public void PublisherKeys_KeepDistinctPublishersApart() =>
        Assert.NotEqual(GcdMatcher.PublisherKey("DC Comics"), GcdMatcher.PublisherKey("D.C. Thomson"));
}
