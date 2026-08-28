using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="SeriesFamilyResolver"/> (docs/superpowers/specs/2026-08-27-metadata-model-
/// phase4g-age-progression-design.md) against a real SQLite database.
/// </summary>
public class SeriesFamilyResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public SeriesFamilyResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_seriesfamily_test_{Guid.NewGuid():N}.db");
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

    private static void Relate(PaperbunkrDbContext context, int sourceId, int targetId, RelationType type)
    {
        var relation = new MediaRelation { SourceSeriesId = sourceId, TargetSeriesId = targetId, RelationType = type };
        relation.Evidence.Add(new RelationEvidence { MediaRelation = relation, Provider = RelationEvidenceProvider.User, Confidence = 1.0m });
        context.MediaRelations.Add(relation);
        context.SaveChanges();
    }

    [Fact]
    public void Family_IncludesSeriesReachableByMultiHopMediaRelationChain()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int a = SeedSeries(context, "A");
        int b = SeedSeries(context, "B");
        int c = SeedSeries(context, "C");
        Relate(context, a, b, RelationType.Sequel);
        Relate(context, b, c, RelationType.SpinOff);

        var family = SeriesFamilyResolver.GetFamily(context, a).Select(s => s.Id).ToList();

        Assert.Equal(new[] { a, b, c }.OrderBy(x => x), family.OrderBy(x => x));
    }

    [Fact]
    public void Family_MutualRelationCycle_DoesNotInfiniteLoop()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int a = SeedSeries(context, "A");
        int b = SeedSeries(context, "B");
        Relate(context, a, b, RelationType.Related);
        Relate(context, b, a, RelationType.Similar);

        var family = SeriesFamilyResolver.GetFamily(context, a).Select(s => s.Id).ToList();

        Assert.Equal(2, family.Count);
    }

    [Fact]
    public void Family_IncludesSeriesSharingOnlyAContinuity_NoDirectMediaRelation()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int a = SeedSeries(context, "A");
        int b = SeedSeries(context, "B");
        var continuity = ContinuityResolver.GetOrCreate(context, "Earth-616");
        ContinuityResolver.AddSeriesToContinuity(context, a, continuity.Id);
        ContinuityResolver.AddSeriesToContinuity(context, b, continuity.Id);

        var family = SeriesFamilyResolver.GetFamily(context, a).Select(s => s.Id).ToList();

        Assert.Contains(b, family);
    }

    [Fact]
    public void Family_SeriesWithNoRelationsOrContinuities_ReturnsJustItself()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int a = SeedSeries(context, "A");
        SeedSeries(context, "Unrelated");

        var family = SeriesFamilyResolver.GetFamily(context, a);

        Assert.Single(family);
        Assert.Equal(a, family[0].Id);
    }
}
