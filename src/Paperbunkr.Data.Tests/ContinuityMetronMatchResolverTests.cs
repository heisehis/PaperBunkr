using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="ContinuityMetronMatchResolver"/> (docs/superpowers/specs/2026-09-23-metron-
/// api-utilization-design.md) against a real SQLite database.
/// </summary>
public class ContinuityMetronMatchResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ContinuityMetronMatchResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_continuitymetron_test_{Guid.NewGuid():N}.db");
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
    public void SyncFromIssueDetails_MetronProvider_CreatesContinuityAndMembership()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Captain America");

        ContinuityMetronMatchResolver.SyncFromIssueDetails(context, seriesId, ComicProvider.Metron,
            new[] { new ComicVineIdName(21, "Earth-616") });

        var continuity = context.Continuities.Single();
        Assert.Equal("Earth-616", continuity.Name);
        Assert.Equal("21", continuity.MetronId);
        Assert.True(context.ContinuityMemberships.Any(m => m.ContinuityId == continuity.Id && m.SeriesId == seriesId));
    }

    [Fact]
    public void SyncFromIssueDetails_ComicVineProvider_IsANoOp()
    {
        // ComicVine never populates Universes (confirmed - no such concept there), but this also
        // guards against a future caller passing a non-empty list for the wrong provider.
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Captain America");

        ContinuityMetronMatchResolver.SyncFromIssueDetails(context, seriesId, ComicProvider.ComicVine,
            new[] { new ComicVineIdName(21, "Earth-616") });

        Assert.Empty(context.Continuities);
    }

    [Fact]
    public void SyncFromIssueDetails_ExistingWikidataContinuity_MetronTagWinsAndAddsItsId()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Captain America");
        context.Continuities.Add(new Continuity { Name = "Earth-616", WikidataId = "Q2246088", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        context.SaveChanges();

        ContinuityMetronMatchResolver.SyncFromIssueDetails(context, seriesId, ComicProvider.Metron,
            new[] { new ComicVineIdName(21, "Earth-616") });

        var continuity = context.Continuities.Single();
        Assert.Equal("Q2246088", continuity.WikidataId);   // not clobbered
        Assert.Equal("21", continuity.MetronId);            // Metron's own id added alongside it
    }
}
