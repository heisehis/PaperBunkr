using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddReaderBackgroundTextureAndSpreadPosition</c> migration (docs/superpowers/specs/
/// 2026-09-10-reader-backlog-batch-b-design.md) - two plain nullable column adds
/// (<c>AppSettings.BackgroundTexture</c>, <c>IssuePages.SpreadPosition</c>), no data fix, no
/// sentinel needed for either (both nullable, null is a real "not set" value). <c>Down</c> is a
/// deliberate no-op (see the migration's comment) - same reasoning as every other AppSettings/
/// IssuePage column migration: a <c>DropColumn</c> would trigger SQLite's full-table rebuild,
/// silently dropping the orphaned <c>LibraryGroupField</c> family of columns and breaking a later
/// <c>Down()</c> step in a rollback chain.
/// </summary>
public class AddReaderBackgroundTextureAndSpreadPositionMigrationTests : IDisposable
{
    private const string PriorMigration = "20260909221449_AddReaderMemoryLimitMb";
    private readonly string _dbPath;

    public AddReaderBackgroundTextureAndSpreadPositionMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_readertextureandspread_migration_test_{Guid.NewGuid():N}.db");
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
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        return new PaperbunkrDbContext(options);
    }

    [Fact]
    public void Migration_AddsBothNullableColumns_ThatRoundTrip_AndDownIsANoOp()
    {
        // Up to HEAD: AppSettings.BackgroundTexture and IssuePages.SpreadPosition both land as
        // nullable, defaulting to null (Auto/first-texture and PageSpreadPosition.Default
        // respectively, per each entity's own doc comment - null is a real value here, not CLR-
        // default ambiguity, so no HasSentinel needed).
        int issueId;
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var settings = context.GetOrCreateAppSettings();
            Assert.Null(settings.BackgroundTexture);

            settings.BackgroundTexture = "carbon";
            context.SaveChanges();

            var series = new Series { Name = "Migration Test Series" };
            context.Series.Add(series);
            context.SaveChanges();
            var issue = new Issue { SeriesId = series.Id, FilePath = "migration-test.cbz" };
            context.Issues.Add(issue);
            context.SaveChanges();
            issueId = issue.Id;

            var page = new IssuePage { IssueId = issueId, PageNumber = 3, SpreadPosition = PageSpreadPosition.Near };
            context.IssuePages.Add(page);
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal("carbon", context.GetOrCreateAppSettings().BackgroundTexture);
            var page = context.IssuePages.Single(p => p.IssueId == issueId && p.PageNumber == 3);
            Assert.Equal(PageSpreadPosition.Near, page.SpreadPosition);
        }

        // Down one step: Down() is a deliberate no-op, so both columns stay put (left as orphans)
        // and the existing rows are untouched.
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);

            var appSettingsColumn = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('AppSettings') WHERE name = 'BackgroundTexture';")
                .ToList();
            Assert.Single(appSettingsColumn);

            var issuePagesColumn = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('IssuePages') WHERE name = 'SpreadPosition';")
                .ToList();
            Assert.Single(issuePagesColumn);

            var issuePageRowCount = context.Database
                .SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM IssuePages")
                .Single();
            Assert.Equal(1, issuePageRowCount);
        }
    }
}
