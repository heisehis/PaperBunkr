using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises the <see cref="SmartListField.Continuity"/> smart-list field (docs/superpowers/specs/
/// 2026-08-27-metadata-model-phase4f-continuity-browse-design.md's deferred continuity-scoped Smart
/// Lists).
/// </summary>
public class SmartListContinuityFieldTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public SmartListContinuityFieldTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_smartcontinuity_test_{Guid.NewGuid():N}.db");
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
    public void ContinuityCondition_MatchesIssuesWhoseSeriesIsInThatContinuity()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);

        var inUniverse = new Series { Name = "Avengers" };
        var outsider = new Series { Name = "Spawn" };
        context.Series.AddRange(inUniverse, outsider);
        context.SaveChanges();
        context.Issues.Add(new Issue { SeriesId = inUniverse.Id, Number = "1" });
        context.Issues.Add(new Issue { SeriesId = outsider.Id, Number = "1" });
        context.SaveChanges();

        var continuity = ContinuityResolver.GetOrCreate(context, "Earth-616");
        ContinuityResolver.AddSeriesToContinuity(context, inUniverse.Id, continuity.Id);

        var list = new SmartList
        {
            Name = "616 issues",
            Conditions = { new SmartListCondition { Field = SmartListField.Continuity, Operator = SmartListOperator.Contains, Value = "Earth-616" } },
        };
        context.SmartLists.Add(list);
        context.SaveChanges();

        var matches = SmartListQueryBuilder.Build(context, list);

        var match = Assert.Single(matches);
        Assert.Equal(inUniverse.Id, match.SeriesId);
    }

    [Fact]
    public void ContinuityDefinition_IsRegisteredInCatalog()
    {
        Assert.True(SmartListCatalog.Definitions.ContainsKey(SmartListField.Continuity));
        Assert.Equal("Continuity", SmartListCatalog.Definitions[SmartListField.Continuity].Label);
    }
}
