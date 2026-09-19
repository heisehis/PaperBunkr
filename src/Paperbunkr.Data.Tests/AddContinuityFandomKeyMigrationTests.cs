using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddContinuityFandomKey</c> migration (docs/superpowers/specs/2026-09-17-
/// storyevent-continuity-autopopulate-design.md follow-up) - one new nullable column
/// (<c>Continuity.FandomKey</c>) and one new dismissal table, both additive, no data fix needed.
/// </summary>
public class AddContinuityFandomKeyMigrationTests : IDisposable
{
    private const string PriorMigration = "20260917064541_AddStoryEventContinuityAutoPopulationSchema";
    private readonly string _dbPath;

    public AddContinuityFandomKeyMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_continuity_fandomkey_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsNullableColumn_ToExistingRow_AndCreatesDismissalTable()
    {
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);
            context.Database.ExecuteSqlRaw("INSERT INTO Series (Name, ContentType, ReadingMode) VALUES ('Spider-Man 2099', 'Unknown', 'LeftToRight');");
            context.Database.ExecuteSqlRaw("INSERT INTO Continuities (Name, CreatedAt, UpdatedAt) VALUES ('Earth-928', '2026-09-17', '2026-09-17');");
            migrator.Migrate();
        }

        using (var context = CreateContext())
        {
            var continuity = context.Continuities.Single();
            Assert.Null(continuity.FandomKey);

            continuity.FandomKey = "Earth-928";
            context.SaveChanges();

            var series = context.Series.Single();
            context.ContinuityFandomSuggestionDismissals.Add(new ContinuityFandomSuggestionDismissal { SeriesId = series.Id, FandomKey = "Earth-928" });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal("Earth-928", context.Continuities.Single().FandomKey);
            Assert.Single(context.ContinuityFandomSuggestionDismissals);

            var indexes = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_index_list('ContinuityFandomSuggestionDismissals') WHERE \"unique\" = 1;")
                .ToList();
            Assert.NotEmpty(indexes);
        }
    }

    [Fact]
    public void Migration_IsReversible_DroppingColumnAndTable()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);

            var tables = context.Database
                .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='table' AND name='ContinuityFandomSuggestionDismissals';")
                .ToList();
            Assert.Empty(tables);

            var columns = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Continuities') WHERE name = 'FandomKey';")
                .ToList();
            Assert.Empty(columns);
        }
    }
}
