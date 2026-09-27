using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="PublisherResolver"/> (docs/superpowers/specs/2026-09-23-metron-api-utilization-
/// design.md) against a real SQLite database.
/// </summary>
public class PublisherResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public PublisherResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_publisher_test_{Guid.NewGuid():N}.db");
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
    public void SyncIssue_SetsPublisherEntityId()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = "Test Series" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", Publisher = "DC Comics" };
        context.Issues.Add(issue);
        context.SaveChanges();

        PublisherResolver.SyncIssue(context, issue.Id);

        var reloaded = context.Issues.Include(i => i.PublisherEntity).Single(i => i.Id == issue.Id);
        Assert.NotNull(reloaded.PublisherEntityId);
        Assert.Equal("DC Comics", reloaded.PublisherEntity!.Name);
    }

    [Fact]
    public void SyncSeries_PrefersIssuePublisherOverSeriesPublisher()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = "Test Series", Publisher = "Stale CE Publisher" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", Publisher = "DC Comics" };
        context.Issues.Add(issue);
        context.SaveChanges();

        PublisherResolver.SyncSeries(context, series.Id);

        var reloaded = context.Series.Include(s => s.PublisherEntity).Single(s => s.Id == series.Id);
        Assert.Equal("DC Comics", reloaded.PublisherEntity!.Name);
    }

    [Fact]
    public void GetOrCreate_IsCaseInsensitive()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var first = PublisherResolver.GetOrCreate(context, "Marvel");
        var second = PublisherResolver.GetOrCreate(context, "MARVEL");

        Assert.Equal(first.Id, second.Id);
        Assert.Single(context.Publishers);
    }
}
