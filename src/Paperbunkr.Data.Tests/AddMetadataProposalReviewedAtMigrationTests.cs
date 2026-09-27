using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddMetadataProposalReviewedAt</c> migration (docs/superpowers/specs/2026-09-25-needs-review-into-library-health-design.md):
/// <c>MetadataProposal.ReviewedAt</c> is nullable and null for every existing/auto-applied row, so all of them keep showing in the Applied list until reviewed.
/// </summary>
public class AddMetadataProposalReviewedAtMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_proposal_reviewed_migration_test_{Guid.NewGuid():N}.db");

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
    public void Migration_AddsNullableReviewedAtColumn()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        using var connection = context.Database.GetDbConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"notnull\" FROM pragma_table_info('MetadataProposals') WHERE name = 'ReviewedAt'";
        var notNull = command.ExecuteScalar();

        Assert.NotNull(notNull); // the column exists
        Assert.Equal(0L, Convert.ToInt64(notNull)); // and is nullable
    }

    [Fact]
    public void ReviewedAt_DefaultsToNull_AndRoundTrips()
    {
        int id;
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var series = new Series { Name = "Kilo Station", ContentType = ContentType.Comic };
            context.Series.Add(series);
            context.SaveChanges();
            var proposal = new MetadataProposal { SeriesId = series.Id, Field = MetadataProposalField.Summary, ProposedValue = "x", Status = MetadataProposalStatus.Accepted };
            context.MetadataProposals.Add(proposal);
            context.SaveChanges();
            id = proposal.Id;
        }

        using (var context = CreateContext())
        {
            var proposal = context.MetadataProposals.Single(p => p.Id == id);
            Assert.Null(proposal.ReviewedAt);
            proposal.ReviewedAt = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(new DateTime(2026, 9, 26, 12, 0, 0), context.MetadataProposals.Single(p => p.Id == id).ReviewedAt);
        }
    }
}
