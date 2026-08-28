using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>MetadataModelPhase4dEventRelations</c> migration against a real SQLite database
/// carrying pre-migration data (docs/superpowers/specs/2026-08-27-metadata-model-phase4d-event-
/// relations-design.md) - purely additive schema (two new tables, no existing-column changes),
/// following 2a/3/4a/4b's precedent.
/// </summary>
public class MetadataModelPhase4dMigrationTests : IDisposable
{
    private const string PriorMigration = "20260827123120_AddBooksBrowseState";
    private readonly string _dbPath;

    public MetadataModelPhase4dMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_phase4d_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_PreservesExistingRows_AddsEmptyEventRelationTables_AndCascadesBothDirections()
    {
        int keptEventId;

        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            context.Database.ExecuteSql($"INSERT INTO StoryEvents (Name, CreatedAt, UpdatedAt) VALUES ('Existing Event', '2026-01-01', '2026-01-01');");
            migrator.Migrate();
        }

        int relationId;
        using (var context = CreateContext())
        {
            // Pre-existing StoryEvent row untouched; new tables exist and start empty.
            var existing = Assert.Single(context.StoryEvents);
            Assert.Equal("Existing Event", existing.Name);
            keptEventId = existing.Id;
            Assert.Empty(context.EventRelations);
            Assert.Empty(context.EventRelationEvidence);

            var other = new StoryEvent { Name = "Other Event", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            context.StoryEvents.Add(other);
            context.SaveChanges();

            var relation = new EventRelation { SourceEventId = keptEventId, TargetEventId = other.Id, RelationType = RelationType.Crossover };
            relation.Evidence.Add(new EventRelationEvidence { EventRelation = relation, Provider = RelationEvidenceProvider.User, Confidence = 1.0m });
            context.EventRelations.Add(relation);
            context.SaveChanges();
            relationId = relation.Id;
        }

        using (var context = CreateContext())
        {
            // Removing an EventRelation cascades to its evidence.
            context.EventRelations.Remove(context.EventRelations.Single(r => r.Id == relationId));
            context.SaveChanges();
            Assert.Empty(context.EventRelations);
            Assert.Empty(context.EventRelationEvidence);
        }

        using (var context = CreateContext())
        {
            var kept = context.StoryEvents.Single(e => e.Id == keptEventId);
            var other = context.StoryEvents.Single(e => e.Name == "Other Event");
            var relation = new EventRelation { SourceEventId = kept.Id, TargetEventId = other.Id, RelationType = RelationType.Sequel };
            relation.Evidence.Add(new EventRelationEvidence { EventRelation = relation, Provider = RelationEvidenceProvider.User, Confidence = 1.0m });
            context.EventRelations.Add(relation);
            context.SaveChanges();

            // Deleting a StoryEvent cascades any EventRelation referencing it (either endpoint).
            context.StoryEvents.Remove(other);
            context.SaveChanges();
            Assert.Empty(context.EventRelations);
            Assert.Empty(context.EventRelationEvidence);
            Assert.NotNull(context.StoryEvents.Find(keptEventId));
        }
    }
}
