using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="ExternalMetadataResolver"/> (docs/superpowers/specs/2026-08-17-metadata-
/// model-phase5a-external-metadata-schema-design.md) against a real SQLite database - same
/// rationale as <see cref="MediaRelationResolverTests"/>/<see cref="ContinuityResolverTests"/>.
/// Read-only resolver, no adapter exists yet, so all rows here are seeded directly.
/// </summary>
public class ExternalMetadataResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ExternalMetadataResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_externalmetadata_test_{Guid.NewGuid():N}.db");
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

    [Fact]
    public void GetExternalIds_NoRows_ReturnsEmpty()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Series A");

        var result = ExternalMetadataResolver.GetExternalIds(context, seriesId);

        Assert.Empty(result);
    }

    [Fact]
    public void GetExternalIds_ScopedToQueriedSeriesOnly()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesAId = SeedSeries(context, "Series A");
        int seriesBId = SeedSeries(context, "Series B");
        context.ExternalMediaIds.AddRange(
            new ExternalMediaId { SeriesId = seriesAId, Provider = ExternalMetadataProvider.AniList, ExternalId = "123" },
            new ExternalMediaId { SeriesId = seriesBId, Provider = ExternalMetadataProvider.AniList, ExternalId = "456" });
        context.SaveChanges();

        var result = ExternalMetadataResolver.GetExternalIds(context, seriesAId);

        var entry = Assert.Single(result);
        Assert.Equal("123", entry.ExternalId);
    }

    [Fact]
    public void GetRatings_ScopedToQueriedSeriesOnly()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesAId = SeedSeries(context, "Series A");
        int seriesBId = SeedSeries(context, "Series B");
        context.ExternalRatings.AddRange(
            new ExternalRating { SeriesId = seriesAId, Provider = ExternalMetadataProvider.MyAnimeList, Value = 8.5m, Scale = 10m, RetrievedAt = DateTime.UtcNow },
            new ExternalRating { SeriesId = seriesBId, Provider = ExternalMetadataProvider.MyAnimeList, Value = 7.0m, Scale = 10m, RetrievedAt = DateTime.UtcNow });
        context.SaveChanges();

        var result = ExternalMetadataResolver.GetRatings(context, seriesAId);

        var entry = Assert.Single(result);
        Assert.Equal(8.5m, entry.Value);
    }

    [Fact]
    public void GetLatestSnapshot_ReturnsMostRecentForProvider()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Series A");
        context.ExternalMetadataSnapshots.AddRange(
            new ExternalMetadataSnapshot { SeriesId = seriesId, Provider = ExternalMetadataProvider.AniList, ExternalId = "1", RetrievedAt = new DateTime(2026, 1, 1), Payload = "{}", SchemaVersion = "1" },
            new ExternalMetadataSnapshot { SeriesId = seriesId, Provider = ExternalMetadataProvider.AniList, ExternalId = "1", RetrievedAt = new DateTime(2026, 6, 1), Payload = "{\"newer\":true}", SchemaVersion = "1" });
        context.SaveChanges();

        var result = ExternalMetadataResolver.GetLatestSnapshot(context, seriesId, ExternalMetadataProvider.AniList);

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2026, 6, 1), result!.RetrievedAt);
    }

    [Fact]
    public void GetLatestSnapshot_NoSnapshotForThatProvider_ReturnsNull_EvenWhenOtherProvidersHaveSnapshots()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Series A");
        context.ExternalMetadataSnapshots.Add(
            new ExternalMetadataSnapshot { SeriesId = seriesId, Provider = ExternalMetadataProvider.AniList, ExternalId = "1", RetrievedAt = DateTime.UtcNow, Payload = "{}", SchemaVersion = "1" });
        context.SaveChanges();

        var result = ExternalMetadataResolver.GetLatestSnapshot(context, seriesId, ExternalMetadataProvider.MyAnimeList);

        Assert.Null(result);
    }

    [Fact]
    public void DeletingSeries_CascadesAllThreeTables()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Series A");
        context.ExternalMediaIds.Add(new ExternalMediaId { SeriesId = seriesId, Provider = ExternalMetadataProvider.AniList, ExternalId = "1" });
        context.ExternalMetadataSnapshots.Add(new ExternalMetadataSnapshot { SeriesId = seriesId, Provider = ExternalMetadataProvider.AniList, ExternalId = "1", RetrievedAt = DateTime.UtcNow, Payload = "{}", SchemaVersion = "1" });
        context.ExternalRatings.Add(new ExternalRating { SeriesId = seriesId, Provider = ExternalMetadataProvider.AniList, Value = 8m, Scale = 10m, RetrievedAt = DateTime.UtcNow });
        context.SaveChanges();

        context.Series.Remove(context.Series.Single());
        context.SaveChanges();

        Assert.Empty(context.ExternalMediaIds);
        Assert.Empty(context.ExternalMetadataSnapshots);
        Assert.Empty(context.ExternalRatings);
    }
}
