using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>SeriesMetadataProposals</c> migration against a real SQLite database carrying
/// pre-migration data (docs/superpowers/specs/2026-08-23-apply-from-provider-design.md) - widens
/// <c>MetadataProposal.IssueId</c> to nullable and adds <c>SeriesId</c>/<c>ProviderKey</c>, so
/// existing Issue-scoped rows surviving the column type change untouched needs real coverage, not
/// just an assumption (SQLite's ALTER COLUMN is a table-rebuild under the hood, unlike a plain
/// ADD COLUMN).
/// </summary>
public class SeriesMetadataProposalsMigrationTests : IDisposable
{
    private const string PriorMigration = "20260823032304_AddWatchedFolderWatchFlag";
    private readonly string _dbPath;

    public SeriesMetadataProposalsMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_series_proposals_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_PreservesExistingIssueScopedProposal_AndAddsSeriesScopedColumns()
    {
        int issueId;
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            context.Database.ExecuteSql(
                $"INSERT INTO Series (Name, ContentType, ReadingMode, Status) VALUES ('Old Shape Series', 'Unknown', 'LeftToRight', 'Unknown');");
            context.Database.ExecuteSql(
                $"INSERT INTO Issues (SeriesId, Number, ColorMode, FileIsMissing, Checked, IsPlaceholder, MissingAcknowledged, OpenCount, IsFinalIssue) SELECT Id, '1', 'Unknown', 0, 0, 0, 0, 0, 0 FROM Series WHERE Name = 'Old Shape Series';");

            var connection = context.Database.GetDbConnection();
            connection.Open();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT Id FROM Issues WHERE Number = '1';";
                issueId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            context.Database.ExecuteSql(
                $"INSERT INTO MetadataProposals (IssueId, Field, ProposedValue, Source, Confidence, Status, CreatedAt) VALUES ({issueId}, 'Number', '12', 'FilenameParser', 0.6, 'Accepted', '2026-08-23 00:00:00');");

            migrator.Migrate();
        }

        using (var context = CreateContext())
        {
            // Pre-existing Issue-scoped row keeps its IssueId after the nullable-column ALTER.
            var existing = Assert.Single(context.MetadataProposals);
            Assert.Equal(issueId, existing.IssueId);
            Assert.Null(existing.SeriesId);
            Assert.Null(existing.ProviderKey);

            int seriesId = context.Series.Single().Id;

            // New Series-scoped shape round-trips via EF, not just "the columns exist".
            context.MetadataProposals.Add(new MetadataProposal
            {
                SeriesId = seriesId,
                Field = MetadataProposalField.Summary,
                CurrentValue = null,
                ProposedValue = "A synopsis.",
                Source = MetadataProposalSource.MetadataProvider,
                ProviderKey = ExternalMetadataProvider.MangaBaka,
                Confidence = 1.0m,
                Status = MetadataProposalStatus.Accepted,
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var seriesProposal = context.MetadataProposals.Single(p => p.SeriesId != null);
            Assert.Null(seriesProposal.IssueId);
            Assert.Equal(MetadataProposalField.Summary, seriesProposal.Field);
            Assert.Equal(ExternalMetadataProvider.MangaBaka, seriesProposal.ProviderKey);
            Assert.Equal(2, context.MetadataProposals.Count());
        }
    }
}
