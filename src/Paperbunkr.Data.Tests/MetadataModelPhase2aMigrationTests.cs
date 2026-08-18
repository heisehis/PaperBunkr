using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>MetadataModelPhase2aMetadataProposals</c> migration against a real SQLite
/// database carrying pre-migration data (docs/superpowers/specs/2026-08-17-metadata-model-phase2a-
/// metadata-proposals-design.md) - purely additive schema (new table + one new column with a DB
/// default on the existing <c>AppSettings</c> singleton row), so unlike Phase 1's migration this
/// doesn't need a hand-verified raw-SQL backfill, but existing rows surviving untouched and the new
/// column's default applying correctly still need real coverage, not just an assumption.
/// </summary>
public class MetadataModelPhase2aMigrationTests : IDisposable
{
    private const string PriorMigration = "20260817112016_MetadataModelPhase1CanonicalMetadata";
    private readonly string _dbPath;

    public MetadataModelPhase2aMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_phase2a_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_PreservesExistingRows_AddsEmptyMetadataProposalsTable_AndDefaultsPolicyToAutomatic()
    {
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            context.Database.ExecuteSql(
                $"INSERT INTO Series (Name, ContentType, ReadingMode, Status) VALUES ('Old Shape Series', 'Unknown', 'LeftToRight', 'Unknown');");
            context.Database.ExecuteSql(
                $"INSERT INTO Issues (SeriesId, Number, ColorMode, FileIsMissing, Checked, IsPlaceholder, MissingAcknowledged, OpenCount, IsFinalIssue) SELECT Id, '1', 'Unknown', 0, 0, 0, 0, 0, 0 FROM Series WHERE Name = 'Old Shape Series';");
            context.Database.ExecuteSql(
                $"INSERT INTO AppSettings (Id, ActiveSkinKey) VALUES (1, 'default');");

            migrator.Migrate();
        }

        using (var context = CreateContext())
        {
            // Pre-existing rows are untouched - this migration adds a table and a defaulted column,
            // it doesn't rewrite anything.
            var issue = context.Issues.Include(i => i.MetadataProposals).Single(i => i.Number == "1");
            Assert.Empty(issue.MetadataProposals);

            var settings = context.AppSettings.Single(a => a.Id == 1);
            Assert.Equal(MetadataResolutionPolicy.Automatic, settings.MetadataResolutionPolicy);

            Assert.Empty(context.MetadataProposals);

            // New table's real shape - insert/read round-trip via EF, not just "the table exists".
            context.MetadataProposals.Add(new MetadataProposal
            {
                IssueId = issue.Id,
                Field = MetadataProposalField.Number,
                CurrentValue = null,
                ProposedValue = "12",
                Source = MetadataProposalSource.FilenameParser,
                Confidence = 0.6m,
                Status = MetadataProposalStatus.Accepted,
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var proposal = Assert.Single(context.MetadataProposals);
            Assert.Equal(MetadataProposalField.Number, proposal.Field);
            Assert.Equal("12", proposal.ProposedValue);
            Assert.Equal(MetadataProposalSource.FilenameParser, proposal.Source);
            Assert.Equal(MetadataProposalStatus.Accepted, proposal.Status);
        }
    }
}
