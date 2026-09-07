using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddMissingVerificationCountAndRemovedLibraryEntry</c> migration
/// (docs/superpowers/specs/2026-09-06-missing-files-library-health-design.md): a plain int column
/// on Issues, a plain nullable DateTime column on AppSettings, and a brand-new RemovedLibraryEntries
/// table. <c>Down</c> drops the new table but is a deliberate no-op for the two column adds (see the
/// migration's own comment) - a <c>DropColumn</c> on Issues/AppSettings would trigger SQLite's
/// full-table rebuild, silently dropping the orphaned <c>LibraryGroupField</c> family of columns and
/// breaking later <c>Down()</c> steps in a rollback chain.
/// </summary>
public class AddMissingVerificationCountAndRemovedLibraryEntryMigrationTests : IDisposable
{
    private const string PriorMigration = "20260906030530_AddScheduledTaskState";
    private readonly string _dbPath;

    public AddMissingVerificationCountAndRemovedLibraryEntryMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_libraryhealth_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsColumnsAndTable_ThatRoundTrip()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var series = new Series { Name = "Test Series" };
        var issue = new Issue { Series = series, Number = "1" };
        context.Series.Add(series);
        context.Issues.Add(issue);
        context.SaveChanges();

        Assert.Equal(0, issue.MissingVerificationCount);
        Assert.Null(context.GetOrCreateAppSettings().LastLibraryHealthVerifyUtc);

        issue.MissingVerificationCount = 2;
        context.GetOrCreateAppSettings().LastLibraryHealthVerifyUtc = new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc);
        context.RemovedLibraryEntries.Add(new RemovedLibraryEntry
        {
            SeriesName = "Test Series",
            SeriesId = series.Id,
            Number = "1",
            FilePath = @"C:\Comics\test.cbz",
            RemovedAtUtc = new DateTime(2026, 9, 6, 1, 0, 0, DateTimeKind.Utc),
            Reason = RemovedLibraryEntryReason.MissingFileCleanup,
        });
        context.SaveChanges();

        using var reopened = CreateContext();
        Assert.Equal(2, reopened.Issues.Single().MissingVerificationCount);
        Assert.Equal(new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc), reopened.GetOrCreateAppSettings().LastLibraryHealthVerifyUtc);
        var entry = reopened.RemovedLibraryEntries.Single();
        Assert.Equal("Test Series", entry.SeriesName);
        Assert.Equal(RemovedLibraryEntryReason.MissingFileCleanup, entry.Reason);
    }

    [Fact]
    public void Down_DropsRemovedLibraryEntriesTable_ButLeavesTheTwoColumnsAsOrphans()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        context.GetService<IMigrator>().Migrate(PriorMigration);

        var issueColumns = context.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Issues') WHERE name = 'MissingVerificationCount';")
            .ToList();
        Assert.Single(issueColumns);

        var settingsColumns = context.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('AppSettings') WHERE name = 'LastLibraryHealthVerifyUtc';")
            .ToList();
        Assert.Single(settingsColumns);

        var tables = context.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'RemovedLibraryEntries';")
            .ToList();
        Assert.Empty(tables);
    }
}
