using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>Schema and behavior of the acquisition tables (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §4).</summary>
public class AcquisitionEntityTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    public AcquisitionEntityTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_acquisition_test_{Guid.NewGuid():N}.db");
        _context = Create(_dbPath);
        _context.Database.EnsureCreated();
    }

    private static PaperbunkrDbContext Create(string path) =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={path};Foreign Keys=True").Options);

    public void Dispose()
    {
        _context.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private WatchedSeries AddWatched(int volumeId = 4050, int? seriesId = null)
    {
        var watched = new WatchedSeries { ExternalVolumeId = volumeId, Name = "Spawn", SeriesId = seriesId, AddedAt = DateTime.UtcNow };
        _context.WatchedSeries.Add(watched);
        _context.SaveChanges();
        return watched;
    }

    [Fact]
    public void WantedIssue_StatusIsStoredAsItsName_AndRoundTrips()
    {
        var watched = AddWatched();
        _context.WantedIssues.Add(new WantedIssue
        {
            WatchedSeriesId = watched.Id, ExternalIssueId = 1, IssueNumber = "263",
            Status = WantedIssueStatus.Snatched, CreatedAt = DateTime.UtcNow,
        });
        _context.SaveChanges();

        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status FROM WantedIssues";
        Assert.Equal("Snatched", command.ExecuteScalar());

        _context.ChangeTracker.Clear();
        Assert.Equal(WantedIssueStatus.Snatched, _context.WantedIssues.Single().Status);
    }

    [Fact]
    public void WantedIssue_NewRowsDefaultToWanted()
    {
        Assert.Equal(WantedIssueStatus.Wanted, new WantedIssue().Status);
    }

    [Fact]
    public void ComicVineVolumeId_IsUnique()
    {
        AddWatched(volumeId: 7);
        _context.WatchedSeries.Add(new WatchedSeries { ExternalVolumeId = 7, Name = "Dup", AddedAt = DateTime.UtcNow });

        Assert.Throws<DbUpdateException>(() => _context.SaveChanges());
    }

    [Fact]
    public void ComicVineIssueId_IsUnique_ForWantedIssues()
    {
        var watched = AddWatched();
        _context.WantedIssues.Add(new WantedIssue { WatchedSeriesId = watched.Id, ExternalIssueId = 9, IssueNumber = "1", CreatedAt = DateTime.UtcNow });
        _context.SaveChanges();
        _context.WantedIssues.Add(new WantedIssue { WatchedSeriesId = watched.Id, ExternalIssueId = 9, IssueNumber = "1", CreatedAt = DateTime.UtcNow });

        Assert.Throws<DbUpdateException>(() => _context.SaveChanges());
    }

    [Fact]
    public void DeletingAWatchedSeries_CascadesCatalogWantedAndCandidates()
    {
        var watched = AddWatched();
        var wanted = new WantedIssue { WatchedSeriesId = watched.Id, ExternalIssueId = 1, IssueNumber = "1", CreatedAt = DateTime.UtcNow };
        wanted.Candidates.Add(new ReleaseCandidate { Title = "Spawn 001", DownloadUrl = "magnet:?xt=urn:btih:abc", FoundAt = DateTime.UtcNow });
        _context.WantedIssues.Add(wanted);
        _context.CatalogIssues.Add(new CatalogIssue { WatchedSeriesId = watched.Id, ExternalIssueId = 2, IssueNumber = "2" });
        _context.SaveChanges();

        _context.WatchedSeries.Remove(watched);
        _context.SaveChanges();

        Assert.Empty(_context.CatalogIssues);
        Assert.Empty(_context.WantedIssues);
        Assert.Empty(_context.ReleaseCandidates);
    }

    [Fact]
    public void DeletingTheLocalSeries_ClearsTheLinkButKeepsTheWatchedSeries()
    {
        var series = new Series { Name = "Spawn" };
        _context.Series.Add(series);
        _context.SaveChanges();
        AddWatched(seriesId: series.Id);

        _context.Series.Remove(series);
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var watched = _context.WatchedSeries.Single();
        Assert.Null(watched.SeriesId);
    }

    [Fact]
    public void DeletingTheImportedIssue_ClearsWantedIssueLink_ButKeepsTheRow()
    {
        var series = new Series { Name = "Spawn" };
        var issue = new Issue { Series = series, Number = "1", FilePath = "C:\\x\\1.cbz" };
        _context.Issues.Add(issue);
        _context.SaveChanges();
        var watched = AddWatched(seriesId: series.Id);
        _context.WantedIssues.Add(new WantedIssue
        {
            WatchedSeriesId = watched.Id, ExternalIssueId = 1, IssueNumber = "1", IssueId = issue.Id,
            Status = WantedIssueStatus.Imported, CreatedAt = DateTime.UtcNow,
        });
        _context.SaveChanges();

        _context.Issues.Remove(issue);
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        Assert.Null(_context.WantedIssues.Single().IssueId);
    }

    [Fact]
    public void AcquisitionSettings_IsASingleton_WithSafeDefaults()
    {
        var first = _context.GetOrCreateAcquisitionSettings();
        var second = _context.GetOrCreateAcquisitionSettings();

        Assert.Equal(1, first.Id);
        Assert.Same(first, second);
        Assert.Equal(1, _context.AcquisitionSettings.Count());
        Assert.False(first.Enabled);                       // nothing contacts an indexer until the user opts in
        Assert.Equal("paperbunkr-comics", first.QBittorrentCategory);
        Assert.Equal(60, first.PollIntervalMinutes);
    }

    [Fact]
    public void Migrate_OnAFreshDatabase_CreatesTheAcquisitionTables()
    {
        var path = Path.Combine(Path.GetTempPath(), $"paperbunkr_acquisition_migrate_{Guid.NewGuid():N}.db");
        try
        {
            using (var context = Create(path))
            {
                context.Database.Migrate();
            }

            using var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();
            foreach (var table in new[] { "WatchedSeries", "CatalogIssues", "WantedIssues", "ReleaseCandidates", "AcquisitionSettings" })
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
                command.Parameters.AddWithValue("$name", table);
                Assert.Equal(1L, command.ExecuteScalar());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }
}
