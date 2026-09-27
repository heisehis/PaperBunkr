using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="SeriesAssociationSync"/> (docs/superpowers/specs/2026-09-23-metron-api-
/// utilization-design.md) against a real SQLite database.
/// </summary>
public class SeriesAssociationSyncTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public SeriesAssociationSyncTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_seriesassoc_test_{Guid.NewGuid():N}.db");
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

    private static int SeedSeries(PaperbunkrDbContext context)
    {
        var series = new Series { Name = "Test Series" };
        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    [Fact]
    public void Sync_CreatesAssociations()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context);

        SeriesAssociationSync.Sync(context, seriesId, new[] { new ComicVineIdName(42, "Related Series") });

        var association = context.SeriesAssociations.Single();
        Assert.Equal(seriesId, association.SeriesId);
        Assert.Equal("42", association.ExternalSeriesId);
        Assert.Equal("Related Series", association.Name);
    }

    [Fact]
    public void Sync_RemovesStaleAssociations()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context);
        SeriesAssociationSync.Sync(context, seriesId, new[] { new ComicVineIdName(42, "Related Series"), new ComicVineIdName(43, "Another") });

        SeriesAssociationSync.Sync(context, seriesId, new[] { new ComicVineIdName(42, "Related Series") });

        Assert.Single(context.SeriesAssociations);
    }

    [Fact]
    public void Sync_RunTwice_IsIdempotent()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context);

        SeriesAssociationSync.Sync(context, seriesId, new[] { new ComicVineIdName(42, "Related Series") });
        SeriesAssociationSync.Sync(context, seriesId, new[] { new ComicVineIdName(42, "Related Series") });

        Assert.Single(context.SeriesAssociations);
    }

    [Fact]
    public void Sync_IgnoresEntriesWithNoExternalId()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context);

        SeriesAssociationSync.Sync(context, seriesId, new[] { new ComicVineIdName(null, "Unidentified") });

        Assert.Empty(context.SeriesAssociations);
    }
}
