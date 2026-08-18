using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>MetadataModelPhase3MediaRelations</c> migration against a real SQLite database
/// carrying pre-migration data (docs/superpowers/specs/2026-08-17-metadata-model-phase3-media-
/// relations-design.md) - purely additive schema (two new tables, no existing-column changes),
/// following 2a/2b's precedent.
/// </summary>
public class MetadataModelPhase3MigrationTests : IDisposable
{
    private const string PriorMigration = "20260817121927_MetadataModelPhase2aMetadataProposals";
    private readonly string _dbPath;

    public MetadataModelPhase3MigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_phase3_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_PreservesExistingRows_AddsEmptyMediaRelationTables_AndCascadeDeleteWorks()
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

            // Pre-existing rows untouched, new tables exist and start empty.
            Assert.Empty(context.MediaRelations);
            Assert.Empty(context.RelationEvidence);

            var relation = new MediaRelation { SourceSeriesId = seriesAId, TargetSeriesId = seriesBId, RelationType = RelationType.Crossover };
            relation.Evidence.Add(new RelationEvidence { MediaRelation = relation, Provider = RelationEvidenceProvider.User, Confidence = 1.0m });
            context.MediaRelations.Add(relation);
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var relation = Assert.Single(context.MediaRelations.Include(m => m.Evidence));
            Assert.Equal(RelationType.Crossover, relation.RelationType);
            Assert.Single(relation.Evidence);

            // Cascade delete: removing the source series removes the relation and its evidence too.
            context.Series.Remove(context.Series.Single(s => s.Id == seriesAId));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Empty(context.MediaRelations);
            Assert.Empty(context.RelationEvidence);
            Assert.Single(context.Series); // Series B survives
        }
    }
}
