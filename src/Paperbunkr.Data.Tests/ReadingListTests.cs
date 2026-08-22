using cYo.Projects.ComicRack.Engine.Database;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises the reading-list engine (docs/superpowers/specs/2026-08-06-reading-lists-design.md):
/// matcher resolution, CBL round-trip (via the real ported serializer, not a hand-written fixture —
/// same precedent as <see cref="CeLibraryMigratorTests"/>), CSV parsing, and text export formatting.
/// </summary>
public class ReadingListTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;
    private readonly int _seriesAId;
    private readonly int _issue1Id;
    private readonly int _issue2VolumeAId;
    private readonly int _issue2VolumeBId;

    public ReadingListTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_readinglist_test_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _context = new PaperbunkrDbContext(options);
        _context.Database.EnsureCreated();

        var seriesA = new Series { Name = "Kilo Station", SortName = "Kilo Station" };
        _context.Series.Add(seriesA);
        _context.SaveChanges();
        _seriesAId = seriesA.Id;

        var issue1 = new Issue { SeriesId = seriesA.Id, Number = "1", Year = 2020 };
        // Same Series+Number across two volumes/years - exercises the tie-breaker.
        var issue2VolumeA = new Issue { SeriesId = seriesA.Id, Number = "2", Volume = "1", Year = 2019 };
        var issue2VolumeB = new Issue { SeriesId = seriesA.Id, Number = "2", Volume = "2", Year = 2023 };
        _context.Issues.AddRange(issue1, issue2VolumeA, issue2VolumeB);
        _context.SaveChanges();

        _issue1Id = issue1.Id;
        _issue2VolumeAId = issue2VolumeA.Id;
        _issue2VolumeBId = issue2VolumeB.Id;
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    // --- Matcher ---

    [Fact]
    public void FindExisting_MatchesBySeriesAndNumber()
    {
        var match = ReadingListMatcher.FindExisting(_context, "kilo station", "1");
        Assert.NotNull(match);
        Assert.Equal(_issue1Id, match!.Id);
    }

    [Fact]
    public void FindExisting_NarrowsAmbiguousMatchByVolume()
    {
        var match = ReadingListMatcher.FindExisting(_context, "Kilo Station", "2", volume: "2");
        Assert.NotNull(match);
        Assert.Equal(_issue2VolumeBId, match!.Id);
    }

    [Fact]
    public void FindExisting_NarrowsAmbiguousMatchByYear()
    {
        var match = ReadingListMatcher.FindExisting(_context, "Kilo Station", "2", year: 2019);
        Assert.NotNull(match);
        Assert.Equal(_issue2VolumeAId, match!.Id);
    }

    [Fact]
    public void FindExisting_ReturnsNullWhenNoMatch()
    {
        Assert.Null(ReadingListMatcher.FindExisting(_context, "Nonexistent Series", "1"));
    }

    [Fact]
    public void ResolveOrCreatePlaceholder_ReusesExistingIssue()
    {
        var issue = ReadingListMatcher.ResolveOrCreatePlaceholder(_context, "Kilo Station", "1");
        Assert.Equal(_issue1Id, issue.Id);
        Assert.False(issue.IsPlaceholder);
    }

    [Fact]
    public void ResolveOrCreatePlaceholder_CreatesSeriesAndPlaceholderIssueOnNoMatch()
    {
        var issue = ReadingListMatcher.ResolveOrCreatePlaceholder(_context, "Brand New Series", "5", year: 2024);

        Assert.True(issue.IsPlaceholder);
        Assert.True(issue.FileIsMissing);
        Assert.Equal("5", issue.Number);
        Assert.NotEqual(0, issue.SeriesId);

        var series = _context.Series.First(s => s.Id == issue.SeriesId);
        Assert.Equal("Brand New Series", series.Name);
    }

    [Fact]
    public void ResolveOrCreatePlaceholder_ReusesSeriesButAddsNewIssueForNewNumber()
    {
        ReadingListMatcher.ResolveOrCreatePlaceholder(_context, "Kilo Station", "99");
        Assert.Equal(1, _context.Series.Count(s => s.Name == "Kilo Station")); // no duplicate series created
    }

    /// <summary>docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md §3 - the manual "add a physical book" entry point's wasCreated overload.</summary>
    [Fact]
    public void ResolveOrCreatePlaceholder_WithWasCreatedOut_TrueForFreshSeriesAndNumber()
    {
        var issue = ReadingListMatcher.ResolveOrCreatePlaceholder(
            _context, "Another Brand New Series", "1", null, null, null, out bool wasCreated);

        Assert.True(wasCreated);
        Assert.True(issue.IsPlaceholder);
    }

    [Fact]
    public void ResolveOrCreatePlaceholder_WithWasCreatedOut_FalseWhenAnExistingIssueMatches()
    {
        var issue = ReadingListMatcher.ResolveOrCreatePlaceholder(
            _context, "Kilo Station", "1", null, null, null, out bool wasCreated);

        Assert.False(wasCreated);
        Assert.Equal(_issue1Id, issue.Id);
    }

    // --- CBL round-trip ---

    [Fact]
    public void Cbl_ExportThenImport_RoundTripsOrderAndCount()
    {
        var list = new ReadingList { Name = "Signal War" };
        list.Items.Add(new ReadingListItem { IssueId = _issue1Id, SortOrder = 0 });
        list.Items.Add(new ReadingListItem { IssueId = _issue2VolumeAId, SortOrder = 1 });
        _context.ReadingLists.Add(list);
        _context.SaveChanges();

        string cblPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_test_{Guid.NewGuid():N}.cbl");
        try
        {
            CblReadingListIO.Export(_context, list.Id, cblPath);

            // Confirms this is a real, independently-readable CBL file (not just a black-box
            // round trip) - deserialize with the same ported serializer CblReadingListIO itself
            // uses, matching CeLibraryMigratorTests' "generate/verify via the real code path"
            // precedent.
            var rawContainer = ComicReadingListContainer.Deserialize(cblPath);
            Assert.Equal("Signal War", rawContainer.Name);
            Assert.Equal(2, rawContainer.Items.Count);
            Assert.Equal("Kilo Station", rawContainer.Items[0].Series);
            Assert.Equal("1", rawContainer.Items[0].Number);

            var imported = CblReadingListIO.Import(_context, cblPath);
            Assert.Equal(2, imported.Items.Count);
            var orderedIssueIds = imported.Items.OrderBy(i => i.SortOrder).Select(i => i.IssueId).ToList();
            Assert.Equal([_issue1Id, _issue2VolumeAId], orderedIssueIds);
        }
        finally
        {
            if (File.Exists(cblPath)) File.Delete(cblPath);
        }
    }

    [Fact]
    public void Cbl_Import_CreatesPlaceholderForUnmatchedEntry()
    {
        var container = new ComicReadingListContainer { Name = "Crossover" };
        container.Items.Add(new ComicReadingListItem { Series = "Unowned Series", Number = "1", Volume = -1, Year = 2022 });

        string cblPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_test_{Guid.NewGuid():N}.cbl");
        try
        {
            container.Serialize(cblPath);

            var imported = CblReadingListIO.Import(_context, cblPath);
            Assert.Single(imported.Items);

            var issue = _context.Issues.First(i => i.Id == imported.Items[0].IssueId);
            Assert.True(issue.IsPlaceholder);
            Assert.True(issue.FileIsMissing);
        }
        finally
        {
            if (File.Exists(cblPath)) File.Delete(cblPath);
        }
    }

    // --- CSV import ---

    [Fact]
    public void Csv_Import_ParsesValidRowsAndResolvesMatches()
    {
        string csvPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_test_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(csvPath, "Series,Number,Volume,Year,Format\nKilo Station,1,,2020,\nBrand New Series,5,,2024,\n");

            var result = CsvReadingListIO.Import(_context, csvPath);

            Assert.Equal(2, result.List.Items.Count);
            Assert.Equal(1, result.OwnedCount);
            Assert.Equal(1, result.PlaceholderCount);
            Assert.Empty(result.SkippedRows);
        }
        finally
        {
            if (File.Exists(csvPath)) File.Delete(csvPath);
        }
    }

    [Fact]
    public void Csv_Import_SkipsRowsMissingRequiredColumnsWithoutFailingTheWholeImport()
    {
        string csvPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_test_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(csvPath, "Series,Number\nKilo Station,1\n,\nKilo Station,\n");

            var result = CsvReadingListIO.Import(_context, csvPath);

            Assert.Single(result.List.Items);
            Assert.Equal(2, result.SkippedRows.Count);
        }
        finally
        {
            if (File.Exists(csvPath)) File.Delete(csvPath);
        }
    }

    [Fact]
    public void Csv_Import_ThrowsWhenRequiredHeaderColumnsMissing()
    {
        string csvPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_test_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(csvPath, "Title,Issue\nSomething,1\n");
            Assert.Throws<InvalidDataException>(() => CsvReadingListIO.Import(_context, csvPath));
        }
        finally
        {
            if (File.Exists(csvPath)) File.Delete(csvPath);
        }
    }

    // --- Text export ---

    [Fact]
    public void TextExport_FormatsNumberedListWithYear()
    {
        var list = new ReadingList { Name = "Signal War" };
        list.Items.Add(new ReadingListItem { IssueId = _issue1Id, SortOrder = 0 });
        list.Items.Add(new ReadingListItem { IssueId = _issue2VolumeAId, SortOrder = 1 });
        _context.ReadingLists.Add(list);
        _context.SaveChanges();

        string text = ReadingListTextExporter.Export(_context, list.Id);

        Assert.Contains("# Signal War", text);
        Assert.Contains("2 issues", text);
        Assert.Contains("1. Kilo Station #1 (2020)", text);
        Assert.Contains("2. Kilo Station #2 (2019)", text);
    }
}
