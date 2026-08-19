using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="HomeFeedResolver"/> (docs/superpowers/specs/2026-08-18-home-screen-design.md)
/// against a real SQLite database - same rationale as <see cref="RecommendationResolverTests"/>.
/// </summary>
public class HomeFeedResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public HomeFeedResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_home_feed_test_{Guid.NewGuid():N}.db");
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

    private static int SeedSeries(PaperbunkrDbContext context, string name)
    {
        var series = new Series { Name = name };
        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    private static int SeedIssue(PaperbunkrDbContext context, int seriesId, int? lastPageRead = null,
        int? pageCount = 100, DateTime? addedTime = null, DateTime? openedTime = null, string? genre = null)
    {
        var issue = new Issue
        {
            SeriesId = seriesId,
            LastPageRead = lastPageRead,
            PageCount = pageCount,
            AddedTime = addedTime,
            OpenedTime = openedTime,
            Genre = genre,
        };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    // --- GetContinueReading ---

    [Fact]
    public void GetContinueReading_PicksTheActualInProgressIssue_NotTheFirstUnreadOne()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Ongoing Series");
        SeedIssue(context, seriesId, lastPageRead: null); // issue #1: never opened
        int inProgressIssueId = SeedIssue(context, seriesId, lastPageRead: 30, pageCount: 100, openedTime: DateTime.UtcNow); // issue #2: mid-read

        var results = HomeFeedResolver.GetContinueReading(context);

        var candidate = Assert.Single(results);
        Assert.Equal(seriesId, candidate.Series.Id);
        Assert.Equal(inProgressIssueId, candidate.ResumeIssue.Id);
    }

    [Fact]
    public void GetContinueReading_ExcludesUnreadAndFullyReadSeries()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int unreadSeriesId = SeedSeries(context, "Unread Series");
        SeedIssue(context, unreadSeriesId, lastPageRead: null);
        int finishedSeriesId = SeedSeries(context, "Finished Series");
        SeedIssue(context, finishedSeriesId, lastPageRead: 100, pageCount: 100);

        var results = HomeFeedResolver.GetContinueReading(context);

        Assert.Empty(results);
    }

    [Fact]
    public void GetContinueReading_SortsByMostRecentlyOpened()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int olderSeriesId = SeedSeries(context, "Older");
        SeedIssue(context, olderSeriesId, lastPageRead: 10, pageCount: 100, openedTime: new DateTime(2026, 1, 1));
        int newerSeriesId = SeedSeries(context, "Newer");
        SeedIssue(context, newerSeriesId, lastPageRead: 10, pageCount: 100, openedTime: new DateTime(2026, 6, 1));

        var results = HomeFeedResolver.GetContinueReading(context);

        Assert.Equal(new[] { newerSeriesId, olderSeriesId }, results.Select(c => c.Series.Id));
    }

    // --- GetRecentlyAdded ---

    [Fact]
    public void GetRecentlyAdded_SortsByNewestIssuesAddedTime()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int olderSeriesId = SeedSeries(context, "Older");
        SeedIssue(context, olderSeriesId, addedTime: new DateTime(2026, 1, 1));
        int newerSeriesId = SeedSeries(context, "Newer");
        SeedIssue(context, newerSeriesId, addedTime: new DateTime(2026, 6, 1));

        var results = HomeFeedResolver.GetRecentlyAdded(context);

        Assert.Equal(new[] { newerSeriesId, olderSeriesId }, results.Select(s => s.Id));
    }

    [Fact]
    public void GetRecentlyAdded_ExcludesSeriesWithNoIssues()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        SeedSeries(context, "Empty Series");

        var results = HomeFeedResolver.GetRecentlyAdded(context);

        Assert.Empty(results);
    }

    // --- GetRecentlyOpenedSeriesIds ---

    [Fact]
    public void GetRecentlyOpenedSeriesIds_SortsByMostRecentlyOpened_AndRespectsCount()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int neverOpenedId = SeedSeries(context, "Never Opened");
        SeedIssue(context, neverOpenedId, openedTime: null);
        int oldestId = SeedSeries(context, "Oldest");
        SeedIssue(context, oldestId, openedTime: new DateTime(2026, 1, 1));
        int middleId = SeedSeries(context, "Middle");
        SeedIssue(context, middleId, openedTime: new DateTime(2026, 3, 1));
        int newestId = SeedSeries(context, "Newest");
        SeedIssue(context, newestId, openedTime: new DateTime(2026, 6, 1));

        var results = HomeFeedResolver.GetRecentlyOpenedSeriesIds(context, count: 2);

        Assert.Equal(new[] { newestId, middleId }, results);
    }

    // --- GetSpotlightPicks ---

    [Fact]
    public void GetSpotlightPicks_FirstPickFavorsUnreadIssueMatchingReadGenres()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Series");
        SeedIssue(context, seriesId, lastPageRead: 100, pageCount: 100, genre: "Action"); // read - feeds the frequency map
        int matchingUnreadId = SeedIssue(context, seriesId, lastPageRead: null, genre: "Action"); // matches the map
        SeedIssue(context, seriesId, lastPageRead: null, genre: "Romance"); // doesn't match - zero weight

        // count: 1 - with exactly one nonzero-weight candidate against one zero-weight candidate,
        // the weighted roll always lands in the matching candidate's cumulative slice regardless of
        // the RNG seed (the zero-weight one contributes a zero-width slice), so this is deterministic.
        var picks = HomeFeedResolver.GetSpotlightPicks(context, new Random(1), count: 1);

        var pick = Assert.Single(picks);
        Assert.Equal(matchingUnreadId, pick.Id);
    }

    [Fact]
    public void GetSpotlightPicks_NeverReturnsAnInProgressIssue()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Series");
        int inProgressId = SeedIssue(context, seriesId, lastPageRead: 30, pageCount: 100);

        var picks = HomeFeedResolver.GetSpotlightPicks(context, new Random(1));

        Assert.Empty(picks); // the only issue in the library is in progress, not a valid Spotlight candidate
        Assert.True(inProgressId > 0); // sanity: the seed actually created the issue
    }

    [Fact]
    public void GetSpotlightPicks_FallsBackToUniformRandom_WhenNoGenreSignalExists()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Series");
        int onlyCandidateId = SeedIssue(context, seriesId, lastPageRead: null, genre: null);

        var picks = HomeFeedResolver.GetSpotlightPicks(context, new Random(1));

        var pick = Assert.Single(picks);
        Assert.Equal(onlyCandidateId, pick.Id);
    }

    [Fact]
    public void GetSpotlightPicks_ReturnsEmpty_WhenNoUnreadIssuesExist()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Series");
        SeedIssue(context, seriesId, lastPageRead: 100, pageCount: 100);

        var picks = HomeFeedResolver.GetSpotlightPicks(context, new Random(1));

        Assert.Empty(picks);
    }

    [Fact]
    public void GetSpotlightPicks_RespectsCountLimit()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Series");
        for (int i = 0; i < 5; i++)
        {
            SeedIssue(context, seriesId, lastPageRead: null);
        }

        var picks = HomeFeedResolver.GetSpotlightPicks(context, new Random(1), count: 2);

        Assert.Equal(2, picks.Count);
    }

    [Fact]
    public void GetSpotlightPicks_NeverReturnsDuplicates_AndCapsAtAvailableCandidates()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Series");
        SeedIssue(context, seriesId, lastPageRead: null, genre: "Action");
        SeedIssue(context, seriesId, lastPageRead: null, genre: "Action");
        SeedIssue(context, seriesId, lastPageRead: null); // no genre - exercises the uniform-fallback tail

        var picks = HomeFeedResolver.GetSpotlightPicks(context, new Random(1), count: 10);

        Assert.Equal(3, picks.Count); // capped at the 3 available candidates despite asking for 10
        Assert.Equal(picks.Select(p => p.Id).Distinct().Count(), picks.Count); // no duplicates
    }

    // --- GetReadingListSpotlight ---

    [Fact]
    public void GetReadingListSpotlight_PicksAListWithAnUnreadItem()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Series");
        int issueId = SeedIssue(context, seriesId, lastPageRead: null);
        int listId = CreateReadingList(context, "My List", issueId);

        var pick = HomeFeedResolver.GetReadingListSpotlight(context, new Random(1));

        Assert.NotNull(pick);
        Assert.Equal(listId, pick!.Id);
    }

    [Fact]
    public void GetReadingListSpotlight_ExcludesListsWhereEverythingIsRead()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Series");
        int issueId = SeedIssue(context, seriesId, lastPageRead: 100, pageCount: 100);
        CreateReadingList(context, "Finished List", issueId);

        var pick = HomeFeedResolver.GetReadingListSpotlight(context, new Random(1));

        Assert.Null(pick);
    }

    [Fact]
    public void GetReadingListSpotlight_ReturnsNull_WhenNoReadingListsExist()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);

        var pick = HomeFeedResolver.GetReadingListSpotlight(context, new Random(1));

        Assert.Null(pick);
    }

    private static int CreateReadingList(PaperbunkrDbContext context, string name, int issueId)
    {
        var list = new ReadingList { Name = name };
        list.Items.Add(new ReadingListItem { IssueId = issueId, SortOrder = 0 });
        context.ReadingLists.Add(list);
        context.SaveChanges();
        return list.Id;
    }
}
