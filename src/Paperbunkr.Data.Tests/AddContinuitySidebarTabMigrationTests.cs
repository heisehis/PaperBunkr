using Microsoft.EntityFrameworkCore;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddContinuitySidebarTab</c> migration (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md):
/// <c>AppSettings.ContinuitySidebarTab</c> is nullable and unset by default. <c>Down</c> is a deliberate no-op, so only the
/// migrate-to-HEAD default and the round trip are asserted (no up-down-up).
/// </summary>
public class AddContinuitySidebarTabMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_continuity_tab_migration_test_{Guid.NewGuid():N}.db");

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
    public void Migration_AddsColumn_UnsetByDefault()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        Assert.Null(context.GetOrCreateAppSettings().ContinuitySidebarTab);
    }

    [Fact]
    public void Column_RoundTrips()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            context.GetOrCreateAppSettings().ContinuitySidebarTab = "Events";
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal("Events", context.GetOrCreateAppSettings().ContinuitySidebarTab);
        }
    }
}
