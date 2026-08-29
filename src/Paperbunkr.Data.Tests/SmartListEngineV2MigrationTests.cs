using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>SmartListEngineV2NestedGroups</c> migration's zero-data-loss shape
/// (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md §2/§5): every pre-v2 flat
/// smart list gets one <c>Mode = And</c>, <c>ParentGroupId = null</c> root group, every existing
/// condition is repointed at it with <c>Not = false</c> / <c>IgnoreCase = true</c>, and the
/// engine's result set for that data is unchanged.
/// </summary>
public class SmartListEngineV2MigrationTests : IDisposable
{
    private const string PriorMigration = "20260828135146_ContinuityMembershipJoinEntity";
    private readonly string _dbPath;

    public SmartListEngineV2MigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_smartv2_migration_{Guid.NewGuid():N}.db");
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

    private PaperbunkrDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        return new PaperbunkrDbContext(options);
    }

    [Fact]
    public void Migration_GivesEveryListAFlatAndRootGroup_WithConditionsRepointedAndDefaultsSet()
    {
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            // Series/Issues are unchanged between the prior migration and v2, so EF can insert them
            // against the current model. SmartLists/SmartListConditions change shape, so those go in
            // via raw SQL at the pre-v2 schema (SmartListConditions carries SmartListId directly).
            var series = new Entities.Series { Id = 1, Name = "Alpha", ContentType = Entities.ContentType.Comic, Status = Entities.SeriesStatus.Completed };
            context.Series.Add(series);
            context.Issues.Add(new Entities.Issue { Id = 1, SeriesId = 1, Number = "1", Publisher = "Acme" });
            context.Issues.Add(new Entities.Issue { Id = 2, SeriesId = 1, Number = "2", Publisher = "Zenith" });
            context.SaveChanges();

            context.Database.ExecuteSqlRaw("INSERT INTO SmartLists (Id, Name, IsSystem, SortOrder) VALUES (10, 'Acme books', 0, 0);");
            context.Database.ExecuteSqlRaw("INSERT INTO SmartLists (Id, Name, IsSystem, SortOrder) VALUES (20, 'Two rules', 0, 1);");
            context.Database.ExecuteSqlRaw("INSERT INTO SmartLists (Id, Name, IsSystem, SortOrder) VALUES (30, 'No rules', 0, 2);");

            context.Database.ExecuteSqlRaw("INSERT INTO SmartListConditions (Id, SmartListId, Field, Operator, Value, SortOrder) VALUES (100, 10, 'Publisher', 'Is', 'Acme', 0);");
            context.Database.ExecuteSqlRaw("INSERT INTO SmartListConditions (Id, SmartListId, Field, Operator, Value, SortOrder) VALUES (200, 20, 'ContentType', 'Is', 'Comic', 0);");
            context.Database.ExecuteSqlRaw("INSERT INTO SmartListConditions (Id, SmartListId, Field, Operator, Value, SortOrder) VALUES (201, 20, 'SeriesComplete', 'Is', 'true', 1);");

            migrator.Migrate();
        }

        using (var context = CreateContext())
        {
            var groups = context.SmartListConditionGroups.ToList();
            Assert.Equal(3, groups.Count); // exactly one root group per list, none nested
            Assert.All(groups, g =>
            {
                Assert.Equal(SmartListGroupMode.And, g.Mode);
                Assert.Null(g.ParentGroupId);
                Assert.NotNull(g.SmartListId);
            });

            var conditions = context.SmartListConditions.ToList();
            Assert.Equal(3, conditions.Count);
            Assert.All(conditions, c =>
            {
                Assert.False(c.Not);
                Assert.True(c.IgnoreCase);
                Assert.Null(c.SearchMode);
            });

            // Every condition's group belongs to the list the condition used to belong to.
            var groupById = groups.ToDictionary(g => g.Id);
            Assert.Equal(10, groupById[conditions.Single(c => c.Id == 100).GroupId].SmartListId);
            Assert.Equal(20, groupById[conditions.Single(c => c.Id == 200).GroupId].SmartListId);
            Assert.Equal(20, groupById[conditions.Single(c => c.Id == 201).GroupId].SmartListId);

            // ...and the engine still evaluates them (Acme list -> issue 1 only).
            var acmeList = SmartListTreeLoader.LoadWithTree(context, 10)!;
            Assert.Equal(new[] { 1 }, SmartListQueryBuilder.Build(context, acmeList).Select(i => i.Id));

            // The zero-condition list still round-trips to "matches everything".
            var noRules = SmartListTreeLoader.LoadWithTree(context, 30)!;
            Assert.Equal(2, SmartListQueryBuilder.Build(context, noRules).Count);
        }
    }
}
