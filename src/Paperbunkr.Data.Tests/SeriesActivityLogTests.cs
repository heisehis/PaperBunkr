using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>Exercises <see cref="SeriesActivityLog"/> (docs/superpowers/specs/2026-09-13-activity-
/// tab-event-log-expansion-design.md) against a real SQLite database, same rationale as
/// <see cref="ContinuityResolverTests"/>.</summary>
public class SeriesActivityLogTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public SeriesActivityLogTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_activitylog_test_{Guid.NewGuid():N}.db");
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

    [Fact]
    public void Record_InsertsRowWithGivenFields()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            SeriesActivityLog.Record(context, seriesId: 7, SeriesActivityEventKind.TrackerLinked, "AniList", issueId: null);
            context.SaveChanges();
        }

        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var row = Assert.Single(context.SeriesActivityEvents);
            Assert.Equal(7, row.SeriesId);
            Assert.Equal(SeriesActivityEventKind.TrackerLinked, row.Kind);
            Assert.Equal("AniList", row.Detail);
            Assert.Null(row.IssueId);
            Assert.True((DateTime.UtcNow - row.TimestampUtc).TotalMinutes < 1);
        }
    }

    [Fact]
    public void Record_WithIssueId_PersistsIt()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            SeriesActivityLog.Record(context, seriesId: 3, SeriesActivityEventKind.RatingChanged, "Rating set to 4", issueId: 42);
            context.SaveChanges();
        }

        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var row = Assert.Single(context.SeriesActivityEvents);
            Assert.Equal(42, row.IssueId);
        }
    }

    [Fact]
    public void Record_DoesNotSaveByItself()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        SeriesActivityLog.Record(context, seriesId: 1, SeriesActivityEventKind.MetadataLinked, "AniList");

        using var verify = new PaperbunkrDbContext(_dbOptions);
        Assert.Empty(verify.SeriesActivityEvents);
    }
}
