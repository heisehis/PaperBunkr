using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="RecommendationResolver"/> (docs/superpowers/specs/2026-08-18-metadata-
/// model-phase6a-recommendation-engine-design.md) against a real SQLite database - same rationale
/// as <see cref="MediaRelationResolverTests"/>/<see cref="ContinuityResolverTests"/>.
/// </summary>
public class RecommendationResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public RecommendationResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_recommendation_test_{Guid.NewGuid():N}.db");
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

    private static int SeedSeries(PaperbunkrDbContext context, string name, string? genre = null)
    {
        var series = new Series { Name = name, Genre = genre };
        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    private static int SeedIssue(PaperbunkrDbContext context, int seriesId, string? characters = null, string? writer = null, string? tags = null)
    {
        var issue = new Issue { SeriesId = seriesId, Characters = characters, Writer = writer };
        if (tags is not null)
        {
            issue.MergeFrom(IssueTagField.Tags, new[] { tags });
        }

        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    [Fact]
    public void GetRecommendations_DirectMediaRelation_AppearsWithMatchingReasonAndEvidenceConfidence()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int sourceId = SeedSeries(context, "Source Series");
        int targetId = SeedSeries(context, "Target Series");
        // sourceId is stored as the Prequel of targetId - viewed from sourceId (per
        // MediaRelationResolver's own directional-inversion rule), the target's own role is the
        // inverse: targetId is our Sequel, i.e. "read this next."
        MediaRelationResolver.TryCreate(context, sourceId, targetId, RelationType.Prequel);

        var results = RecommendationResolver.GetRecommendations(context, sourceId);

        var candidate = Assert.Single(results);
        Assert.Equal(targetId, candidate.TargetSeriesId);
        Assert.Equal(RecommendationReason.Sequel, candidate.ReasonType);
        Assert.Equal(1.0m, candidate.Signals.RelationScore); // user-created relation is always 1.0 confidence
        Assert.Contains("sequel", candidate.Explanation);
    }

    [Fact]
    public void GetRecommendations_SharedContinuityOnly_ReasonIsSameContinuity_RelationScoreZero()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int sourceId = SeedSeries(context, "Source Series");
        int targetId = SeedSeries(context, "Target Series");
        var continuity = ContinuityResolver.GetOrCreate(context, "Earth-616");
        ContinuityResolver.AddSeriesToContinuity(context, sourceId, continuity.Id);
        ContinuityResolver.AddSeriesToContinuity(context, targetId, continuity.Id);

        var results = RecommendationResolver.GetRecommendations(context, sourceId);

        var candidate = Assert.Single(results);
        Assert.Equal(RecommendationReason.SameContinuity, candidate.ReasonType);
        Assert.Equal(0m, candidate.Signals.RelationScore);
        Assert.Equal(1.0m, candidate.Signals.ContinuityScore);
    }

    [Fact]
    public void GetRecommendations_SharedStoryEventOnly_ReasonIsSameEvent()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int sourceId = SeedSeries(context, "Source Series");
        int targetId = SeedSeries(context, "Target Series");
        int sourceIssueId = SeedIssue(context, sourceId);
        int targetIssueId = SeedIssue(context, targetId);
        var storyEvent = new StoryEvent { Name = "Crisis Event" };
        context.StoryEvents.Add(storyEvent);
        context.SaveChanges();
        EventMembershipResolver.AddMember(context, storyEvent.Id, sourceIssueId, EventMembershipRole.Core);
        EventMembershipResolver.AddMember(context, storyEvent.Id, targetIssueId, EventMembershipRole.TieIn);

        var results = RecommendationResolver.GetRecommendations(context, sourceId);

        var candidate = Assert.Single(results);
        Assert.Equal(RecommendationReason.SameEvent, candidate.ReasonType);
        Assert.Equal(1.0m, candidate.Signals.EventScore);
    }

    [Fact]
    public void GetRecommendations_SharedCollectionOnly_ReasonIsSameCollection()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int sourceId = SeedSeries(context, "Source Series");
        int targetId = SeedSeries(context, "Target Series");
        var collection = Paperbunkr.Data.Collections.CollectionService.Create(context, "Favorites");
        Paperbunkr.Data.Collections.CollectionService.AddItems(context, collection.Id, seriesIds: new[] { sourceId, targetId });

        var results = RecommendationResolver.GetRecommendations(context, sourceId);

        var candidate = Assert.Single(results);
        Assert.Equal(RecommendationReason.SameCollection, candidate.ReasonType);
        Assert.Equal(0m, candidate.Signals.RelationScore);
        Assert.Equal(1.0m, candidate.Signals.CollectionScore);
        Assert.Contains("same collection", candidate.Explanation);
    }

    [Fact]
    public void GetRecommendations_ContentOverlapCanOutweighWeakRelation_FlippingDominantReason()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int sourceId = SeedSeries(context, "Source Series", genre: "Fantasy, Action");
        int targetId = SeedSeries(context, "Target Series", genre: "Fantasy, Action");
        // A generic, lowest-priority relation type so its weighted RelationScore contribution
        // (1.0 * 0.30 = 0.30) is easily beaten by a strong genre match (1.0 * 0.10 = 0.10)... so
        // instead anchor via a weak relation type and pile on matching characters, whose weight
        // (0.15) plus genre (0.10) exceeds Relation's own 0.30 only if Relation's confidence is low.
        var relation = new MediaRelation { SourceSeriesId = sourceId, TargetSeriesId = targetId, RelationType = RelationType.Related };
        relation.Evidence.Add(new RelationEvidence { MediaRelation = relation, Provider = RelationEvidenceProvider.User, Confidence = 0.1m });
        context.MediaRelations.Add(relation);
        context.SaveChanges();
        SeedIssue(context, sourceId, characters: "Alice, Bob, Carol");
        SeedIssue(context, targetId, characters: "Alice, Bob, Carol");

        var results = RecommendationResolver.GetRecommendations(context, sourceId);

        var candidate = Assert.Single(results);
        // Relation weighted: 0.1 * 0.30 = 0.03. Character weighted: 1.0 * 0.15 = 0.15. Character wins.
        Assert.Equal(RecommendationReason.SharedCharacters, candidate.ReasonType);
        Assert.Equal(1.0m, candidate.Signals.CharacterScore);
    }

    [Fact]
    public void GetRecommendations_GenreOverlapWithNoRelationalAnchor_DoesNotAppearAsCandidate()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int sourceId = SeedSeries(context, "Source Series", genre: "Fantasy");
        SeedSeries(context, "Unrelated Series", genre: "Fantasy"); // same genre, no relation/continuity/event link

        var results = RecommendationResolver.GetRecommendations(context, sourceId);

        Assert.Empty(results);
    }

    [Fact]
    public void GetRecommendations_RespectsLimit_OrdersByScoreDescending()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int sourceId = SeedSeries(context, "Source Series");
        int strongTargetId = SeedSeries(context, "Strong Target");
        int weakTargetId = SeedSeries(context, "Weak Target");
        MediaRelationResolver.TryCreate(context, sourceId, strongTargetId, RelationType.Prequel);
        var continuity = ContinuityResolver.GetOrCreate(context, "Earth-616");
        ContinuityResolver.AddSeriesToContinuity(context, sourceId, continuity.Id);
        ContinuityResolver.AddSeriesToContinuity(context, weakTargetId, continuity.Id);

        var results = RecommendationResolver.GetRecommendations(context, sourceId, limit: 1);

        var only = Assert.Single(results);
        Assert.Equal(strongTargetId, only.TargetSeriesId); // Prequel relation (0.30 weight) outscores continuity-only (0.15 weight)
    }

    [Fact]
    public void GetRecommendations_UnknownSeriesId_ReturnsEmpty()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);

        var results = RecommendationResolver.GetRecommendations(context, 999);

        Assert.Empty(results);
    }

    [Fact]
    public void GetRecommendations_NoRelationsAtAll_ReturnsEmpty()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int sourceId = SeedSeries(context, "Lonely Series");

        var results = RecommendationResolver.GetRecommendations(context, sourceId);

        Assert.Empty(results);
    }
}
