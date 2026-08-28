using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="EventRelationResolver"/> (docs/superpowers/specs/2026-08-27-metadata-model-
/// phase4d-event-relations-design.md) against a real SQLite database - same rationale as
/// <see cref="MediaRelationResolverTests"/>, whose shape this resolver mirrors.
/// </summary>
public class EventRelationResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public EventRelationResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_eventrelation_test_{Guid.NewGuid():N}.db");
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

    private static (int aId, int bId) SeedTwoEvents(PaperbunkrDbContext context, string nameA = "Secret Wars (1984)", string nameB = "Secret Wars (2015)")
    {
        var a = new StoryEvent { Name = nameA, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var b = new StoryEvent { Name = nameB, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.StoryEvents.AddRange(a, b);
        context.SaveChanges();
        return (a.Id, b.Id);
    }

    [Fact]
    public void GetRelatedEvents_NoRelations_ReturnsEmpty()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, _) = SeedTwoEvents(context);

        Assert.Empty(EventRelationResolver.GetRelatedEvents(context, aId));
    }

    [Fact]
    public void GetRelatedEvents_SourceSide_DisplaysStoredTypeAsIs()
    {
        // Stored as A --Prequel--> B. Queried from A (the source), B's card shows the stored type.
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoEvents(context);
        EventRelationResolver.TryCreate(context, aId, bId, RelationType.Prequel);

        var entry = Assert.Single(EventRelationResolver.GetRelatedEvents(context, aId));
        Assert.Equal(bId, entry.OtherEvent.Id);
        Assert.Equal(RelationType.Prequel, entry.DisplayType);
    }

    [Fact]
    public void GetRelatedEvents_TargetSide_NamedInversePair_DisplaysInverseType()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoEvents(context);
        EventRelationResolver.TryCreate(context, aId, bId, RelationType.Prequel);

        var entry = Assert.Single(EventRelationResolver.GetRelatedEvents(context, bId));
        Assert.Equal(aId, entry.OtherEvent.Id);
        Assert.Equal(RelationType.Sequel, entry.DisplayType);
    }

    [Fact]
    public void GetRelatedEvents_SymmetricType_DisplaysSameFromEitherSide()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoEvents(context);
        EventRelationResolver.TryCreate(context, aId, bId, RelationType.Crossover);

        var fromA = Assert.Single(EventRelationResolver.GetRelatedEvents(context, aId));
        var fromB = Assert.Single(EventRelationResolver.GetRelatedEvents(context, bId));
        Assert.Equal(RelationType.Crossover, fromA.DisplayType);
        Assert.Equal(RelationType.Crossover, fromB.DisplayType);
    }

    [Fact]
    public void TryCreate_ValidPair_CreatesRelationAndUserEvidence()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoEvents(context);

        bool created = EventRelationResolver.TryCreate(context, aId, bId, RelationType.Crossover);

        Assert.True(created);
        var relation = Assert.Single(context.EventRelations.Include(r => r.Evidence));
        var evidence = Assert.Single(relation.Evidence);
        Assert.Equal(RelationEvidenceProvider.User, evidence.Provider);
        Assert.Equal(1.0m, evidence.Confidence);
    }

    [Fact]
    public void TryCreate_SelfRelation_RejectedNoWrite()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, _) = SeedTwoEvents(context);

        Assert.False(EventRelationResolver.TryCreate(context, aId, aId, RelationType.Related));
        Assert.Empty(context.EventRelations);
    }

    [Fact]
    public void TryCreate_ExactDuplicate_EitherDirection_RejectedNoWrite()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoEvents(context);
        EventRelationResolver.TryCreate(context, aId, bId, RelationType.Crossover);

        Assert.False(EventRelationResolver.TryCreate(context, aId, bId, RelationType.Crossover));
        Assert.False(EventRelationResolver.TryCreate(context, bId, aId, RelationType.Crossover));
        Assert.Single(context.EventRelations);
    }

    [Fact]
    public void Remove_ExistingRelation_DeletesItAndItsEvidence()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoEvents(context);
        EventRelationResolver.TryCreate(context, aId, bId, RelationType.Crossover);
        int relationId = context.EventRelations.Single().Id;

        EventRelationResolver.Remove(context, relationId);

        Assert.Empty(context.EventRelations);
        Assert.Empty(context.EventRelationEvidence);
    }

    [Fact]
    public void Remove_AlreadyGoneId_NoOp_DoesNotThrow()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        EventRelationResolver.Remove(context, 999);
    }

    [Fact]
    public void DeletingStoryEvent_CascadesEventRelations()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoEvents(context);
        EventRelationResolver.TryCreate(context, aId, bId, RelationType.Crossover);

        context.StoryEvents.Remove(context.StoryEvents.Single(e => e.Id == bId));
        context.SaveChanges();

        Assert.Empty(context.EventRelations);
        Assert.Empty(context.EventRelationEvidence);
        Assert.NotNull(context.StoryEvents.Find(aId));
    }
}
