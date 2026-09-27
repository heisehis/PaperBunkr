using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddScrapePermanentlySkipped</c> migration (docs/superpowers/specs/2026-09-24-
/// comicvine-scraper-fidelity-plan.md Step 16) applies cleanly, <see cref="Issue.ScrapePermanentlySkipped"/>
/// round-trips, and defaults to false for a pre-existing row. A full down-migrate is deliberately not
/// tested here, same reasoning as <see cref="AddMetronScrapeFieldsMigrationTests"/>.
/// </summary>
public class AddScrapePermanentlySkippedMigrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public AddScrapePermanentlySkippedMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_scrapepermskip_mig_{Guid.NewGuid():N}.db");
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
    public void FullMigrate_ScrapePermanentlySkipped_DefaultsFalse_AndRoundTrips()
    {
        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            ctx.Database.Migrate();
        }

        int existingId;
        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Batman" };
            var issue = new Issue { Series = series, Number = "1", FilePath = "C:/x/batman1.cbz" };
            ctx.Issues.Add(issue);
            ctx.SaveChanges();
            existingId = issue.Id;
        }

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            Assert.False(ctx.Issues.Single(i => i.Id == existingId).ScrapePermanentlySkipped);
            ctx.Issues.Single(i => i.Id == existingId).ScrapePermanentlySkipped = true;
            ctx.SaveChanges();
        }

        using (var ctx = new PaperbunkrDbContext(_dbOptions))
        {
            Assert.True(ctx.Issues.Single(i => i.Id == existingId).ScrapePermanentlySkipped);
        }
    }
}
