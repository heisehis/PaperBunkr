using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.CeMigration;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// End-to-end exercise of the ComicDb.xml -> SQLite migration path. Rather than hand-writing a
/// ComicDb.xml fixture by guessing at CE's XML shape, this builds a small sample library using
/// the real ported <see cref="ComicBook"/>/<see cref="ComicDatabase"/> types and round-trips it
/// through the real <c>ComicDatabase.SaveXml</c>/<c>LoadXml</c> (XmlSerializer-based)
/// (de)serialization - the same code path a real CE install's ComicDb.xml went through - so the
/// fixture is guaranteed structurally valid rather than assumed. A copy of the generated XML is
/// also written to TestData/SampleComicDb.xml (see <see cref="WriteSampleFixtureToRepo_ForReference"/>)
/// as a checked-in reference fixture.
/// </summary>
public class CeLibraryMigratorTests : IDisposable
{
    private readonly string _xmlPath;
    private readonly string _dbPath;

    public CeLibraryMigratorTests()
    {
        _xmlPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_test_{Guid.NewGuid():N}.xml");
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        // Sqlite keeps pooled native connections open briefly after the last DbContext using them
        // is disposed; clear the pool so the temp file can actually be deleted here.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TryDelete(_xmlPath);
        TryDelete(_xmlPath + ".bak");
        TryDelete(_dbPath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort temp-file cleanup; not worth failing the test run over.
        }
    }

    /// <summary>
    /// Builds a small library across 3 series (2-3 books total per series, 7 books overall,
    /// mirroring the task's "2-3 books across a couple of series" ask) exercising all four rows
    /// of the docs/onboarding.md §6 CE Manga -> ContentType/ReadingMode mapping table.
    /// </summary>
    private static ComicDatabase BuildSampleDatabase()
    {
        var db = ComicDatabase.CreateNew();

        // Comic (Manga = No) -> Comic / LeftToRight
        db.Books.Add(new ComicBook
        {
            Series = "Astro Sentinels",
            Number = "1",
            Volume = 1,
            Writer = "Jane Doe",
            Publisher = "Bunker Comics",
            Genre = "Sci-Fi",
            Manga = MangaYesNo.No,
        });
        db.Books.Add(new ComicBook
        {
            Series = "Astro Sentinels",
            Number = "2",
            Volume = 1,
            Writer = "Jane Doe",
            Publisher = "Bunker Comics",
            Genre = "Sci-Fi",
            Manga = MangaYesNo.No,
        });

        // Manga (Manga = YesAndRightToLeft) -> Manga / RightToLeft
        db.Books.Add(new ComicBook
        {
            Series = "Moonlit Blade",
            Number = "1",
            Volume = 1,
            Writer = "Kenji Sato",
            Publisher = "Paper Press",
            Genre = "Action",
            Manga = MangaYesNo.YesAndRightToLeft,
            SeriesComplete = YesNo.Yes,
        });
        db.Books.Add(new ComicBook
        {
            Series = "Moonlit Blade",
            Number = "2",
            Volume = 1,
            Writer = "Kenji Sato",
            Publisher = "Paper Press",
            Genre = "Action",
            Manga = MangaYesNo.YesAndRightToLeft,
            SeriesComplete = YesNo.Yes,
        });
        db.Books.Add(new ComicBook
        {
            Series = "Moonlit Blade",
            Number = "3",
            Volume = 1,
            Writer = "Kenji Sato",
            Publisher = "Paper Press",
            Genre = "Action",
            Manga = MangaYesNo.YesAndRightToLeft,
            SeriesComplete = YesNo.Yes,
        });

        // Manga (Manga = Yes) -> Manga / LeftToRight
        db.Books.Add(new ComicBook
        {
            Series = "Sunrise Diner",
            Number = "1",
            Writer = "Aiko Tanaka",
            Manga = MangaYesNo.Yes,
        });

        // Unknown (Manga = Unknown) -> Unknown / LeftToRight (default, not a real inference)
        db.Books.Add(new ComicBook
        {
            Series = "Forgotten Reels",
            Number = "1",
            Manga = MangaYesNo.Unknown,
        });

        return db;
    }

    private static DbContextOptions<PaperbunkrDbContext> BuildContextOptions(string dbPath) =>
        new DbContextOptionsBuilder<PaperbunkrDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

    [Fact]
    public void Migrate_WritesGroupedSeriesAndIssues_FromRealSerializedXml()
    {
        var source = BuildSampleDatabase();
        source.SaveXml(_xmlPath);
        Assert.True(File.Exists(_xmlPath));

        var loaded = CeLibraryMigrator.LoadFromXml(_xmlPath);
        Assert.Equal(7, loaded.Books.Count);

        var options = BuildContextOptions(_dbPath);
        using (var context = new PaperbunkrDbContext(options))
        {
            context.Database.EnsureCreated();

            var migrator = new CeLibraryMigrator();
            var result = migrator.Migrate(loaded, context);

            Assert.Equal(4, result.SeriesCreated);
            Assert.Equal(7, result.IssuesCreated);
            Assert.Equal(1, result.SeriesWithGuessedContentType); // "Forgotten Reels"
        }

        using (var context = new PaperbunkrDbContext(options))
        {
            Assert.Equal(4, context.Series.Count());
            Assert.Equal(7, context.Issues.Count());

            var astro = context.Series.Include(s => s.Issues).Single(s => s.Name == "Astro Sentinels");
            Assert.Equal(ContentType.Comic, astro.ContentType);
            Assert.Equal(ReadingMode.LeftToRight, astro.ReadingMode);
            Assert.Equal(2, astro.Issues.Count);
            Assert.False(astro.IsComplete);

            var moonlit = context.Series.Include(s => s.Issues).Single(s => s.Name == "Moonlit Blade");
            Assert.Equal(ContentType.Manga, moonlit.ContentType);
            Assert.Equal(ReadingMode.RightToLeft, moonlit.ReadingMode);
            Assert.Equal(3, moonlit.Issues.Count);
            Assert.True(moonlit.IsComplete);

            var sunrise = context.Series.Single(s => s.Name == "Sunrise Diner");
            Assert.Equal(ContentType.Manga, sunrise.ContentType);
            Assert.Equal(ReadingMode.LeftToRight, sunrise.ReadingMode);

            var forgotten = context.Series.Single(s => s.Name == "Forgotten Reels");
            Assert.Equal(ContentType.Unknown, forgotten.ContentType);
            Assert.Equal(ReadingMode.LeftToRight, forgotten.ReadingMode);
        }
    }

    [Fact]
    public void Preview_ReportsCountsWithoutWritingToContext()
    {
        var source = BuildSampleDatabase();
        source.SaveXml(_xmlPath);

        var loaded = CeLibraryMigrator.LoadFromXml(_xmlPath);
        var migrator = new CeLibraryMigrator();
        var preview = migrator.Preview(loaded);

        Assert.Equal(4, preview.SeriesCount);
        Assert.Equal(7, preview.IssueCount);
        Assert.Equal(1, preview.SeriesWithGuessedContentType);
    }

    [Fact]
    public void Migrate_FromCheckedInFixture_ProducesExpectedCounts()
    {
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "TestData", "SampleComicDb.xml");
        Assert.True(File.Exists(fixturePath), $"Fixture not found at {fixturePath}");

        var loaded = CeLibraryMigrator.LoadFromXml(fixturePath);
        Assert.Equal(7, loaded.Books.Count);

        var options = BuildContextOptions(_dbPath);
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();

        var result = new CeLibraryMigrator().Migrate(loaded, context);

        Assert.Equal(4, result.SeriesCreated);
        Assert.Equal(7, result.IssuesCreated);
        Assert.Equal(1, result.SeriesWithGuessedContentType);
    }

    [Theory]
    [InlineData(MangaYesNo.YesAndRightToLeft, ContentType.Manga, ReadingMode.RightToLeft)]
    [InlineData(MangaYesNo.Yes, ContentType.Manga, ReadingMode.LeftToRight)]
    [InlineData(MangaYesNo.No, ContentType.Comic, ReadingMode.LeftToRight)]
    [InlineData(MangaYesNo.Unknown, ContentType.Unknown, ReadingMode.LeftToRight)]
    public void MapMangaField_MatchesDocsSection6Table(MangaYesNo manga, ContentType expectedContentType, ReadingMode expectedReadingMode)
    {
        var (contentType, readingMode) = CeLibraryMigrator.MapMangaField(manga);
        Assert.Equal(expectedContentType, contentType);
        Assert.Equal(expectedReadingMode, readingMode);
    }
}
