using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises the scope model of <see cref="GoalResolver"/> (docs/superpowers/specs/2026-10-04-insights-goal-scopes-design.md): membership scopes
/// (reading list, collection, story event, continuity, creator), the media-type predicate, combined (AND) scopes, finish goals, count-each-once, and a
/// goal whose scope was deleted. The single-legacy-scope path is covered by <see cref="GoalResolverTests"/>.
/// </summary>
public class GoalScopeResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.ToLocalTime().Date);

    public GoalScopeResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_goalscope_test_{Guid.NewGuid():N}.db");
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

    private static ReadingGoal Goal(PaperbunkrDbContext ctx, GoalKind kind, long target, params (GoalScopeKind Kind, string? Value)[] scopes)
        => Goal(ctx, kind, target, Today.AddDays(-30), Today.AddDays(30), GoalPeriodKind.Custom, scopes);

    private static ReadingGoal Goal(PaperbunkrDbContext ctx, GoalKind kind, long target, DateOnly start, DateOnly end, GoalPeriodKind period,
        params (GoalScopeKind Kind, string? Value)[] scopes)
    {
        var goal = new ReadingGoal
        {
            Title = "Test", Kind = kind, Metric = GoalMetric.Items, Target = target, PeriodKind = period, PeriodStart = start, PeriodEnd = end, CreatedUtc = Now,
            Scopes = scopes.Select(s => new ReadingGoalScope { Kind = s.Kind, Value = s.Value, Label = s.Value }).ToList(),
        };
        ctx.ReadingGoals.Add(goal);
        ctx.SaveChanges();
        return goal;
    }

    private static ReadingList SeedList(PaperbunkrDbContext ctx, string name, params int[] issueIds)
    {
        var list = new ReadingList { Name = name };
        ctx.ReadingLists.Add(list);
        ctx.SaveChanges();
        int order = 0;
        foreach (int id in issueIds)
        {
            ctx.ReadingListItems.Add(new ReadingListItem { ReadingListId = list.Id, IssueId = id, SortOrder = order++ });
        }

        ctx.SaveChanges();
        return list;
    }

    private static void Finish(PaperbunkrDbContext ctx, int issueId, int daysAgo = 0, int? seriesId = null, int? pages = null)
        => InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issueId, ReadingEventKind.Finished, Now.AddDays(-daysAgo), pages, seriesId);

    [Fact]
    public void ReadingListScope_CountGoal_OnlyCountsIssuesInTheList()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var inList = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var outside = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var list = SeedList(ctx, "Civil War", inList.Id);
        Finish(ctx, inList.Id);
        Finish(ctx, outside.Id);
        Goal(ctx, GoalKind.Count, 10, (GoalScopeKind.ReadingList, list.Id.ToString()));

        Assert.Equal(1, GoalResolver.Build(ctx, Now).Single().CurrentValue);
    }

    [Fact]
    public void FinishGoal_CountsDistinctItemsFinishedAtAnyTime_AndIgnoresReReads()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var a = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var b = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var c = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var list = SeedList(ctx, "Civil War", a.Id, b.Id, c.Id);
        Finish(ctx, a.Id, daysAgo: 400); // long before the goal's window - still counts for a finish goal
        Finish(ctx, a.Id, daysAgo: 2); // a re-read - does not count twice
        Finish(ctx, b.Id, daysAgo: 1);
        Goal(ctx, GoalKind.Finish, 0, (GoalScopeKind.ReadingList, list.Id.ToString()));

        var progress = GoalResolver.Build(ctx, Now).Single();

        Assert.Equal(2, progress.CurrentValue);
        Assert.Equal(3, progress.EffectiveTarget);
        Assert.Equal(GoalOutcome.Active, progress.Outcome);
        Assert.False(progress.IsComplete);
    }

    [Fact]
    public void FinishGoal_Completes_OnTheDayTheLastItemWasFirstFinished()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var a = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var b = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var list = SeedList(ctx, "Pair", a.Id, b.Id);
        Finish(ctx, a.Id, daysAgo: 9);
        Finish(ctx, b.Id, daysAgo: 3);
        Finish(ctx, a.Id, daysAgo: 1); // a later re-read must not move the completion date
        Goal(ctx, GoalKind.Finish, 0, (GoalScopeKind.ReadingList, list.Id.ToString()));

        var progress = GoalResolver.Build(ctx, Now).Single();

        Assert.Equal(GoalOutcome.Completed, progress.Outcome);
        Assert.Equal(DateOnly.FromDateTime(Now.AddDays(-3).ToLocalTime().Date), progress.CompletedOn);
    }

    [Fact]
    public void FinishGoal_TargetIsLive_AddingAnIssueToTheListRaisesIt()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var a = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var b = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var list = SeedList(ctx, "Growing", a.Id);
        Finish(ctx, a.Id);
        Goal(ctx, GoalKind.Finish, 0, (GoalScopeKind.ReadingList, list.Id.ToString()));
        Assert.Equal(GoalOutcome.Completed, GoalResolver.Build(ctx, Now).Single().Outcome);

        ctx.ReadingListItems.Add(new ReadingListItem { ReadingListId = list.Id, IssueId = b.Id, SortOrder = 1 });
        ctx.SaveChanges();

        var progress = GoalResolver.Build(ctx, Now).Single();
        Assert.Equal(2, progress.EffectiveTarget);
        Assert.Equal(GoalOutcome.Active, progress.Outcome); // finished a goal's worth, then the list grew: back to in progress
    }

    [Fact]
    public void FinishGoal_WithNoDeadline_IsNeverMissed()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var a = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var list = SeedList(ctx, "Someday", a.Id);
        Goal(ctx, GoalKind.Finish, 0, Today.AddDays(-4000), DateOnly.MaxValue, GoalPeriodKind.NoDeadline, (GoalScopeKind.ReadingList, list.Id.ToString()));

        var progress = GoalResolver.Build(ctx, Now).Single();

        Assert.Equal(GoalOutcome.Active, progress.Outcome);
        Assert.Equal(GoalPaceState.NotApplicable, progress.PaceState);
    }

    [Fact]
    public void FinishGoal_WithAPastDeadline_AndItemsLeft_IsMissed()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var a = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var b = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var list = SeedList(ctx, "Late", a.Id, b.Id);
        Finish(ctx, a.Id, daysAgo: 20);
        Goal(ctx, GoalKind.Finish, 0, Today.AddDays(-60), Today.AddDays(-1), GoalPeriodKind.Custom, (GoalScopeKind.ReadingList, list.Id.ToString()));

        Assert.Equal(GoalOutcome.Missed, GoalResolver.Build(ctx, Now).Single().Outcome);
    }

    [Fact]
    public void AnEmptyList_IsNotACompletedGoal()
    {
        using var ctx = NewContext();
        var list = SeedList(ctx, "Empty");
        Goal(ctx, GoalKind.Finish, 0, (GoalScopeKind.ReadingList, list.Id.ToString()));

        var progress = GoalResolver.Build(ctx, Now).Single();

        Assert.False(progress.IsComplete);
        Assert.NotEqual(GoalOutcome.Completed, progress.Outcome);
        Assert.Equal(0, progress.EffectiveTarget);
    }

    [Fact]
    public void ADeletedScope_IsFlaggedMissing_CountsNothing_AndIsNeverCompleted()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var a = InsightsResolverTests.SeedIssue(ctx, series.Id);
        Finish(ctx, a.Id);
        var goal = Goal(ctx, GoalKind.Count, 1, (GoalScopeKind.ReadingList, "999"));
        goal.Scopes[0].Label = "Civil War";
        ctx.SaveChanges();

        var progress = GoalResolver.Build(ctx, Now).Single();

        Assert.True(progress.ScopeMissing);
        Assert.Equal("Civil War", progress.MissingScopeLabel);
        Assert.Equal(0, progress.CurrentValue); // not widened to the whole library
        Assert.False(progress.IsComplete);
    }

    [Fact]
    public void CombinedScopes_MustAllMatch()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var both = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var listOnly = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var creatorOnly = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var list = SeedList(ctx, "L", both.Id, listOnly.Id);
        var creator = new Creator { Name = "Brian K. Vaughan" };
        ctx.Creators.Add(creator);
        ctx.SaveChanges();
        ctx.CreatorCredits.Add(new CreatorCredit { CreatorId = creator.Id, IssueId = both.Id, Role = "Writer" });
        ctx.CreatorCredits.Add(new CreatorCredit { CreatorId = creator.Id, IssueId = creatorOnly.Id, Role = "Writer" });
        ctx.SaveChanges();
        foreach (var issue in new[] { both, listOnly, creatorOnly })
        {
            Finish(ctx, issue.Id);
        }

        Goal(ctx, GoalKind.Count, 10, (GoalScopeKind.ReadingList, list.Id.ToString()), (GoalScopeKind.Creator, creator.Id.ToString()));

        Assert.Equal(1, GoalResolver.Build(ctx, Now).Single().CurrentValue);
    }

    [Fact]
    public void MembershipPlusPredicate_CombineForACountGoal()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga", publisher: "Marvel");
        var marvel = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var other = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var list = SeedList(ctx, "L", marvel.Id, other.Id);
        Finish(ctx, marvel.Id);
        Finish(ctx, other.Id);
        ctx.ReadingEvents.First(e => e.ItemId == marvel.Id).Publisher = "Marvel";
        ctx.ReadingEvents.First(e => e.ItemId == other.Id).Publisher = "DC";
        ctx.SaveChanges();
        Goal(ctx, GoalKind.Count, 10, (GoalScopeKind.ReadingList, list.Id.ToString()), (GoalScopeKind.Publisher, "Marvel"));

        Assert.Equal(1, GoalResolver.Build(ctx, Now).Single().CurrentValue);
    }

    [Fact]
    public void CollectionScope_ExpandsSeriesEntries_AndIncludesIssueAndBookEntries()
    {
        using var ctx = NewContext();
        var wholeSeries = InsightsResolverTests.SeedSeries(ctx, "Whole");
        var s1 = InsightsResolverTests.SeedIssue(ctx, wholeSeries.Id);
        var s2 = InsightsResolverTests.SeedIssue(ctx, wholeSeries.Id);
        var loose = InsightsResolverTests.SeedIssue(ctx, InsightsResolverTests.SeedSeries(ctx, "Other").Id);
        var unrelated = InsightsResolverTests.SeedIssue(ctx, InsightsResolverTests.SeedSeries(ctx, "Unrelated").Id);
        var book = new Book { Title = "Dune", FilePath = "C:/books/dune.epub", AddedTime = Now };
        ctx.Books.Add(book);
        var collection = new Collection { Name = "Mixed" };
        ctx.Collections.Add(collection);
        ctx.SaveChanges();
        ctx.CollectionItems.AddRange(
            new CollectionItem { CollectionId = collection.Id, SeriesId = wholeSeries.Id },
            new CollectionItem { CollectionId = collection.Id, IssueId = loose.Id },
            new CollectionItem { CollectionId = collection.Id, BookId = book.Id });
        ctx.SaveChanges();
        Finish(ctx, s1.Id);
        Finish(ctx, loose.Id);
        Finish(ctx, unrelated.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Novel, book.Id, ReadingEventKind.Finished, Now);
        Goal(ctx, GoalKind.Finish, 0, (GoalScopeKind.Collection, collection.Id.ToString()));

        var progress = GoalResolver.Build(ctx, Now).Single();

        Assert.Equal(4, progress.EffectiveTarget); // 2 series issues + 1 loose issue + 1 book
        Assert.Equal(3, progress.CurrentValue); // s1, loose, the book; s2 unread; unrelated is not in the collection
    }

    [Fact]
    public void ContinuityScope_ExpandsItsSeriesToTheirIssues()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var a = InsightsResolverTests.SeedIssue(ctx, series.Id);
        InsightsResolverTests.SeedIssue(ctx, series.Id); // a second, unread issue
        var continuity = new Continuity { Name = "Earth-616" };
        ctx.Continuities.Add(continuity);
        ctx.SaveChanges();
        ctx.ContinuityMemberships.Add(new ContinuityMembership { ContinuityId = continuity.Id, SeriesId = series.Id });
        ctx.SaveChanges();
        Finish(ctx, a.Id);
        Goal(ctx, GoalKind.Finish, 0, (GoalScopeKind.Continuity, continuity.Id.ToString()));

        var progress = GoalResolver.Build(ctx, Now).Single();

        Assert.Equal(2, progress.EffectiveTarget);
        Assert.Equal(1, progress.CurrentValue);
    }

    [Fact]
    public void StoryEventScope_UsesTheEventsMemberIssues()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var inEvent = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var outside = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var storyEvent = new StoryEvent { Name = "Secret War" };
        ctx.StoryEvents.Add(storyEvent);
        ctx.SaveChanges();
        ctx.EventMemberships.Add(new EventMembership { StoryEventId = storyEvent.Id, IssueId = inEvent.Id, Position = 1 });
        ctx.SaveChanges();
        Finish(ctx, inEvent.Id);
        Finish(ctx, outside.Id);
        Goal(ctx, GoalKind.Finish, 0, (GoalScopeKind.StoryEvent, storyEvent.Id.ToString()));

        var progress = GoalResolver.Build(ctx, Now).Single();

        Assert.Equal(1, progress.EffectiveTarget);
        Assert.Equal(GoalOutcome.Completed, progress.Outcome);
    }

    [Fact]
    public void MediaTypeScope_SeparatesMangaFromComicsFromNovels()
    {
        using var ctx = NewContext();
        var manga = new Series { Name = "Berserk", ContentType = ContentType.Manga };
        var comic = new Series { Name = "Saga", ContentType = ContentType.Comic };
        ctx.Series.AddRange(manga, comic);
        ctx.SaveChanges();
        var mangaIssue = InsightsResolverTests.SeedIssue(ctx, manga.Id);
        var comicIssue = InsightsResolverTests.SeedIssue(ctx, comic.Id);
        var book = new Book { Title = "Dune", FilePath = "C:/books/dune.epub", AddedTime = Now };
        ctx.Books.Add(book);
        ctx.SaveChanges();
        Finish(ctx, mangaIssue.Id, seriesId: manga.Id);
        Finish(ctx, comicIssue.Id, seriesId: comic.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Novel, book.Id, ReadingEventKind.Finished, Now);
        Goal(ctx, GoalKind.Count, 10, (GoalScopeKind.MediaType, "Manga"));
        Goal(ctx, GoalKind.Count, 10, (GoalScopeKind.MediaType, "Novel"));

        var results = GoalResolver.Build(ctx, Now);

        Assert.Equal(1, results.First(r => r.Goal.Scopes[0].Value == "Manga").CurrentValue);
        Assert.Equal(1, results.First(r => r.Goal.Scopes[0].Value == "Novel").CurrentValue);
    }

    [Fact]
    public void DistinctOnly_CountsAReReadOnce()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var a = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var b = InsightsResolverTests.SeedIssue(ctx, series.Id);
        Finish(ctx, a.Id, daysAgo: 5);
        Finish(ctx, a.Id, daysAgo: 1);
        Finish(ctx, b.Id, daysAgo: 2);
        var counting = Goal(ctx, GoalKind.Count, 10);
        var distinct = Goal(ctx, GoalKind.Count, 10);
        distinct.DistinctOnly = true;
        ctx.SaveChanges();

        var results = GoalResolver.Build(ctx, Now);

        Assert.Equal(3, results.First(r => r.Goal.Id == counting.Id).CurrentValue);
        Assert.Equal(2, results.First(r => r.Goal.Id == distinct.Id).CurrentValue);
    }

    [Fact]
    public void AGoalWithNoScopeRows_AndNoLegacyScope_IsWholeLibrary()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        Finish(ctx, InsightsResolverTests.SeedIssue(ctx, series.Id).Id);
        Goal(ctx, GoalKind.Count, 10);

        Assert.Equal(1, GoalResolver.Build(ctx, Now).Single().CurrentValue);
    }
}
