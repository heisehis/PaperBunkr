using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="CollectionRelationResolver"/> - byte-for-byte the same coverage as
/// <see cref="MediaRelationResolverTests"/>, since this is a direct clone of that resolver for
/// Collection-to-Collection relations rather than Series-to-Series ones.
/// </summary>
public class CollectionRelationResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public CollectionRelationResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_collectionrelation_test_{Guid.NewGuid():N}.db");
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

    private static (int aId, int bId) SeedTwoCollections(PaperbunkrDbContext context, string nameA = "Collection A", string nameB = "Collection B")
    {
        var a = CollectionService.Create(context, nameA);
        var b = CollectionService.Create(context, nameB);
        return (a.Id, b.Id);
    }

    private static void AddRelation(PaperbunkrDbContext context, int sourceId, int targetId, RelationType type)
    {
        context.CollectionRelations.Add(new CollectionRelation { SourceCollectionId = sourceId, TargetCollectionId = targetId, RelationType = type });
        context.SaveChanges();
    }

    [Fact]
    public void GetRelatedCollections_NoRelations_ReturnsEmpty()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, _) = SeedTwoCollections(context);

        var result = CollectionRelationResolver.GetRelatedCollections(context, aId);

        Assert.Empty(result);
    }

    [Fact]
    public void GetRelatedCollections_TargetSide_DisplaysStoredTypeAsIs()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoCollections(context);
        AddRelation(context, aId, bId, RelationType.Prequel);

        var result = CollectionRelationResolver.GetRelatedCollections(context, bId);

        var entry = Assert.Single(result);
        Assert.Equal(aId, entry.OtherCollection.Id);
        Assert.Equal(RelationType.Prequel, entry.DisplayType);
    }

    [Fact]
    public void GetRelatedCollections_SourceSide_NamedInversePair_DisplaysInverseType()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoCollections(context);
        AddRelation(context, aId, bId, RelationType.Prequel);

        var result = CollectionRelationResolver.GetRelatedCollections(context, aId);

        var entry = Assert.Single(result);
        Assert.Equal(bId, entry.OtherCollection.Id);
        Assert.Equal(RelationType.Sequel, entry.DisplayType);
    }

    [Fact]
    public void GetRelatedCollections_Symmetric_DisplaysSameTypeFromEitherSide()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoCollections(context);
        AddRelation(context, aId, bId, RelationType.SameUniverse);

        var fromA = Assert.Single(CollectionRelationResolver.GetRelatedCollections(context, aId));
        var fromB = Assert.Single(CollectionRelationResolver.GetRelatedCollections(context, bId));

        Assert.Equal(RelationType.SameUniverse, fromA.DisplayType);
        Assert.Equal(RelationType.SameUniverse, fromB.DisplayType);
    }

    [Fact]
    public void GetRelatedCollections_MultipleRelations_AllAppear()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoCollections(context);
        var c = CollectionService.Create(context, "Collection C");

        AddRelation(context, aId, bId, RelationType.Crossover);
        AddRelation(context, aId, c.Id, RelationType.Related);

        var result = CollectionRelationResolver.GetRelatedCollections(context, aId);

        Assert.Equal(2, result.Count);
    }

    // --- TryCreate / Remove ---

    [Fact]
    public void TryCreate_ValidPair_CreatesRelation()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoCollections(context);

        bool created = CollectionRelationResolver.TryCreate(context, aId, bId, RelationType.Crossover);

        Assert.True(created);
        var relation = Assert.Single(context.CollectionRelations);
        Assert.Equal(aId, relation.SourceCollectionId);
        Assert.Equal(bId, relation.TargetCollectionId);
    }

    [Fact]
    public void TryCreate_SelfRelation_RejectedNoWrite()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, _) = SeedTwoCollections(context);

        bool created = CollectionRelationResolver.TryCreate(context, aId, aId, RelationType.Related);

        Assert.False(created);
        Assert.Empty(context.CollectionRelations);
    }

    [Fact]
    public void TryCreate_ExactDuplicate_RejectedNoWrite()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoCollections(context);
        AddRelation(context, aId, bId, RelationType.Crossover);

        bool created = CollectionRelationResolver.TryCreate(context, aId, bId, RelationType.Crossover);

        Assert.False(created);
        Assert.Single(context.CollectionRelations);
    }

    [Fact]
    public void TryCreate_DuplicateInOppositeDirection_RejectedNoWrite()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoCollections(context);
        AddRelation(context, aId, bId, RelationType.Crossover);

        bool created = CollectionRelationResolver.TryCreate(context, bId, aId, RelationType.Crossover);

        Assert.False(created);
        Assert.Single(context.CollectionRelations);
    }

    [Fact]
    public void TryCreate_SameCollectionPair_DifferentType_Allowed()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoCollections(context);
        AddRelation(context, aId, bId, RelationType.Crossover);

        bool created = CollectionRelationResolver.TryCreate(context, aId, bId, RelationType.Related);

        Assert.True(created);
        Assert.Equal(2, context.CollectionRelations.Count());
    }

    [Fact]
    public void Remove_ExistingRelation_DeletesIt()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoCollections(context);
        AddRelation(context, aId, bId, RelationType.Crossover);
        int relationId = context.CollectionRelations.Single().Id;

        CollectionRelationResolver.Remove(context, relationId);

        Assert.Empty(context.CollectionRelations);
    }

    [Fact]
    public void Remove_NonExistentRelation_NoOp_DoesNotThrow()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);

        CollectionRelationResolver.Remove(context, 999);
    }

    [Fact]
    public void DeletingACollection_CascadesToItsRelations()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoCollections(context);
        AddRelation(context, aId, bId, RelationType.Crossover);

        CollectionService.Delete(context, aId);

        Assert.Empty(context.CollectionRelations);
    }
}
