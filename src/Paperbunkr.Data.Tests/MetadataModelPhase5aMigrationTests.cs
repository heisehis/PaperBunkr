using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>MetadataModelPhase5aExternalMetadataSchema</c> migration against a real SQLite
/// database carrying pre-migration data (docs/superpowers/specs/2026-08-17-metadata-model-phase5a-
/// external-metadata-schema-design.md) - purely additive schema (three new tables, no existing-
/// column changes), following 4a/4b/4c's precedent.
/// </summary>
public class MetadataModelPhase5aMigrationTests : IDisposable
{
    private const string PriorMigration = "20260817171759_MetadataModelPhase4cReadingListOverhaul";
    private readonly string _dbPath;

    public MetadataModelPhase5aMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_phase5a_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_PreservesExistingRows_AddsEmptyExternalMetadataTables_AndCascadeDeleteWorks()
    {
        int seriesId;

        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            context.Database.ExecuteSql($"INSERT INTO Series (Name, ContentType, ReadingMode, Status) VALUES ('Series A', 'Unknown', 'LeftToRight', 'Unknown');");
            migrator.Migrate();
        }

        using (var context = CreateContext())
        {
            var series = context.Series.Single(s => s.Name == "Series A");
            seriesId = series.Id;

            Assert.Empty(context.ExternalMediaIds);
            Assert.Empty(context.ExternalMetadataSnapshots);
            Assert.Empty(context.ExternalRatings);

            context.ExternalMediaIds.Add(new ExternalMediaId { SeriesId = seriesId, Provider = ExternalMetadataProvider.AniList, ExternalId = "12345" });
            context.ExternalRatings.Add(new ExternalRating { SeriesId = seriesId, Provider = ExternalMetadataProvider.AniList, Value = 8.2m, Scale = 10m, RetrievedAt = DateTime.UtcNow });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Single(context.ExternalMediaIds);
            Assert.Single(context.ExternalRatings);

            context.Series.Remove(context.Series.Single(s => s.Id == seriesId));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Empty(context.ExternalMediaIds);
            Assert.Empty(context.ExternalRatings);
        }
    }
}
