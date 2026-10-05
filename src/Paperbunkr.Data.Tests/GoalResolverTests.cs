using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="GoalResolver"/> (docs/superpowers/specs/2026-09-23-insights-reading-goals-design.md).
/// Reuses <see cref="InsightsResolverTests"/>'s seed helpers - same fixture shape as
/// <see cref="RecapResolverTests"/>/<see cref="StatsResolverTests"/>.
/// </summary>
public class GoalResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.ToLocalTime().Date);

    public GoalResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_goal_test_{Guid.NewGuid():N}.db");
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

    private static ReadingGoal SeedGoal(PaperbunkrDbContext ctx, GoalMetric metric, long target, DateOnly start, DateOnly end,
        GoalScopeKind scopeKind = GoalScopeKind.Library, string? scopeValue = null, GoalPeriodKind periodKind = GoalPeriodKind.ThisYear)
    {
        var g = new ReadingGoal
        {
            Title = "Test goal",
            Metric = metric,
            Target = target,
            PeriodKind = periodKind,
            PeriodStart = start,
            PeriodEnd = end,
            ScopeKind = scopeKind,
            ScopeValue = scopeValue,
            CreatedUtc = Now,
        };
        ctx.ReadingGoals.Add(g);
        ctx.SaveChanges();
        return g;
    }

    [Fact]
    public void ItemsMetric_CountsFinishedEventsInRange()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var i1 = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var i2 = InsightsResolverTests.SeedIssue(ctx, series.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, i1.Id, ReadingEventKind.Finished, Now);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, i2.Id, ReadingEventKind.Finished, Now);
        SeedGoal(ctx, GoalMetric.Items, target: 50, Today.AddDays(-30), Today.AddDays(30));

        var progress = GoalResolver.Build(ctx, Now).Single();
        Assert.Equal(2, progress.CurrentValue);
    }

    [Fact]
    public void PagesMetric_SumsPagesReadInRange()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now, pages: 44);
        SeedGoal(ctx, GoalMetric.Pages, target: 3000, Today.AddDays(-30), Today.AddDays(30));

        var progress = GoalResolver.Build(ctx, Now).Single();
        Assert.Equal(44, progress.CurrentValue);
    }

    [Fact]
    public void SeriesScope_OnlyCountsMatchingSeries()
    {
        using var ctx = NewContext();
        var saga = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var sandman = InsightsResolverTests.SeedSeries(ctx, "Sandman");
        var sagaIssue = InsightsResolverTests.SeedIssue(ctx, saga.Id);
        var sandmanIssue = InsightsResolverTests.SeedIssue(ctx, sandman.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, sagaIssue.Id, ReadingEventKind.Finished, Now, seriesId: saga.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, sandmanIssue.Id, ReadingEventKind.Finished, Now, seriesId: sandman.Id);
        SeedGoal(ctx, GoalMetric.Items, target: 10, Today.AddDays(-30), Today.AddDays(30), GoalScopeKind.Series, saga.Id.ToString());

        var progress = GoalResolver.Build(ctx, Now).Single();
        Assert.Equal(1, progress.CurrentValue);
    }

    [Fact]
    public void PublisherScope_OnlyCountsMatchingPublisher()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga", publisher: "Image");
        var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now);
        using (var ctx2 = NewContext())
        {
            var ev = ctx2.ReadingEvents.Single();
            ev.Publisher = "Image";
            ctx2.SaveChanges();
        }

        SeedGoal(ctx, GoalMetric.Items, target: 10, Today.AddDays(-30), Today.AddDays(30), GoalScopeKind.Publisher, "Image");
        SeedGoal(ctx, GoalMetric.Items, target: 10, Today.AddDays(-30), Today.AddDays(30), GoalScopeKind.Publisher, "DC");

        var results = GoalResolver.Build(ctx, Now);
        Assert.Equal(1, results.First(r => r.Goal.ScopeValue == "Image").CurrentValue);
        Assert.Equal(0, results.First(r => r.Goal.ScopeValue == "DC").CurrentValue);
    }

    [Fact]
    public void GenreScope_ExcludesNovels_SincePrimaryGenreIsAlwaysNullForThem()
    {
        using var ctx = NewContext();
        var book = new Book { Title = "Dune", FilePath = "C:/books/dune.epub", AddedTime = Now };
        ctx.Books.Add(book);
        ctx.SaveChanges();
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Novel, book.Id, ReadingEventKind.Finished, Now);
        SeedGoal(ctx, GoalMetric.Items, target: 5, Today.AddDays(-30), Today.AddDays(30), GoalScopeKind.Genre, "Fantasy");

        var progress = GoalResolver.Build(ctx, Now).Single();
        Assert.Equal(0, progress.CurrentValue);
    }

    [Fact]
    public void Pace_ReportsBehind_WhenCurrentValueTrailsExpectedProgress()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now);
        // 100-day window, 50 days elapsed -> expected 50 of target 100, only 1 actually finished.
        SeedGoal(ctx, GoalMetric.Items, target: 100, Today.AddDays(-50), Today.AddDays(50));

        var progress = GoalResolver.Build(ctx, Now).Single();
        Assert.Equal(GoalPaceState.Behind, progress.PaceState);
        Assert.Equal(49, progress.BehindAmount);
    }

    [Fact]
    public void Pace_ReportsOnTrack_WhenCurrentValueMeetsExpectedProgress()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        for (int i = 0; i < 50; i++)
        {
            var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);
            InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now);
        }

        SeedGoal(ctx, GoalMetric.Items, target: 100, Today.AddDays(-50), Today.AddDays(50));

        var progress = GoalResolver.Build(ctx, Now).Single();
        Assert.Equal(GoalPaceState.OnTrack, progress.PaceState);
        Assert.Equal(0, progress.BehindAmount);
    }

    [Fact]
    public void Pace_IsNotApplicable_OnceComplete_EvenIfNaiveMathWouldSayBehind()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        for (int i = 0; i < 5; i++)
        {
            var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);
            InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now);
        }

        SeedGoal(ctx, GoalMetric.Items, target: 5, Today.AddDays(-1), Today.AddDays(365));

        var progress = GoalResolver.Build(ctx, Now).Single();
        Assert.True(progress.IsComplete);
        Assert.Equal(GoalPaceState.NotApplicable, progress.PaceState);
    }

    [Fact]
    public void CustomRange_PastEnd_WithoutCompleting_IsNotApplicablePace_AndNotComplete()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now.AddDays(-30));
        SeedGoal(ctx, GoalMetric.Items, target: 100, Today.AddDays(-60), Today.AddDays(-1), periodKind: GoalPeriodKind.Custom);

        var progress = GoalResolver.Build(ctx, Now).Single();
        Assert.False(progress.IsComplete);
        Assert.Equal(GoalPaceState.NotApplicable, progress.PaceState);
        Assert.Equal(1, progress.CurrentValue);
    }

    [Theory]
    [InlineData(GoalPeriodKind.ThisYear)]
    [InlineData(GoalPeriodKind.ThisMonth)]
    [InlineData(GoalPeriodKind.Custom)]
    public void PeriodEnded_BelowTarget_IsMissed_ForEveryPeriodKind(GoalPeriodKind kind)
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now.AddDays(-30));
        SeedGoal(ctx, GoalMetric.Items, target: 100, Today.AddDays(-60), Today.AddDays(-1), periodKind: kind);

        var progress = GoalResolver.Build(ctx, Now).Single();
        Assert.Equal(GoalOutcome.Missed, progress.Outcome);
        Assert.Equal(GoalPaceState.NotApplicable, progress.PaceState);
        Assert.Null(progress.CompletedOn);
    }

    [Fact]
    public void PeriodEndingToday_BelowTarget_IsStillActive()
    {
        using var ctx = NewContext();
        SeedGoal(ctx, GoalMetric.Items, target: 10, Today.AddDays(-30), Today);

        Assert.Equal(GoalOutcome.Active, GoalResolver.Build(ctx, Now).Single().Outcome);
    }

    [Fact]
    public void CompletedOnTheLastDay_IsCompleted_NotMissed()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now);
        SeedGoal(ctx, GoalMetric.Items, target: 1, Today.AddDays(-30), Today);

        var progress = GoalResolver.Build(ctx, Now).Single();
        Assert.Equal(GoalOutcome.Completed, progress.Outcome);
        Assert.Equal(Today, progress.CompletedOn);
    }

    [Fact]
    public void CompletedOn_IsTheDayTheRunningTotalFirstReachedTarget_ForItems()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        // Three finishes on three different days; target 2 is reached on the second, not the last.
        foreach (int daysAgo in new[] { 20, 10, 2 })
        {
            var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);
            InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now.AddDays(-daysAgo));
        }

        SeedGoal(ctx, GoalMetric.Items, target: 2, Today.AddDays(-60), Today.AddDays(30));

        var progress = GoalResolver.Build(ctx, Now).Single();
        Assert.Equal(GoalOutcome.Completed, progress.Outcome);
        Assert.Equal(DateOnly.FromDateTime(Now.AddDays(-10).ToLocalTime().Date), progress.CompletedOn);
    }

    [Fact]
    public void CompletedOn_UsesTheCumulativePageCount_ForPages()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        // 40 + 40 pages: a target of 70 is crossed by the second event.
        foreach (int daysAgo in new[] { 15, 5 })
        {
            var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);
            InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now.AddDays(-daysAgo), pages: 40);
        }

        SeedGoal(ctx, GoalMetric.Pages, target: 70, Today.AddDays(-60), Today.AddDays(30));

        Assert.Equal(DateOnly.FromDateTime(Now.AddDays(-5).ToLocalTime().Date), GoalResolver.Build(ctx, Now).Single().CompletedOn);
    }

    [Fact]
    public void ElapsedFraction_ClampsAtZero_AtPeriodStart()
    {
        using var ctx = NewContext();
        // Goal starts today (elapsed fraction 0) - expected progress 0, so 0 actual reads as OnTrack, not Behind.
        SeedGoal(ctx, GoalMetric.Items, target: 10, Today, Today.AddDays(30));
        var progress = GoalResolver.Build(ctx, Now).Single();
        Assert.Equal(GoalPaceState.OnTrack, progress.PaceState);
    }

    [Fact]
    public void ElapsedFraction_ClampsAtOne_AtPeriodEnd()
    {
        using var ctx = NewContext();
        // Goal ends today (elapsed fraction 1) with nothing finished -> fully behind by the full target.
        SeedGoal(ctx, GoalMetric.Items, target: 10, Today.AddDays(-30), Today);
        var progress = GoalResolver.Build(ctx, Now).Single();
        Assert.Equal(GoalPaceState.Behind, progress.PaceState);
        Assert.Equal(10, progress.BehindAmount);
    }
}
