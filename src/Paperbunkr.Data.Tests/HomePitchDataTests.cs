using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Data side of the 2026-09-28 Home pitch (docs/superpowers/specs/2026-09-28-home-improvements-design.md): the merged Continue
/// Reading row (I2), the Needs Attention pick (I3), spotlight relevance (I4) and "Not interested" dismissals (I5). Real SQLite,
/// same rationale as <see cref="HomeFeedResolverTests"/>.
/// </summary>
public class HomePitchDataTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public HomePitchDataTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_home_pitch_test_{Guid.NewGuid():N}.db");
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

    private PaperbunkrDbContext Context() => new(_dbOptions);

    private static int Series(PaperbunkrDbContext context, string name)
    {
        var series = new Series { Name = name };
        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    /// <param name="read">null unread, 100 finished, anything between in progress (page count is 100).</param>
    private static int Issue(PaperbunkrDbContext context, int seriesId, string? number = null, int? read = null,
        DateTime? added = null, DateTime? opened = null, string? genre = null)
    {
        var issue = new Issue { SeriesId = seriesId, Number = number, LastPageRead = read, PageCount = 100, AddedTime = added, OpenedTime = opened };
        if (genre is not null)
        {
            issue.MergeFrom(IssueTagField.Genre, new[] { genre });
        }

        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    private static void Book(PaperbunkrDbContext context, string title, DateTime lastOpened)
    {
        context.Books.Add(new Book
        {
            Title = title,
            Format = BookFormat.Epub,
            FilePath = $@"C:\books\{title}.epub",
            AddedTime = lastOpened,
            LastOpenedTime = lastOpened,
            LastChapterIndex = 2,
            ChapterCount = 10,
        });
        context.SaveChanges();
    }

    // --- I2: merged Continue Reading ---

    [Fact]
    public void ContinueReadingMixed_InterleavesComicsAndBooks_ByLastTouch()
    {
        using var context = Context();
        Issue(context, Series(context, "Comic Old"), read: 30, opened: Now.AddDays(-3));
        Book(context, "Book Mid", Now.AddDays(-2));
        Issue(context, Series(context, "Comic New"), read: 30, opened: Now.AddDays(-1));

        var row = HomeFeedResolver.GetContinueReadingMixed(context);

        Assert.Equal(new[] { "Comic New", "Book Mid", "Comic Old" },
            row.Select(c => c.IsBook ? c.Book!.Title : c.Series!.Name));
    }

    [Fact]
    public void ContinueReadingMixed_RespectsLimit()
    {
        using var context = Context();
        for (int i = 0; i < 4; i++)
        {
            Issue(context, Series(context, $"Comic {i}"), read: 30, opened: Now.AddMinutes(-i));
            Book(context, $"Book {i}", Now.AddMinutes(-i - 10));
        }

        var row = HomeFeedResolver.GetContinueReadingMixed(context, limit: 5);

        Assert.Equal(5, row.Count);
        Assert.Equal(4, row.Count(c => !c.IsBook)); // the four comics are all newer than every book
    }

    // --- I3: Needs Attention ---

    [Fact]
    public void TopAttention_PrefersAlmostDone()
    {
        using var context = Context();
        int almost = Series(context, "Almost");
        Issue(context, almost, "1", read: 100);
        int next = Issue(context, almost, "2");
        int stalled = Series(context, "Stalled");
        Issue(context, stalled, "1", read: 30, opened: Now.AddDays(-40));
        for (int i = 2; i < 7; i++) Issue(context, stalled, i.ToString());

        var attention = HomeFeedResolver.GetTopAttention(context, Now);

        Assert.NotNull(attention);
        Assert.Equal(almost, attention!.SeriesId);
        Assert.Equal("1 issue left in Almost", attention.Headline);
        Assert.Equal(next, attention.ResumeIssueId);
        Assert.Equal("Resume #2", attention.ActionLabel);
    }

    [Fact]
    public void TopAttention_FallsBackToStalledSeries()
    {
        using var context = Context();
        int stalled = Series(context, "Stalled");
        int resume = Issue(context, stalled, "1", read: 30, opened: Now.AddDays(-40));
        for (int i = 2; i < 7; i++) Issue(context, stalled, i.ToString());
        int fresh = Series(context, "Fresh");
        Issue(context, fresh, "1", read: 30, opened: Now.AddDays(-1));
        for (int i = 2; i < 7; i++) Issue(context, fresh, i.ToString());

        var attention = HomeFeedResolver.GetTopAttention(context, Now);

        Assert.NotNull(attention);
        Assert.Equal(stalled, attention!.SeriesId);
        Assert.Equal("Pick Stalled back up", attention.Headline);
        Assert.Contains("dropped off", attention.Reason);
        Assert.Equal(resume, attention.ResumeIssueId);
    }

    [Fact]
    public void TopAttention_FallsBackToGaps_WithViewSeries()
    {
        using var context = Context();
        int gappy = Series(context, "Gappy");
        foreach (var n in new[] { "1", "2", "4" }) Issue(context, gappy, n);

        var attention = HomeFeedResolver.GetTopAttention(context, Now);

        Assert.NotNull(attention);
        Assert.Equal("1 missing from Gappy", attention!.Headline);
        Assert.Equal("Missing #3", attention.Reason);
        Assert.Null(attention.ResumeIssueId);
        Assert.Equal("View series", attention.ActionLabel);
    }

    [Fact]
    public void TopAttention_NullWhenNothingNeedsAttention()
    {
        using var context = Context();
        Issue(context, Series(context, "Lonely"), "1");

        Assert.Null(HomeFeedResolver.GetTopAttention(context, Now));
    }

    [Fact]
    public void Insights_MarksOnlyOldInProgressSeriesAsStalled()
    {
        using var context = Context();
        Issue(context, Series(context, "Old"), read: 30, opened: Now.AddDays(-22));
        Issue(context, Series(context, "Recent"), read: 30, opened: Now.AddDays(-20));

        var snapshot = InsightsResolver.Build(context, Now);

        Assert.True(snapshot.Continue.Single(c => c.SeriesName == "Old").IsStalled);
        Assert.False(snapshot.Continue.Single(c => c.SeriesName == "Recent").IsStalled);
    }

    // --- I4: spotlight relevance ---

    [Fact]
    public void Spotlight_TakesAtMostOneIssuePerSeries()
    {
        using var context = Context();
        int big = Series(context, "Big Run");
        for (int i = 0; i < 6; i++) Issue(context, big, (i + 1).ToString());

        var picks = HomeFeedResolver.GetSpotlightPicks(context, new Random(1), nowUtc: Now);

        Assert.Single(picks);
    }

    [Fact]
    public void Spotlight_GivesTwoSlotsToTheNewestArrivals()
    {
        using var context = Context();
        int oldA = Issue(context, Series(context, "Old A"), added: Now.AddDays(-90));
        int oldB = Issue(context, Series(context, "Old B"), added: Now.AddDays(-90));
        int oldC = Issue(context, Series(context, "Old C"), added: Now.AddDays(-90));
        int newest = Issue(context, Series(context, "New 1"), added: Now.AddDays(-1));
        int second = Issue(context, Series(context, "New 2"), added: Now.AddDays(-2));
        int third = Issue(context, Series(context, "New 3"), added: Now.AddDays(-3));

        var picks = HomeFeedResolver.GetSpotlightPicks(context, new Random(1), count: 4, nowUtc: Now).Select(p => p.Id).ToList();

        Assert.Equal(4, picks.Count);
        Assert.Contains(newest, picks);
        Assert.Contains(second, picks);
        Assert.Equal(second, picks[3]); // interleaved: weighted, new, weighted, new
        Assert.Equal(newest, picks[1]);
        Assert.Equal(2, picks.Count(id => id is var x && (x == oldA || x == oldB || x == oldC || x == third)));
    }

    [Fact]
    public void Spotlight_WithNoNewArrivals_FillsEverySlotFromTheWeightedDraw()
    {
        using var context = Context();
        for (int i = 0; i < 4; i++) Issue(context, Series(context, $"Old {i}"), added: Now.AddDays(-90));

        var picks = HomeFeedResolver.GetSpotlightPicks(context, new Random(1), count: 3, nowUtc: Now);

        Assert.Equal(3, picks.Count);
        Assert.Equal(3, picks.Select(p => p.SeriesId).Distinct().Count());
    }

    // --- I5: "Not interested" ---

    [Fact]
    public void Dismiss_IsIdempotent_AndRestoreRemovesIt()
    {
        using var context = Context();
        int seriesId = Series(context, "Nope");

        DismissedRecommendations.Dismiss(context, seriesId, Now);
        DismissedRecommendations.Dismiss(context, seriesId, Now.AddDays(1));

        var row = Assert.Single(DismissedRecommendations.List(context));
        Assert.Equal("Nope", row.SeriesName);
        Assert.Equal(Now, row.DismissedUtc);
        Assert.Contains(seriesId, DismissedRecommendations.GetIds(context));

        DismissedRecommendations.Restore(context, seriesId);
        Assert.Empty(DismissedRecommendations.GetIds(context));
        DismissedRecommendations.Restore(context, seriesId); // no-op, no throw
    }

    [Fact]
    public void Dismissal_GoesAwayWithItsSeries()
    {
        using var context = Context();
        int seriesId = Series(context, "Deleted Later");
        DismissedRecommendations.Dismiss(context, seriesId, Now);

        context.Series.Remove(context.Series.Single(s => s.Id == seriesId));
        context.SaveChanges();

        Assert.Empty(DismissedRecommendations.List(context));
    }
}
