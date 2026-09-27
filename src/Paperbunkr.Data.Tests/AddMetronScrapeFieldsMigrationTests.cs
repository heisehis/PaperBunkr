using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddMetronScrapeFields</c> migration (docs/superpowers/specs/2026-09-23-metron-api-
/// utilization-design.md) applies cleanly and <see cref="Continuity.MetronId"/> round-trips. A full
/// down-migrate is deliberately not tested here, same reasoning as <see cref="AddReadingGoalsMigrationTests"/>.
/// </summary>
public class AddMetronScrapeFieldsMigrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public AddMetronScrapeFieldsMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_metronscrape_mig_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
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

    [Fact]
    public void FullMigrate_ContinuityMetronId_RoundTrips()
    {
        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            ctx.Database.Migrate();
        }

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            ctx.Continuities.Add(new Continuity { Name = "Earth-616", MetronId = "21", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            ctx.SaveChanges();
        }

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            Assert.Equal("21", ctx.Continuities.Single().MetronId);
        }
    }
}
