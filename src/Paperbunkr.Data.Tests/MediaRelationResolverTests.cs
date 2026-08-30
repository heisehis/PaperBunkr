using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="MediaRelationResolver.GetRelatedSeries"/> (docs/superpowers/specs/
/// 2026-08-17-metadata-model-phase3-media-relations-design.md) against a real SQLite database -
/// same rationale as <see cref="SeriesReassignmentResolverTests"/> (Phase 2b): this reads real
/// persisted rows and FK-joined <see cref="Series"/> data, not pure in-memory objects.
/// </summary>
public class MediaRelationResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public MediaRelationResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_mediarelation_test_{Guid.NewGuid():N}.db");
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

    private (int aId, int bId) SeedTwoSeries(PaperbunkrDbContext context, string nameA = "Series A", string nameB = "Series B")
    {
        var a = new Series { Name = nameA };
        var b = new Series { Name = nameB };
        context.Series.AddRange(a, b);
        context.SaveChanges();
        return (a.Id, b.Id);
    }

    private static void AddRelation(PaperbunkrDbContext context, int sourceId, int targetId, RelationType type)
    {
        var relation = new MediaRelation { SourceSeriesId = sourceId, TargetSeriesId = targetId, RelationType = type };
        relation.Evidence.Add(new RelationEvidence { MediaRelation = relation, Provider = RelationEvidenceProvider.User, Confidence = 1.0m });
        context.MediaRelations.Add(relation);
        context.SaveChanges();
    }

    [Fact]
    public void GetRelatedSeries_NoRelations_ReturnsEmpty()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, _) = SeedTwoSeries(context);

        var result = MediaRelationResolver.GetRelatedFromSeries(context, aId);

        Assert.Empty(result);
    }

    [Fact]
    public void GetRelatedSeries_TargetSide_DisplaysStoredTypeAsIs()
    {
        // RelationType always describes the SOURCE's role. Stored as A --Prequel--> B ("A is the
        // Prequel of B"). Viewed from B (the target), A's card correctly shows "Prequel" - that's
        // literally A's role, no inversion needed.
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoSeries(context);
        AddRelation(context, aId, bId, RelationType.Prequel);

        var result = MediaRelationResolver.GetRelatedFromSeries(context, bId);

        var entry = Assert.Single(result);
        Assert.Equal(aId, entry.Series!.Id);
        Assert.Equal(RelationType.Prequel, entry.DisplayType);
    }

    [Fact]
    public void GetRelatedSeries_SourceSide_NamedInversePair_DisplaysInverseType()
    {
        // Same stored relation (A --Prequel--> B, "A is the Prequel of B"). Viewed from A (the
        // source), B's card must show B's OWN role - "Sequel" - not the stored "Prequel", which
        // would incorrectly describe B as if it were the earlier work.
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoSeries(context);
        AddRelation(context, aId, bId, RelationType.Prequel);

        var result = MediaRelationResolver.GetRelatedFromSeries(context, aId);

        var entry = Assert.Single(result);
        Assert.Equal(bId, entry.Series!.Id);
        Assert.Equal(RelationType.Sequel, entry.DisplayType);
    }

    [Fact]
    public void GetRelatedSeries_Symmetric_DisplaysSameTypeFromEitherSide()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoSeries(context);
        AddRelation(context, aId, bId, RelationType.Crossover);

        var fromA = Assert.Single(MediaRelationResolver.GetRelatedFromSeries(context, aId));
        var fromB = Assert.Single(MediaRelationResolver.GetRelatedFromSeries(context, bId));

        Assert.Equal(RelationType.Crossover, fromA.DisplayType);
        Assert.Equal(RelationType.Crossover, fromB.DisplayType);
    }

    [Fact]
    public void GetRelatedSeries_DirectionalNoNamedInverse_DisplaysSameLabelFromEitherSide_ButOtherSeriesDiffers()
    {
        // SpinOff has no named inverse in the vocabulary, so both sides show the same label - but
        // the OTHER series shown still correctly differs per side (A on B's page, B on A's page).
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoSeries(context);
        AddRelation(context, aId, bId, RelationType.SpinOff); // A is a SpinOff of B

        var fromA = Assert.Single(MediaRelationResolver.GetRelatedFromSeries(context, aId));
        var fromB = Assert.Single(MediaRelationResolver.GetRelatedFromSeries(context, bId));

        Assert.Equal(bId, fromA.Series!.Id);
        Assert.Equal(RelationType.SpinOff, fromA.DisplayType);
        Assert.Equal(aId, fromB.Series!.Id);
        Assert.Equal(RelationType.SpinOff, fromB.DisplayType);
    }

    [Fact]
    public void GetRelatedSeries_MultipleRelations_AllAppear()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoSeries(context);
        var cId = new Series { Name = "Series C" };
        context.Series.Add(cId);
        context.SaveChanges();

        AddRelation(context, aId, bId, RelationType.Crossover);
        AddRelation(context, aId, cId.Id, RelationType.Related);

        var result = MediaRelationResolver.GetRelatedFromSeries(context, aId);

        Assert.Equal(2, result.Count);
    }

    // --- TryCreate / Remove ---

    [Fact]
    public void TryCreate_ValidPair_CreatesRelationAndUserEvidence()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoSeries(context);

        bool created = MediaRelationResolver.TryCreate(context, aId, bId, RelationType.Crossover);

        Assert.True(created);
        var relation = Assert.Single(context.MediaRelations.Include(m => m.Evidence));
        Assert.Equal(aId, relation.SourceSeriesId);
        Assert.Equal(bId, relation.TargetSeriesId);
        var evidence = Assert.Single(relation.Evidence);
        Assert.Equal(RelationEvidenceProvider.User, evidence.Provider);
        Assert.Equal(1.0m, evidence.Confidence);
    }

    [Fact]
    public void TryCreate_SelfRelation_RejectedNoWrite()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, _) = SeedTwoSeries(context);

        bool created = MediaRelationResolver.TryCreate(context, aId, aId, RelationType.Related);

        Assert.False(created);
        Assert.Empty(context.MediaRelations);
    }

    [Fact]
    public void TryCreate_ExactDuplicate_RejectedNoWrite()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoSeries(context);
        AddRelation(context, aId, bId, RelationType.Crossover);

        bool created = MediaRelationResolver.TryCreate(context, aId, bId, RelationType.Crossover);

        Assert.False(created);
        Assert.Single(context.MediaRelations);
    }

    [Fact]
    public void TryCreate_DuplicateInOppositeDirection_RejectedNoWrite()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoSeries(context);
        AddRelation(context, aId, bId, RelationType.Crossover);

        bool created = MediaRelationResolver.TryCreate(context, bId, aId, RelationType.Crossover);

        Assert.False(created);
        Assert.Single(context.MediaRelations);
    }

    [Fact]
    public void TryCreate_SameSeriesPair_DifferentType_Allowed()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoSeries(context);
        AddRelation(context, aId, bId, RelationType.Crossover);

        bool created = MediaRelationResolver.TryCreate(context, aId, bId, RelationType.Related);

        Assert.True(created);
        Assert.Equal(2, context.MediaRelations.Count());
    }

    [Fact]
    public void Remove_ExistingRelation_DeletesItAndItsEvidence()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, bId) = SeedTwoSeries(context);
        AddRelation(context, aId, bId, RelationType.Crossover);
        int relationId = context.MediaRelations.Single().Id;

        MediaRelationResolver.Remove(context, relationId);

        Assert.Empty(context.MediaRelations);
        Assert.Empty(context.RelationEvidence);
    }

    [Fact]
    public void Remove_NonExistentRelation_NoOp_DoesNotThrow()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);

        MediaRelationResolver.Remove(context, 999);
    }

    // --- Collection nodes (docs/superpowers/specs/2026-08-30-media-relation-collection-nodes-
    // design.md) ---

    private int AddCollection(PaperbunkrDbContext context, string name = "Omnibus")
    {
        var collection = new Collection { Name = name };
        context.Collections.Add(collection);
        context.SaveChanges();
        return collection.Id;
    }

    [Fact]
    public void TryCreate_SeriesToCollection_CreatesRelation()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, _) = SeedTwoSeries(context);
        int collectionId = AddCollection(context);

        bool created = MediaRelationResolver.TryCreate(
            context, MediaRelationEndpointKind.Series, aId, MediaRelationEndpointKind.Collection, collectionId, RelationType.Crossover);

        Assert.True(created);
        var relation = Assert.Single(context.MediaRelations);
        Assert.Equal(aId, relation.SourceSeriesId);
        Assert.Null(relation.SourceCollectionId);
        Assert.Equal(collectionId, relation.TargetCollectionId);
        Assert.Null(relation.TargetSeriesId);
    }

    [Fact]
    public void TryCreate_CollectionToCollection_RejectedNoWrite()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int aId = AddCollection(context, "A");
        int bId = AddCollection(context, "B");

        bool created = MediaRelationResolver.TryCreate(
            context, MediaRelationEndpointKind.Collection, aId, MediaRelationEndpointKind.Collection, bId, RelationType.Related);

        Assert.False(created);
        Assert.Empty(context.MediaRelations);
    }

    [Fact]
    public void GetRelatedFromSeries_CollectionEndpoint_ReturnsCollectionKind()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, _) = SeedTwoSeries(context);
        int collectionId = AddCollection(context);
        MediaRelationResolver.TryCreate(context, MediaRelationEndpointKind.Series, aId, MediaRelationEndpointKind.Collection, collectionId, RelationType.Crossover);

        var entry = Assert.Single(MediaRelationResolver.GetRelatedFromSeries(context, aId));

        Assert.Equal(MediaRelationEndpointKind.Collection, entry.Kind);
        Assert.Equal(collectionId, entry.Collection!.Id);
        Assert.Null(entry.Series);
    }

    [Fact]
    public void GetRelatedFromCollection_SeriesEndpoint_ReturnsSeriesKind_WithDirectionalInversion()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, _) = SeedTwoSeries(context);
        int collectionId = AddCollection(context);
        // Collection is the Prequel of Series A.
        MediaRelationResolver.TryCreate(context, MediaRelationEndpointKind.Collection, collectionId, MediaRelationEndpointKind.Series, aId, RelationType.Prequel);

        var fromCollection = Assert.Single(MediaRelationResolver.GetRelatedFromCollection(context, collectionId));
        var fromSeries = Assert.Single(MediaRelationResolver.GetRelatedFromSeries(context, aId));

        Assert.Equal(MediaRelationEndpointKind.Series, fromCollection.Kind);
        Assert.Equal(aId, fromCollection.Series!.Id);
        // Viewed from the SOURCE's (Collection's) own page, the OTHER side (Series A, the target)
        // must show ITS OWN role - the inverse of "Prequel" ("Sequel") - not the stored type,
        // matching GetRelatedFromSeries's own documented convention (the real bug this class's own
        // doc comment already warns about: an early version inverted on the wrong side).
        Assert.Equal(RelationType.Sequel, fromCollection.DisplayType);

        Assert.Equal(MediaRelationEndpointKind.Collection, fromSeries.Kind);
        Assert.Equal(collectionId, fromSeries.Collection!.Id);
        // Viewed from the TARGET's (Series A's) own page, the source's (Collection's) role is the
        // stored type, as-is - it already correctly describes the source.
        Assert.Equal(RelationType.Prequel, fromSeries.DisplayType);
    }

    [Fact]
    public void TryCreate_SelfRelation_SameKindAndId_RejectedNoWrite()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int collectionId = AddCollection(context);

        bool created = MediaRelationResolver.TryCreate(
            context, MediaRelationEndpointKind.Collection, collectionId, MediaRelationEndpointKind.Collection, collectionId, RelationType.Related);

        Assert.False(created);
        Assert.Empty(context.MediaRelations);
    }

    [Fact]
    public void TryCreate_DuplicateMixedTriple_RejectedNoWrite()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, _) = SeedTwoSeries(context);
        int collectionId = AddCollection(context);
        MediaRelationResolver.TryCreate(context, MediaRelationEndpointKind.Series, aId, MediaRelationEndpointKind.Collection, collectionId, RelationType.Crossover);

        bool created = MediaRelationResolver.TryCreate(context, MediaRelationEndpointKind.Collection, collectionId, MediaRelationEndpointKind.Series, aId, RelationType.Crossover);

        Assert.False(created);
        Assert.Single(context.MediaRelations);
    }

    [Fact]
    public void Remove_SeriesToCollectionRelation_DeletesItAndItsEvidence()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, _) = SeedTwoSeries(context);
        int collectionId = AddCollection(context);
        MediaRelationResolver.TryCreate(context, MediaRelationEndpointKind.Series, aId, MediaRelationEndpointKind.Collection, collectionId, RelationType.Crossover);
        int relationId = context.MediaRelations.Single().Id;

        MediaRelationResolver.Remove(context, relationId);

        Assert.Empty(context.MediaRelations);
        Assert.Empty(context.RelationEvidence);
        Assert.NotNull(context.Collections.Find(collectionId)); // deleting the relation doesn't delete the collection
    }

    [Fact]
    public void DeletingCollection_CascadesItsMediaRelations()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var (aId, _) = SeedTwoSeries(context);
        int collectionId = AddCollection(context);
        MediaRelationResolver.TryCreate(context, MediaRelationEndpointKind.Series, aId, MediaRelationEndpointKind.Collection, collectionId, RelationType.Crossover);

        context.Collections.Remove(context.Collections.Find(collectionId)!);
        context.SaveChanges();

        Assert.Empty(context.MediaRelations);
    }
}
