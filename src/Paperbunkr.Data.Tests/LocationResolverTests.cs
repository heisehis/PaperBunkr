using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="LocationResolver"/> (docs/superpowers/specs/2026-09-23-metron-api-utilization-
/// design.md) against a real SQLite database.
/// </summary>
public class LocationResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public LocationResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_location_test_{Guid.NewGuid():N}.db");
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

    private static int SeedIssue(PaperbunkrDbContext context, string seriesName, string? locations)
    {
        var series = new Series { Name = seriesName };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", Locations = locations };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    [Fact]
    public void SyncFromIssue_MaterializesAppearances()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context, "Batman", "Gotham City, Arkham Asylum");

        LocationResolver.SyncFromIssue(context, issueId);

        Assert.Equal(2, context.LocationAppearances.Count(a => a.IssueId == issueId));
        Assert.Equal(2, context.Locations.Count());
    }

    [Fact]
    public void SyncFromIssue_RemovesStale_AndPrunesOrphanLocations()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context, "Batman", "Gotham City, Arkham Asylum");
        LocationResolver.SyncFromIssue(context, issueId);

        var issue = context.Issues.Single(i => i.Id == issueId);
        issue.Locations = "Gotham City";
        context.SaveChanges();
        LocationResolver.SyncFromIssue(context, issueId);

        Assert.Single(context.LocationAppearances.Where(a => a.IssueId == issueId));
        Assert.Single(context.Locations);
        Assert.Equal("Gotham City", context.Locations.Single().Name);
    }
}
