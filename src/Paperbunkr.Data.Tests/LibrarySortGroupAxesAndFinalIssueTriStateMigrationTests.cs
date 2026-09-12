using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>LibrarySortGroupAxesAndFinalIssueTriState</c> migration against a real SQLite
/// database carrying pre-migration data (docs/superpowers/specs/2026-09-12-library-sort-group-axes-
/// design.md §4) - <c>Issue.IsFinalIssue</c>'s bool -> bool? widen is a table-rebuild under the hood
/// (unlike the two plain AppSettings ADD COLUMNs in the same migration), so existing True/False rows
/// surviving it unchanged needs real coverage, not just an assumption.
/// </summary>
public class LibrarySortGroupAxesAndFinalIssueTriStateMigrationTests : IDisposable
{
    private const string PriorMigration = "20260911011311_AddReaderBackgroundTextureAndSpreadPosition";
    private readonly string _dbPath;

    public LibrarySortGroupAxesAndFinalIssueTriStateMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_final_issue_tristate_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_PreservesExistingTrueFalseValues_AndNewIssuesDefaultToNull()
    {
        int trueIssueId, falseIssueId;
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            context.Database.ExecuteSql(
                $"INSERT INTO Series (Name, ContentType, ReadingMode, Status) VALUES ('Pre-Migration Series', 'Unknown', 'LeftToRight', 'Unknown');");
            int seriesId;
            var connection = context.Database.GetDbConnection();
            connection.Open();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT Id FROM Series WHERE Name = 'Pre-Migration Series';";
                seriesId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            context.Database.ExecuteSql(
                $"INSERT INTO Issues (SeriesId, Number, ColorMode, FileIsMissing, Checked, IsPlaceholder, MissingAcknowledged, OpenCount, IsFinalIssue) VALUES ({seriesId}, '1', 'Unknown', 0, 0, 0, 0, 0, 1);");
            context.Database.ExecuteSql(
                $"INSERT INTO Issues (SeriesId, Number, ColorMode, FileIsMissing, Checked, IsPlaceholder, MissingAcknowledged, OpenCount, IsFinalIssue) VALUES ({seriesId}, '2', 'Unknown', 0, 0, 0, 0, 0, 0);");

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT Id FROM Issues WHERE Number = '1';";
                trueIssueId = Convert.ToInt32(cmd.ExecuteScalar());
            }
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT Id FROM Issues WHERE Number = '2';";
                falseIssueId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            migrator.Migrate();
        }

        using (var context = CreateContext())
        {
            // Pre-existing rows keep their exact prior value across the nullable-column ALTER -
            // not coalesced, not flipped.
            Assert.Equal(true, context.Issues.Single(i => i.Id == trueIssueId).IsFinalIssue);
            Assert.Equal(false, context.Issues.Single(i => i.Id == falseIssueId).IsFinalIssue);

            // A freshly-inserted post-migration issue with no explicit value reads back null
            // (Unknown), not the old implicit false default.
            var series = context.Series.Single();
            var freshIssue = new Issue { SeriesId = series.Id, Number = "3" };
            context.Issues.Add(freshIssue);
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Null(context.Issues.Single(i => i.Number == "3").IsFinalIssue);

            // AppSettings' two new companion columns round-trip through EF too.
            var settings = context.GetOrCreateAppSettings();
            Assert.Null(settings.LibrarySortVirtualTagId);
            Assert.Null(settings.LibraryGroupVirtualTagId);
            settings.LibrarySortVirtualTagId = 42;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(42, context.GetOrCreateAppSettings().LibrarySortVirtualTagId);
        }
    }
}
