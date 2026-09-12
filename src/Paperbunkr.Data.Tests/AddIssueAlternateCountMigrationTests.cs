using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddIssueAlternateCount</c> migration (docs/superpowers/specs/
/// 2026-09-12-issue-alternate-count-design.md) - a plain nullable <c>Issue</c> column, no data fix,
/// real <c>DropColumn</c> on <c>Down()</c> (a brand-new column has no unmapped-orphan collateral-
/// damage risk, unlike the no-op-<c>Down()</c> migrations elsewhere in this schema). Rolls back only
/// one step (to the immediately-preceding migration), not the whole history - a broader multi-step
/// rollback currently hits an unrelated pre-existing bug in
/// <c>LibrarySortGroupAxesAndFinalIssueTriState</c> (see the design doc's finding), which this test
/// deliberately does not exercise.
/// </summary>
public class AddIssueAlternateCountMigrationTests : IDisposable
{
    private const string PriorMigration = "20260912051746_LibrarySortGroupAxesAndFinalIssueTriState";
    private readonly string _dbPath;

    public AddIssueAlternateCountMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_altcount_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsNullableColumn_ThatRoundTrips_AndIsReversible()
    {
        int issueId;
        using (var context = CreateContext())
        {
            context.Database.Migrate();

            var series = new Series { Name = "S" };
            context.Series.Add(series);
            context.SaveChanges();
            var issue = new Issue { SeriesId = series.Id, Number = "1" };
            context.Issues.Add(issue);
            context.SaveChanges();
            issueId = issue.Id;

            Assert.Null(issue.AlternateCount);
            issue.AlternateCount = 6;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(6, context.Issues.Find(issueId)!.AlternateCount);
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);

            var columns = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Issues') WHERE name = 'AlternateCount';")
                .ToList();
            Assert.Empty(columns);
        }
    }
}
