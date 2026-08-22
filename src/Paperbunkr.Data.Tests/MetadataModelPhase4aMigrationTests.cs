using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>MetadataModelPhase4aContinuity</c> migration against a real SQLite database
/// carrying pre-migration data (docs/superpowers/specs/2026-08-17-metadata-model-phase4a-
/// continuity-design.md) - purely additive schema (one new table plus the implicit skip-navigation
/// join table, no existing-column changes), following 2a/2b/3's precedent.
/// </summary>
public class MetadataModelPhase4aMigrationTests : IDisposable
{
    private const string PriorMigration = "20260817153929_MetadataModelPhase3MediaRelations";
    private readonly string _dbPath;

    public MetadataModelPhase4aMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_phase4a_migration_test_{Guid.NewGuid():N}.db");
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

    private PaperbunkrDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        return new PaperbunkrDbContext(options);
    }

    [Fact]
    public void Migration_PreservesExistingRows_AddsEmptyContinuityTable_AndCascadeDeleteWorksBothDirections()
    {
        int seriesAId;
        int seriesBId;

        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            context.Database.ExecuteSql($"INSERT INTO Series (Name, ContentType, ReadingMode, Status) VALUES ('Series A', 'Unknown', 'LeftToRight', 'Unknown');");
            context.Database.ExecuteSql($"INSERT INTO Series (Name, ContentType, ReadingMode, Status) VALUES ('Series B', 'Unknown', 'LeftToRight', 'Unknown');");

            migrator.Migrate();
        }

        using (var context = CreateContext())
        {
            var seriesA = context.Series.Single(s => s.Name == "Series A");
            var seriesB = context.Series.Single(s => s.Name == "Series B");
            seriesAId = seriesA.Id;
            seriesBId = seriesB.Id;

            // Pre-existing rows untouched, new table exists and starts empty.
            Assert.Empty(context.Continuities);

            var continuity = new Continuity { Name = "Earth-616", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            continuity.Series.Add(seriesA);
            continuity.Series.Add(seriesB);
            context.Continuities.Add(continuity);
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var continuity = Assert.Single(context.Continuities.Include(c => c.Series));
            Assert.Equal(2, continuity.Series.Count);

            // Cascade delete (Series side): removing a member series clears its join row without
            // deleting the Continuity row or the other member.
            context.Series.Remove(context.Series.Single(s => s.Id == seriesAId));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var continuity = Assert.Single(context.Continuities.Include(c => c.Series));
            var remaining = Assert.Single(continuity.Series);
            Assert.Equal(seriesBId, remaining.Id);

            // Cascade delete (Continuity side): removing the continuity clears its join rows
            // without deleting the remaining series.
            context.Continuities.Remove(continuity);
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Empty(context.Continuities);
            Assert.Single(context.Series); // Series B survives
        }
    }
}
