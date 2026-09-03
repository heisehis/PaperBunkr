using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddCoverAspectRatio</c> migration (docs/superpowers/specs/
/// 2026-09-03-panorama-variable-width-design.md) - a plain nullable <c>Issue</c> column, no data
/// fix. Panorama reads it to size each cover tile to its real aspect ratio without decoding.
/// </summary>
public class AddCoverAspectRatioMigrationTests : IDisposable
{
    private const string PriorMigration = "20260903133114_UnifyLibrarySortGroupFields";
    private readonly string _dbPath;

    public AddCoverAspectRatioMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_coveraspect_migration_test_{Guid.NewGuid():N}.db");
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

            Assert.Null(issue.CoverAspectRatio);
            issue.CoverAspectRatio = 1.5123;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(1.5123, context.Issues.Find(issueId)!.CoverAspectRatio!.Value, precision: 4);
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);

            var columns = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Issues') WHERE name = 'CoverAspectRatio';")
                .ToList();
            Assert.Empty(columns);
        }
    }
}
