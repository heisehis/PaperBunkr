using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="InsightsResolver"/> (docs/superpowers/specs/2026-09-05-insights-dashboard-
/// design.md) against a real SQLite database - same fixture rationale as <see cref="HomeFeedResolverTests"/>.
/// </summary>
public class InsightsResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    public InsightsResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_insights_test_{Guid.NewGuid():N}.db");
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

    private static Series SeedSeries(PaperbunkrDbContext ctx, string name, ReadingStatus status = ReadingStatus.Unknown, string? publisher = null)
    {
        var s = new Series { Name = name, ReadingStatus = status, Publisher = publisher };
        ctx.Series.Add(s);
        ctx.SaveChanges();
        return s;
    }

    private static Issue SeedIssue(PaperbunkrDbContext ctx, int seriesId, string? number = null, int? lastPageRead = null,
        int pageCount = 100, DateTime? addedTime = null, DateTime? openedTime = null, float? rating = null, string? genre = null)
    {
        var i = new Issue
        {
            SeriesId = seriesId,
            Number = number,
            LastPageRead = lastPageRead,
            PageCount = pageCount,
            AddedTime = addedTime,
            OpenedTime = openedTime,
            OpenCount = openedTime is null ? 0 : 1,
            Rating = rating,
        };
        if (genre is not null)
        {
            i.Tags.Add(new IssueTag { Field = IssueTagField.Genre, Value = genre, Category = "Genre" });
        }

        ctx.Issues.Add(i);
        ctx.SaveChanges();
        return i;
    }

    private static void SeedEvent(PaperbunkrDbContext ctx, ReadingItemType type, int itemId, ReadingEventKind kind, DateTime whenUtc, int? pages = null, int? seriesId = null)
    {
        ctx.ReadingEvents.Add(new ReadingEvent
        {
            ItemType = type,
            ItemId = itemId,
            Kind = kind,
            TimestampUtc = whenUtc,
            PagesRead = pages,
            SeriesId = seriesId,
        });
        ctx.SaveChanges();
    }

    [Fact]
    public void EmptyLibrary_ProducesZeroedSnapshot_WithoutThrowing()
    {
        using var ctx = NewContext();
        var snap = InsightsResolver.Build(ctx, InsightsRange.Days90, Now);

        Assert.Empty(snap.Continue);
        Assert.Empty(snap.AlmostDone);
        Assert.Empty(snap.DiveIn);
        Assert.Empty(snap.Gaps);
        Assert.Equal(0, snap.Lifetime.ItemsRead);
        Assert.Equal(0, snap.ReadingDayStreak.Current);
        Assert.Equal(0, snap.FinishStreak.Current);
        Assert.Equal(0, snap.Completion.Read + snap.Completion.InProgress + snap.Completion.Unread);
    }

    [Fact]
    public void Continue_ListsEveryInProgressSeries_TagsTheStaleOnes_ExcludesDropped()
    {
        using var ctx = NewContext();
        var live = SeedSeries(ctx, "Live");
        var stale = SeedSeries(ctx, "Stale");
        var dropped = SeedSeries(ctx, "Dropped", ReadingStatus.Dropped);

        var i1 = SeedIssue(ctx, live.Id, lastPageRead: 50);      // in progress, touched recently
        var i2 = SeedIssue(ctx, stale.Id, lastPageRead: 50);     // in progress, stale
        var i3 = SeedIssue(ctx, dropped.Id, lastPageRead: 50);   // in progress but dropped

        SeedEvent(ctx, ReadingItemType.Comic, i1.Id, ReadingEventKind.Opened, Now.AddDays(-2));
        SeedEvent(ctx, ReadingItemType.Comic, i2.Id, ReadingEventKind.Opened, Now.AddDays(-40));
        SeedEvent(ctx, ReadingItemType.Comic, i3.Id, ReadingEventKind.Opened, Now.AddDays(-40));

        var cont = InsightsResolver.Build(ctx, InsightsRange.Days90, Now).Continue;

        Assert.Equal(2, cont.Count);
        Assert.Equal(new[] { live.Id, stale.Id }, cont.Select(c => c.SeriesId)); // most-recently-touched first
        Assert.DoesNotContain("dropped off", cont.Single(c => c.SeriesId == live.Id).Subtitle);
        Assert.Contains("dropped off", cont.Single(c => c.SeriesId == stale.Id).Subtitle);
        Assert.Equal(i1.Id, cont.Single(c => c.SeriesId == live.Id).ResumeIssueId);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void AlmostDone_TriggersForOneToThreeRemainingInAStartedSeries(int unreadCount, bool expected)
    {
        using var ctx = NewContext();
        var s = SeedSeries(ctx, "Run");
        SeedIssue(ctx, s.Id, number: "1", lastPageRead: 100); // read (100/100 = 100%)
        for (int n = 0; n < unreadCount; n++)
        {
            SeedIssue(ctx, s.Id, number: (n + 2).ToString(), lastPageRead: null);
        }

        var snap = InsightsResolver.Build(ctx, InsightsRange.Days90, Now);
        Assert.Equal(expected, snap.AlmostDone.Any(a => a.SeriesId == s.Id));
    }

    [Fact]
    public void Gaps_FlagNearCompleteRunsWithAFewHoles_IgnoreNonNumericNumbers()
    {
        using var ctx = NewContext();
        var s = SeedSeries(ctx, "Gappy");
        foreach (var n in new[] { "1", "2", "4", "5", "6", "7", "Annual 1" }) // own 6 of #1-7 -> ownership 0.86
        {
            SeedIssue(ctx, s.Id, number: n);
        }

        var snap = InsightsResolver.Build(ctx, InsightsRange.Days90, Now);
        var gap = Assert.Single(snap.Gaps);
        Assert.Equal(new[] { 3 }, gap.MissingNumbers);
    }

    [Fact]
    public void Gaps_IgnoreScatteredOwnershipAcrossAWideRange()
    {
        using var ctx = NewContext();
        var s = SeedSeries(ctx, "Bulk import");
        foreach (var n in new[] { "1", "2", "3", "598", "599", "600" }) // 6 of a 600-wide span -> ownership 0.01
        {
            SeedIssue(ctx, s.Id, number: n);
        }

        Assert.Empty(InsightsResolver.Build(ctx, InsightsRange.Days90, Now).Gaps);
    }

    [Fact]
    public void Gaps_IgnoreRunsMissingMoreThanTen()
    {
        using var ctx = NewContext();
        var s = SeedSeries(ctx, "Holey");
        // own #1-4 then #16-40: span 40, owned 29 -> ownership 0.725 < 0.75 anyway, but also >10 missing.
        for (int n = 1; n <= 4; n++) SeedIssue(ctx, s.Id, number: n.ToString());
        for (int n = 16; n <= 40; n++) SeedIssue(ctx, s.Id, number: n.ToString());

        Assert.Empty(InsightsResolver.Build(ctx, InsightsRange.Days90, Now).Gaps);
    }

    [Fact]
    public void DiveIn_SurfacesNeverOpenedOwnedRunsStartingAtOne_BiggestFirst()
    {
        using var ctx = NewContext();
        var big = SeedSeries(ctx, "Big run");        // own #1-12, never opened -> in
        for (int n = 1; n <= 12; n++) SeedIssue(ctx, big.Id, number: n.ToString());

        var small = SeedSeries(ctx, "Small run");    // own #1-6, never opened -> in, but after Big
        for (int n = 1; n <= 6; n++) SeedIssue(ctx, small.Id, number: n.ToString());

        var started = SeedSeries(ctx, "Started");     // own #1-12 but one is read -> out
        for (int n = 1; n <= 12; n++) SeedIssue(ctx, started.Id, number: n.ToString(), pageCount: 10, lastPageRead: n == 1 ? 10 : (int?)null, openedTime: n == 1 ? Now.AddDays(-1) : (DateTime?)null);

        var noStart = SeedSeries(ctx, "No #1");        // own #5-20, never opened -> out (doesn't start at #1/#2)
        for (int n = 5; n <= 20; n++) SeedIssue(ctx, noStart.Id, number: n.ToString());

        var diveIn = InsightsResolver.Build(ctx, InsightsRange.Days90, Now).DiveIn;

        Assert.Equal(new[] { big.Id, small.Id }, diveIn.Select(d => d.SeriesId));
        Assert.Null(diveIn[0].ResumeIssueId);
        Assert.Contains("12 issues", diveIn[0].Subtitle);
    }

    [Fact]
    public void DiveIn_IgnoresScatteredOwnershipEvenWhenItStartsAtOne()
    {
        using var ctx = NewContext();
        var s = SeedSeries(ctx, "Scattered");
        foreach (var n in new[] { "1", "2", "40", "41", "80", "81" }) // starts at #1 but owns ~7% of the span
        {
            SeedIssue(ctx, s.Id, number: n);
        }

        Assert.Empty(InsightsResolver.Build(ctx, InsightsRange.Days90, Now).DiveIn);
    }

    [Fact]
    public void Lifetime_CountsDistinctFinishedItems_RereadsDoNotInflate()
    {
        using var ctx = NewContext();
        var s = SeedSeries(ctx, "S");
        var i = SeedIssue(ctx, s.Id, pageCount: 20);
        SeedEvent(ctx, ReadingItemType.Comic, i.Id, ReadingEventKind.Finished, Now.AddDays(-10));
        SeedEvent(ctx, ReadingItemType.Comic, i.Id, ReadingEventKind.Finished, Now.AddDays(-1)); // re-read

        var snap = InsightsResolver.Build(ctx, InsightsRange.Days90, Now);
        Assert.Equal(1, snap.Lifetime.ItemsRead);
        Assert.Equal(20, snap.Lifetime.PagesRead);
    }

    [Fact]
    public void Lifetime_KeepsCountingWhenTheItemRowIsGone()
    {
        using var ctx = NewContext();
        SeedEvent(ctx, ReadingItemType.Comic, itemId: 9999, ReadingEventKind.Finished, Now.AddDays(-3));

        var snap = InsightsResolver.Build(ctx, InsightsRange.Days90, Now);
        Assert.Equal(1, snap.Lifetime.ItemsRead); // no Issue row, still counted
    }

    [Fact]
    public void ReadingDayStreak_CountsConsecutiveLocalDays_FinishStreakIsFinishOnly()
    {
        using var ctx = NewContext();
        // Opened on each of the last 3 days; finished only today and yesterday.
        SeedEvent(ctx, ReadingItemType.Comic, 1, ReadingEventKind.Opened, Now);
        SeedEvent(ctx, ReadingItemType.Comic, 1, ReadingEventKind.Opened, Now.AddDays(-1));
        SeedEvent(ctx, ReadingItemType.Comic, 1, ReadingEventKind.Opened, Now.AddDays(-2));
        SeedEvent(ctx, ReadingItemType.Comic, 1, ReadingEventKind.Finished, Now);
        SeedEvent(ctx, ReadingItemType.Comic, 1, ReadingEventKind.Finished, Now.AddDays(-1));

        var snap = InsightsResolver.Build(ctx, InsightsRange.Days90, Now);
        Assert.Equal(3, snap.ReadingDayStreak.Current);
        Assert.Equal(2, snap.FinishStreak.Current);
    }

    [Fact]
    public void Pace_WeeklyBucketsForNinetyDays_MonthlyForTwelveMonths()
    {
        using var ctx = NewContext();
        SeedEvent(ctx, ReadingItemType.Comic, 1, ReadingEventKind.Finished, Now.AddDays(-3), pages: 40);
        SeedEvent(ctx, ReadingItemType.Comic, 2, ReadingEventKind.Finished, Now.AddDays(-40), pages: 10);

        var weekly = InsightsResolver.Build(ctx, InsightsRange.Days90, Now).Pace;
        Assert.Equal(13, weekly.Count);
        Assert.Equal(2, weekly.Sum(b => b.Finished));
        Assert.Equal(50, weekly.Sum(b => b.Pages));

        var monthly = InsightsResolver.Build(ctx, InsightsRange.Months12, Now).Pace;
        Assert.Equal(12, monthly.Count);
    }

    [Fact]
    public void FinishedInRange_ExcludesEventsOutsideTheWindow()
    {
        using var ctx = NewContext();
        SeedEvent(ctx, ReadingItemType.Comic, 1, ReadingEventKind.Finished, Now.AddDays(-10), pages: 30);
        SeedEvent(ctx, ReadingItemType.Comic, 2, ReadingEventKind.Finished, Now.AddDays(-200), pages: 30);

        var snap = InsightsResolver.Build(ctx, InsightsRange.Days90, Now);
        Assert.Equal(1, snap.FinishedInRange.Items);
        Assert.Equal(30, snap.FinishedInRange.Pages);
    }

    [Fact]
    public void Completion_CountsAcrossComicsAndNovels()
    {
        using var ctx = NewContext();
        var s = SeedSeries(ctx, "S");
        SeedIssue(ctx, s.Id, pageCount: 100, lastPageRead: 100); // read
        SeedIssue(ctx, s.Id, pageCount: 100, lastPageRead: 50);  // in progress
        SeedIssue(ctx, s.Id, pageCount: 100, lastPageRead: null); // unread

        ctx.Books.Add(new Book { Title = "Done", FilePath = "a", Finished = true });
        ctx.Books.Add(new Book { Title = "Mid", FilePath = "b", LastOpenedTime = Now, LastChapterIndex = 3 });
        ctx.Books.Add(new Book { Title = "Fresh", FilePath = "c" });
        ctx.SaveChanges();

        var c = InsightsResolver.Build(ctx, InsightsRange.Days90, Now).Completion;
        Assert.Equal(2, c.Read);
        Assert.Equal(2, c.InProgress);
        Assert.Equal(2, c.Unread);
    }

    [Fact]
    public void Ratings_BucketsRoundedStarsAndExcludesUnrated()
    {
        using var ctx = NewContext();
        var s = SeedSeries(ctx, "S");
        SeedIssue(ctx, s.Id, rating: 4.4f);
        SeedIssue(ctx, s.Id, rating: 4.5f);
        SeedIssue(ctx, s.Id, rating: null);

        var ratings = InsightsResolver.Build(ctx, InsightsRange.Days90, Now).Ratings;
        Assert.Equal(5, ratings.Count);
        Assert.Equal(1, ratings.Single(r => r.Stars == 4).Count);
        Assert.Equal(1, ratings.Single(r => r.Stars == 5).Count);
    }
}
