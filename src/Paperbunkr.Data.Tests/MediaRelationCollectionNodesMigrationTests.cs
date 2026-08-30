using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>MediaRelationCollectionNodes</c> migration's zero-data-loss shape
/// (docs/superpowers/specs/2026-08-30-media-relation-collection-nodes-design.md): every pre-
/// existing Series↔Series <see cref="MediaRelation"/> row keeps its FKs untouched after
/// <see cref="MediaRelation.SourceSeriesId"/>/<see cref="MediaRelation.TargetSeriesId"/> relax to
/// nullable and the two new Collection FK columns + CHECK constraints are added.
/// </summary>
public class MediaRelationCollectionNodesMigrationTests : IDisposable
{
    private const string PriorMigration = "20260829235157_AddSmartCollections";
    private readonly string _dbPath;

    public MediaRelationCollectionNodesMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_mediarelation_collectionnodes_migration_{Guid.NewGuid():N}.db");
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
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        return new PaperbunkrDbContext(options);
    }

    [Fact]
    public void Migration_PreservesExistingSeriesToSeriesRelation_AndAllowsNewCollectionSidedOnes()
    {
        int existingRelationId;
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            context.Database.ExecuteSqlRaw("INSERT INTO Series (Id, Name, SortName, ContentType, ReadingMode, Status, ReadingStatus) VALUES (1, 'Alpha', NULL, 'Comic', 'LeftToRight', 'Completed', 'Unknown');");
            context.Database.ExecuteSqlRaw("INSERT INTO Series (Id, Name, SortName, ContentType, ReadingMode, Status, ReadingStatus) VALUES (2, 'Beta', NULL, 'Comic', 'LeftToRight', 'Completed', 'Unknown');");
            context.Database.ExecuteSqlRaw("INSERT INTO MediaRelations (Id, SourceSeriesId, TargetSeriesId, RelationType, CreatedAt) VALUES (1, 1, 2, 'Crossover', '2026-01-01 00:00:00');");

            migrator.Migrate();
            existingRelationId = 1;
        }

        using (var context = CreateContext())
        {
            var existing = context.MediaRelations.Single(m => m.Id == existingRelationId);
            Assert.Equal(1, existing.SourceSeriesId);
            Assert.Equal(2, existing.TargetSeriesId);
            Assert.Null(existing.SourceCollectionId);
            Assert.Null(existing.TargetCollectionId);
            Assert.Equal(RelationType.Crossover, existing.RelationType);

            // The new CHECK constraints and Collection FK actually took effect post-migration.
            int collectionId;
            var collection = new Collection { Name = "Omnibus" };
            context.Collections.Add(collection);
            context.SaveChanges();
            collectionId = collection.Id;

            bool created = MediaRelationResolver.TryCreate(
                context, MediaRelationEndpointKind.Series, 1, MediaRelationEndpointKind.Collection, collectionId, RelationType.Related);

            Assert.True(created);
            Assert.Equal(2, context.MediaRelations.Count());
        }
    }
}
