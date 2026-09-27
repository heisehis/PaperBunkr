using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="InsightsRecommendationResolver"/> (docs/superpowers/specs/2026-09-23-insights-
/// recommendations-surface-design.md). Reuses <see cref="InsightsResolverTests"/>'s seed helpers and
/// <see cref="MediaRelationResolver.TryCreate"/> for a real relational anchor, same as
/// <see cref="RecommendationResolverTests"/>.
/// </summary>
public class InsightsRecommendationResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    public InsightsRecommendationResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_insights_recs_test_{Guid.NewGuid():N}.db");
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

    private PaperbunkrDbContext NewContext() => new(_dbOptions);

    /// <summary>Marks a series as "recently opened" for <see cref="HomeFeedResolver.GetRecentlyOpenedSeriesIds"/>
    /// purposes by giving one of its issues an <see cref="Issue.OpenedTime"/>.</summary>
    private static void MarkOpened(PaperbunkrDbContext ctx, int seriesId, DateTime when)
    {
        var issue = InsightsResolverTests.SeedIssue(ctx, seriesId, openedTime: when);
    }

    [Fact]
    public void FinishedSeriesWithRealRelation_ReturnsSeedAndTarget()
    {
        using var ctx = NewContext();
        var source = InsightsResolverTests.SeedSeries(ctx, "Source Series");
        var target = InsightsResolverTests.SeedSeries(ctx, "Target Series");
        var issue = InsightsResolverTests.SeedIssue(ctx, source.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now, seriesId: source.Id);
        MediaRelationResolver.TryCreate(ctx, source.Id, target.Id, RelationType.Prequel);

        var seed = InsightsRecommendationResolver.GetSeedWithRecommendations(ctx);

        Assert.NotNull(seed);
        Assert.Equal(source.Id, seed!.SeedSeriesId);
        Assert.Equal("Source Series", seed.SeedSeriesName);
        var recommendation = Assert.Single(seed.Recommendations);
        Assert.Equal(target.Id, recommendation.TargetSeriesId);
        Assert.NotEmpty(recommendation.Explanation);
    }

    [Fact]
    public void TargetAlreadyInHomesPicks_IsExcluded_AndFallsThroughToNextSeed()
    {
        using var ctx = NewContext();
        var homeSeed = InsightsResolverTests.SeedSeries(ctx, "Home Seed");
        var excludedTarget = InsightsResolverTests.SeedSeries(ctx, "Excluded Target");
        MarkOpened(ctx, homeSeed.Id, Now);
        MediaRelationResolver.TryCreate(ctx, homeSeed.Id, excludedTarget.Id, RelationType.Prequel);

        // Most-recently-finished series only relates to the now-excluded target.
        var recentSeed = InsightsResolverTests.SeedSeries(ctx, "Recent Finish");
        var recentIssue = InsightsResolverTests.SeedIssue(ctx, recentSeed.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, recentIssue.Id, ReadingEventKind.Finished, Now, seriesId: recentSeed.Id);
        MediaRelationResolver.TryCreate(ctx, recentSeed.Id, excludedTarget.Id, RelationType.Prequel);

        // Earlier-finished series relates to a different, non-excluded target.
        var olderSeed = InsightsResolverTests.SeedSeries(ctx, "Older Finish");
        var olderTarget = InsightsResolverTests.SeedSeries(ctx, "Older Target");
        var olderIssue = InsightsResolverTests.SeedIssue(ctx, olderSeed.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, olderIssue.Id, ReadingEventKind.Finished, Now.AddDays(-5), seriesId: olderSeed.Id);
        MediaRelationResolver.TryCreate(ctx, olderSeed.Id, olderTarget.Id, RelationType.Prequel);

        var seed = InsightsRecommendationResolver.GetSeedWithRecommendations(ctx);

        Assert.NotNull(seed);
        Assert.Equal(olderSeed.Id, seed!.SeedSeriesId);
        Assert.Equal(olderTarget.Id, Assert.Single(seed.Recommendations).TargetSeriesId);
    }

    [Fact]
    public void AllCandidatesExcluded_ReturnsNull()
    {
        using var ctx = NewContext();
        var homeSeed = InsightsResolverTests.SeedSeries(ctx, "Home Seed");
        var excludedTarget = InsightsResolverTests.SeedSeries(ctx, "Excluded Target");
        MarkOpened(ctx, homeSeed.Id, Now);
        MediaRelationResolver.TryCreate(ctx, homeSeed.Id, excludedTarget.Id, RelationType.Prequel);

        var finishedSeed = InsightsResolverTests.SeedSeries(ctx, "Finished Series");
        var issue = InsightsResolverTests.SeedIssue(ctx, finishedSeed.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now, seriesId: finishedSeed.Id);
        MediaRelationResolver.TryCreate(ctx, finishedSeed.Id, excludedTarget.Id, RelationType.Prequel);

        Assert.Null(InsightsRecommendationResolver.GetSeedWithRecommendations(ctx));
    }

    [Fact]
    public void NoFinishedEventsAtAll_ReturnsNull()
    {
        using var ctx = NewContext();
        Assert.Null(InsightsRecommendationResolver.GetSeedWithRecommendations(ctx));
    }

    [Fact]
    public void MostRecentlyFinishedSeries_IsPreferred_WhenItHasValidRecommendations()
    {
        using var ctx = NewContext();
        var recentSeed = InsightsResolverTests.SeedSeries(ctx, "Recent Finish");
        var recentTarget = InsightsResolverTests.SeedSeries(ctx, "Recent Target");
        var recentIssue = InsightsResolverTests.SeedIssue(ctx, recentSeed.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, recentIssue.Id, ReadingEventKind.Finished, Now, seriesId: recentSeed.Id);
        MediaRelationResolver.TryCreate(ctx, recentSeed.Id, recentTarget.Id, RelationType.Prequel);

        var olderSeed = InsightsResolverTests.SeedSeries(ctx, "Older Finish");
        var olderTarget = InsightsResolverTests.SeedSeries(ctx, "Older Target");
        var olderIssue = InsightsResolverTests.SeedIssue(ctx, olderSeed.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, olderIssue.Id, ReadingEventKind.Finished, Now.AddDays(-5), seriesId: olderSeed.Id);
        MediaRelationResolver.TryCreate(ctx, olderSeed.Id, olderTarget.Id, RelationType.Prequel);

        var seed = InsightsRecommendationResolver.GetSeedWithRecommendations(ctx);

        Assert.NotNull(seed);
        Assert.Equal(recentSeed.Id, seed!.SeedSeriesId);
    }
}
