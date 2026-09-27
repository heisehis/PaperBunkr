using Microsoft.EntityFrameworkCore;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddPreOpenNextIssue</c> migration (docs/superpowers/specs/2026-09-25-comic-reader-performance-
/// design.md A): <c>AppSettings.PreOpenNextIssue</c>, default on. <c>Down</c> is a deliberate no-op, so - per the up-down-up
/// antipattern note - only the migrate-to-HEAD default and round-trip are asserted.
/// </summary>
public class AddPreOpenNextIssueMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public AddPreOpenNextIssueMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_preopen_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsColumn_DefaultingOn_ThatRoundTrips()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var settings = context.GetOrCreateAppSettings();

            Assert.True(settings.PreOpenNextIssue);

            settings.PreOpenNextIssue = false;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.False(context.GetOrCreateAppSettings().PreOpenNextIssue);
        }
    }
}
