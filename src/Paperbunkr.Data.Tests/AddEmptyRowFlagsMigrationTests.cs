using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddEmptyRowFlags</c> migration (docs/superpowers/specs/2026-09-17-series-name-
/// matching-and-empty-row-cleanup-design.md): <c>Series.EmptyRowAcknowledged</c>,
/// <c>Issue.EmptyRowAcknowledged</c>, and <c>Issue.IsContentEmpty</c> - three plain bool columns,
/// default <see langword="false"/>. No other structural change in this migration, so unlike
/// <c>AddMissingVerificationCountAndRemovedLibraryEntry</c>'s hand-fixed no-op <c>Down</c>, a real
/// <c>DropColumn</c> is safe here and verified to actually round-trip.
/// </summary>
public class AddEmptyRowFlagsMigrationTests : IDisposable
{
    private const string PriorMigration = "20260917151001_AddContinuityFandomKey";
    private readonly string _dbPath;

    public AddEmptyRowFlagsMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_emptyrowflags_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsColumns_DefaultFalse_ThatRoundTrip()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var series = new Series { Name = "Test" };
        context.Series.Add(series);
        var issue = new Issue { Series = series, Number = "1" };
        context.Issues.Add(issue);
        context.SaveChanges();

        Assert.False(series.EmptyRowAcknowledged);
        Assert.False(issue.EmptyRowAcknowledged);
        Assert.False(issue.IsContentEmpty);

        series.EmptyRowAcknowledged = true;
        issue.EmptyRowAcknowledged = true;
        issue.IsContentEmpty = true;
        context.SaveChanges();

        using var reopened = CreateContext();
        var reopenedSeries = reopened.Series.Single();
        var reopenedIssue = reopened.Issues.Single();
        Assert.True(reopenedSeries.EmptyRowAcknowledged);
        Assert.True(reopenedIssue.EmptyRowAcknowledged);
        Assert.True(reopenedIssue.IsContentEmpty);
    }

    [Fact]
    public void Down_DropsAllThreeColumns()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        context.GetService<IMigrator>().Migrate(PriorMigration);

        var seriesColumns = context.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Series') WHERE name = 'EmptyRowAcknowledged';")
            .ToList();
        var issueColumns = context.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Issues') WHERE name IN ('EmptyRowAcknowledged', 'IsContentEmpty');")
            .ToList();

        Assert.Empty(seriesColumns);
        Assert.Empty(issueColumns);
    }
}
