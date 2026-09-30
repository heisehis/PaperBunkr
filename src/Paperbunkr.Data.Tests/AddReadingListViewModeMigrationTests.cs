using Microsoft.EntityFrameworkCore;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddReadingListViewMode</c> migration (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md §5):
/// <c>AppSettings.ReadingListViewMode</c> is nullable and unset by default. <c>Down</c> is a deliberate no-op, so only the migrate-to-HEAD
/// default and the round trip are asserted (no up-down-up).
/// </summary>
public class AddReadingListViewModeMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_rl_view_mode_migration_test_{Guid.NewGuid():N}.db");

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

    private PaperbunkrDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    [Fact]
    public void Migration_AddsColumn_UnsetByDefault()
    {
        using var context = CreateContext();
        context.Database.Migrate();
        Assert.Null(context.GetOrCreateAppSettings().ReadingListViewMode);
    }

    [Fact]
    public void Column_RoundTrips()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            context.GetOrCreateAppSettings().ReadingListViewMode = "Covers";
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal("Covers", context.GetOrCreateAppSettings().ReadingListViewMode);
        }
    }
}
