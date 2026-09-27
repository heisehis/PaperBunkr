using Microsoft.EntityFrameworkCore;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddLibraryHealthTab</c> migration (docs/superpowers/specs/2026-09-26-library-health-subtabs-design.md):
/// <c>AppSettings.LibraryHealthTab</c> is nullable and unset by default. <c>Down</c> is a deliberate no-op, so only the
/// migrate-to-HEAD default and the round trip are asserted.
/// </summary>
public class AddLibraryHealthTabMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_lh_tab_migration_test_{Guid.NewGuid():N}.db");

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

        Assert.Null(context.GetOrCreateAppSettings().LibraryHealthTab);
    }

    [Fact]
    public void Column_RoundTrips()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            context.GetOrCreateAppSettings().LibraryHealthTab = "Files";
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal("Files", context.GetOrCreateAppSettings().LibraryHealthTab);
        }
    }
}
