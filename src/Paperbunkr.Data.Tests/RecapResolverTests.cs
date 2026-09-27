using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="RecapResolver"/> (docs/superpowers/specs/2026-09-23-insights-year-in-review-recap-
/// design.md). Reuses <see cref="InsightsResolverTests"/>'s seed helpers - same fixture shape as
/// <see cref="StatsResolverTests"/>.
/// </summary>
public class RecapResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    public RecapResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_recap_test_{Guid.NewGuid():N}.db");
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

    /// <summary>Local midnight of <paramref name="year"/>-01-01, converted to the UTC instant it
    /// represents on this machine - used for boundary-precision tests instead of a fixed UTC offset,
    /// since <see cref="RecapResolver"/> buckets everything by local calendar day.</summary>
    private static DateTime LocalNewYearUtc(int year)
        => TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeZoneInfo.Local);

    private static Book SeedBook(PaperbunkrDbContext ctx, string title, string? author)
    {
        var b = new Book { Title = title, Author = author, FilePath = $"C:/books/{title}.epub", AddedTime = Now };
        ctx.Books.Add(b);
        ctx.SaveChanges();
        return b;
    }

    [Fact]
    public void ItemsFinishedAndPagesRead_CountFinishedEventsInYear()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var i1 = InsightsResolverTests.SeedIssue(ctx, series.Id);
        var i2 = InsightsResolverTests.SeedIssue(ctx, series.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, i1.Id, ReadingEventKind.Finished, Now, pages: 22);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, i2.Id, ReadingEventKind.Finished, Now.AddDays(-10), pages: 30);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, i1.Id, ReadingEventKind.Finished, Now.AddYears(-1), pages: 99); // prior year, excluded

        var snap = RecapResolver.Build(ctx, 2026, Now);
        Assert.Equal(2, snap.ItemsFinished);
        Assert.Equal(52, snap.PagesRead);
    }

    [Fact]
    public void LongestStreakDays_CountsConsecutiveLocalDaysWithinYear()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Opened, Now);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Opened, Now.AddDays(-1));
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Opened, Now.AddDays(-2));
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Opened, Now.AddDays(-10)); // isolated day

        var snap = RecapResolver.Build(ctx, 2026, Now);
        Assert.Equal(3, snap.LongestStreakDays);
    }

    [Fact]
    public void LongestStreakDays_DoesNotCrossYearBoundary()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);

        var newYear = LocalNewYearUtc(2027); // local Jan 1 2027 00:00:00
        // Dec 30, 31 of 2026 and Jan 1, 2 of 2027 - a 4-day lifetime streak spanning the boundary.
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Opened, newYear.AddDays(-2));
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Opened, newYear.AddDays(-1));
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Opened, newYear);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Opened, newYear.AddDays(1));

        Assert.Equal(2, RecapResolver.Build(ctx, 2026, Now).LongestStreakDays);
        Assert.Equal(2, RecapResolver.Build(ctx, 2027, Now).LongestStreakDays);
    }

    [Fact]
    public void BusiestDay_IsTheLocalDayWithMostPagesRead()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now, pages: 40);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now.AddDays(-5), pages: 300);

        var busiest = RecapResolver.Build(ctx, 2026, Now).BusiestDay;
        Assert.NotNull(busiest);
        Assert.Equal(300, busiest!.Pages);
        Assert.Equal(DateOnly.FromDateTime(Now.AddDays(-5).ToLocalTime().Date), busiest.Date);
    }

    [Fact]
    public void BusiestDay_IsNull_WhenNoPagesLoggedAllYear()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Opened, Now, pages: null);

        Assert.Null(RecapResolver.Build(ctx, 2026, Now).BusiestDay);
    }

    [Fact]
    public void TopSeries_TiesRenderAsAndNOthers()
    {
        using var ctx = NewContext();
        var saga = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var sandman = InsightsResolverTests.SeedSeries(ctx, "Sandman");
        var sagaIssue = InsightsResolverTests.SeedIssue(ctx, saga.Id);
        var sandmanIssue = InsightsResolverTests.SeedIssue(ctx, sandman.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, sagaIssue.Id, ReadingEventKind.Finished, Now);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, sandmanIssue.Id, ReadingEventKind.Finished, Now);

        var topSeries = RecapResolver.Build(ctx, 2026, Now).TopSeries;
        Assert.NotNull(topSeries);
        Assert.Equal(2, topSeries!.Titles.Count);
        Assert.Equal("Saga and 1 other", topSeries.DisplayTitle);
        // Cover-key identity (docs/superpowers/specs/2026-09-23-insights-redesign-design.md) - the
        // first tied series, matching DisplayTitle's own "Saga and 1 other" convention.
        Assert.Equal(saga.Id, topSeries.SeriesId);
    }

    [Fact]
    public void TopWriter_CombinesComicWriterAndBookAuthor()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);
        issue.Writer = "Brian K. Vaughan";
        ctx.SaveChanges();
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now);

        var book = SeedBook(ctx, "Dune", "Frank Herbert");
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Novel, book.Id, ReadingEventKind.Finished, Now);

        var topWriter = RecapResolver.Build(ctx, 2026, Now).TopWriter;
        Assert.NotNull(topWriter);
        Assert.Equal(2, topWriter!.Titles.Count);
        Assert.Contains("Brian K. Vaughan", topWriter.Titles);
        Assert.Contains("Frank Herbert", topWriter.Titles);
    }

    [Fact]
    public void TopArtist_DedupesOnePersonAcrossRolesOnSameIssue()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);
        issue.Penciller = "Jim Lee";
        issue.Inker = "Jim Lee";
        ctx.SaveChanges();
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now);

        var topArtist = RecapResolver.Build(ctx, 2026, Now).TopArtist;
        Assert.NotNull(topArtist);
        Assert.Equal(new[] { "Jim Lee" }, topArtist!.Titles);
        Assert.Equal("1 credited", topArtist.Detail);
    }

    [Fact]
    public void HighestRatedSeries_AveragesRatedIssuesFinishedInYear()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var issue = InsightsResolverTests.SeedIssue(ctx, series.Id, rating: 5f);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now);

        var highest = RecapResolver.Build(ctx, 2026, Now).HighestRatedSeries;
        Assert.NotNull(highest);
        Assert.Equal("Saga", highest!.Titles.Single());
        Assert.Equal("Score: 5", highest.Detail);
        Assert.Equal(series.Id, highest.SeriesId);
    }

    [Fact]
    public void MostRereadItem_CountsTwoOrMoreFinishesInYear()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now.AddDays(-30));

        var reread = RecapResolver.Build(ctx, 2026, Now).MostRereadItem;
        Assert.NotNull(reread);
        Assert.Equal("Saga", reread!.Titles.Single());
        Assert.Equal("2 rereads", reread.Detail);
        Assert.Equal(issue.Id, reread.IssueId);
    }

    [Fact]
    public void MostRereadItem_Tie_UsesFirstTiedComicIssueId()
    {
        using var ctx = NewContext();
        var saga = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var sandman = InsightsResolverTests.SeedSeries(ctx, "Sandman");
        var sagaIssue = InsightsResolverTests.SeedIssue(ctx, saga.Id);
        var sandmanIssue = InsightsResolverTests.SeedIssue(ctx, sandman.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, sagaIssue.Id, ReadingEventKind.Finished, Now);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, sagaIssue.Id, ReadingEventKind.Finished, Now.AddDays(-30));
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, sandmanIssue.Id, ReadingEventKind.Finished, Now);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, sandmanIssue.Id, ReadingEventKind.Finished, Now.AddDays(-30));

        var reread = RecapResolver.Build(ctx, 2026, Now).MostRereadItem;
        Assert.NotNull(reread);
        Assert.Equal(2, reread!.Titles.Count);
        Assert.Equal(sagaIssue.Id, reread.IssueId); // first tied comic issue, matching DisplayTitle's own "first" convention
    }

    [Fact]
    public void EmptyTiles_DoNotThrow_AndOtherFieldsStillPopulate()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now, pages: 20);

        var snap = RecapResolver.Build(ctx, 2026, Now);
        Assert.Equal(1, snap.ItemsFinished);
        Assert.Null(snap.MostRereadItem);
        Assert.Null(snap.HighestRatedSeries);
    }

    [Fact]
    public void AvailableYears_IncludesCurrentPartialYear_ExcludesYearsWithNoEvents()
    {
        using var ctx = NewContext();
        var series = InsightsResolverTests.SeedSeries(ctx, "Saga");
        var issue = InsightsResolverTests.SeedIssue(ctx, series.Id);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now);
        InsightsResolverTests.SeedEvent(ctx, ReadingItemType.Comic, issue.Id, ReadingEventKind.Finished, Now.AddYears(-2));

        var years = RecapResolver.AvailableYears(ctx, Now);
        Assert.Equal(new[] { 2026, 2024 }, years);
    }

    [Fact]
    public void Build_ForYearWithNoEvents_ReturnsZeroedSnapshot()
    {
        using var ctx = NewContext();
        var snap = RecapResolver.Build(ctx, 2020, Now);
        Assert.Equal(0, snap.ItemsFinished);
        Assert.Equal(0, snap.PagesRead);
        Assert.Equal(0, snap.LongestStreakDays);
        Assert.Null(snap.BusiestDay);
        Assert.Null(snap.TopSeries);
    }
}
