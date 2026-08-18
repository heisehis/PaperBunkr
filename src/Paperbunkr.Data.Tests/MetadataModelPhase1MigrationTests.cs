using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the hand-edited <c>MetadataModelPhase1CanonicalMetadata</c> migration's raw-SQL
/// backfill against a real SQLite database carrying pre-migration data (docs/superpowers/specs/
/// 2026-08-17-metadata-model-phase1-canonical-metadata-design.md) - the auto-scaffolded version of
/// this migration mistook the BlackAndWhite-&gt;OpenCount column swap for a RenameColumn, which
/// would have silently corrupted data; this test exists specifically to catch a regression back to
/// that shape (assertions here would fail against a naive RenameColumn since ColorMode wouldn't
/// exist as a column at all in that case).
/// </summary>
public class MetadataModelPhase1MigrationTests : IDisposable
{
    private const string PriorMigration = "20260817043912_AddLibraryListLayoutSettings";
    private readonly string _dbPath;

    public MetadataModelPhase1MigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_phase1_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_BackfillsColorModeAndStatus_FromOldBoolColumns_AndPreservesVolumeAsText()
    {
        using (var context = CreateContext())
        {
            // Bring the schema to exactly one migration before this phase's - the real old shape
            // (bool BlackAndWhite, bool IsComplete, int Volume) - not EnsureCreated's always-latest
            // shape, which would never exercise the backfill at all.
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            // Identified by Name/Number below rather than tracking rowids through the insert
            // sequence - EF Core doesn't guarantee ExecuteSql and a follow-up SELECT last_insert_
            // rowid() share the same underlying SQLite connection (confirmed by a real flaky failure
            // under full-suite parallel test execution, passed in isolation), so rowid-chasing here
            // was itself a bug, not just unnecessary complexity.
            context.Database.ExecuteSql(
                $"INSERT INTO Series (Name, ContentType, ReadingMode, IsComplete) VALUES ('Old Shape Series', 'Unknown', 'LeftToRight', 1);");
            context.Database.ExecuteSql(
                $"INSERT INTO Issues (SeriesId, Number, Volume, BlackAndWhite, FileIsMissing, Checked, IsPlaceholder, MissingAcknowledged) SELECT Id, '1', 5, 1, 0, 0, 0, 0 FROM Series WHERE Name = 'Old Shape Series';");
            context.Database.ExecuteSql(
                $"INSERT INTO Issues (SeriesId, Number, Volume, BlackAndWhite, FileIsMissing, Checked, IsPlaceholder, MissingAcknowledged) SELECT Id, '2', NULL, 0, 0, 0, 0, 0 FROM Series WHERE Name = 'Old Shape Series';");

            // Now migrate the rest of the way, including this phase's migration.
            migrator.Migrate();
        }

        using (var context = CreateContext())
        {
            var series = context.Series.Include(s => s.Issues).First(s => s.Name == "Old Shape Series");
            Assert.Equal(SeriesStatus.Completed, series.Status);
            Assert.True(series.IsComplete);

            var bwIssue = series.Issues.First(i => i.Number == "1");
            Assert.Equal(ColorMode.BlackAndWhite, bwIssue.ColorMode);
            Assert.Equal("5", bwIssue.Volume);
            Assert.Equal(0, bwIssue.OpenCount);

            var colorIssue = series.Issues.First(i => i.Number == "2");
            Assert.Equal(ColorMode.Color, colorIssue.ColorMode);
            Assert.Null(colorIssue.Volume);
        }
    }
}
