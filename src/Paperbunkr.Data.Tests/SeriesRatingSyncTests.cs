using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// <see cref="Series.Rating"/> follows its issues' ratings on every save (<c>PaperbunkrDbContext.QueueSeriesRatingRecompute</c>), and the
/// one-time <see cref="SeriesRatingBackfill"/> fills series rated before that existed. Without these, Insights' "You vs Community" saw only
/// series whose score had been pulled from a tracker.
/// </summary>
public class SeriesRatingSyncTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_series_rating_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public SeriesRatingSyncTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
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

    private PaperbunkrDbContext Open() => new(_options);

    private (int SeriesId, int[] IssueIds) AddSeries(string name, params float?[] ratings)
    {
        using var context = Open();
        var series = new Series { Name = name };
        context.Series.Add(series);
        context.SaveChanges();
        var issues = ratings.Select((r, n) => new Issue { SeriesId = series.Id, Number = (n + 1).ToString(), Rating = r }).ToList();
        context.Issues.AddRange(issues);
        context.SaveChanges();
        return (series.Id, issues.Select(i => i.Id).ToArray());
    }

    private float? SeriesRating(int seriesId)
    {
        using var context = Open();
        return context.Series.AsNoTracking().Single(s => s.Id == seriesId).Rating;
    }

    private void SetIssueRating(int issueId, float? rating)
    {
        using var context = Open();
        context.Issues.Single(i => i.Id == issueId).Rating = rating;
        context.SaveChanges();
    }

    [Fact]
    public void AddingRatedIssues_SetsTheAverage()
    {
        var (seriesId, _) = AddSeries("Saga", 4f, 2f, null);

        Assert.Equal(3f, SeriesRating(seriesId));
    }

    [Fact]
    public void AddingSeriesAndRatedIssueInOneSave_SetsTheRating()
    {
        int seriesId;
        using (var context = Open())
        {
            var series = new Series { Name = "Ordeal" };
            series.Issues.Add(new Issue { Number = "1", Rating = 5f });
            context.Series.Add(series);
            context.SaveChanges();
            seriesId = series.Id;
        }

        Assert.Equal(5f, SeriesRating(seriesId));
    }

    [Fact]
    public void RatingAnIssue_UpdatesTheSeries_AndClearingTheLastRatingClearsIt()
    {
        var (seriesId, issues) = AddSeries("Kagurabachi", null, null);
        Assert.Null(SeriesRating(seriesId));

        SetIssueRating(issues[0], 4f);
        Assert.Equal(4f, SeriesRating(seriesId));

        SetIssueRating(issues[1], 5f);
        Assert.Equal(4.5f, SeriesRating(seriesId));

        SetIssueRating(issues[0], null);
        SetIssueRating(issues[1], null);
        Assert.Null(SeriesRating(seriesId));
    }

    [Fact]
    public void ZeroStars_CountsAsUnrated()
    {
        var (seriesId, issues) = AddSeries("Capes", 4f, null);

        SetIssueRating(issues[1], 0f);

        Assert.Equal(4f, SeriesRating(seriesId));
    }

    [Fact]
    public void MovingARatedIssue_RecomputesBothSeries()
    {
        var (from, issues) = AddSeries("From", 5f, 3f);
        var (to, _) = AddSeries("To", 1f);

        using (var context = Open())
        {
            context.Issues.Single(i => i.Id == issues[0]).SeriesId = to;
            context.SaveChanges();
        }

        Assert.Equal(3f, SeriesRating(from));
        Assert.Equal(3f, SeriesRating(to));
    }

    [Fact]
    public void DeletingARatedIssue_RecomputesTheSeries()
    {
        var (seriesId, issues) = AddSeries("Reality Quest", 5f, 3f);

        using (var context = Open())
        {
            context.Issues.Remove(context.Issues.Single(i => i.Id == issues[0]));
            context.SaveChanges();
        }

        Assert.Equal(3f, SeriesRating(seriesId));
    }

    [Fact]
    public void UnrelatedIssueEdit_KeepsATrackerScore()
    {
        var (seriesId, issues) = AddSeries("30 Years Since the Prologue", 4f);
        using (var context = Open())
        {
            context.Series.Single(s => s.Id == seriesId).Rating = 4.6f; // "Use this score"
            context.SaveChanges();
        }

        using (var context = Open())
        {
            context.Issues.Single(i => i.Id == issues[0]).Title = "Prologue";
            context.SaveChanges();
        }

        Assert.Equal(4.6f, SeriesRating(seriesId));
    }

    [Fact]
    public void Backfill_FillsUnratedSeries_AndKeepsExistingRatings()
    {
        var (filled, _) = AddSeries("Filled", 5f, 4f);
        var (kept, _) = AddSeries("Kept", 4f);
        var (none, _) = AddSeries("None", null, 0f);
        using (var context = Open())
        {
            // Simulate the state before the save-time sync existed.
            context.Database.ExecuteSqlRaw("""UPDATE "Series" SET "Rating" = NULL WHERE "Id" IN ({0}, {1})""", filled, none);
            context.Database.ExecuteSqlRaw("""UPDATE "Series" SET "Rating" = 4.6 WHERE "Id" = {0}""", kept);
            foreach (var sql in SeriesRatingBackfill.Statements)
            {
                context.Database.ExecuteSqlRaw(sql);
                context.Database.ExecuteSqlRaw(sql);
            }
        }

        Assert.Equal(4.5f, SeriesRating(filled));
        Assert.Equal(4.6f, SeriesRating(kept));
        Assert.Null(SeriesRating(none));
    }
}
