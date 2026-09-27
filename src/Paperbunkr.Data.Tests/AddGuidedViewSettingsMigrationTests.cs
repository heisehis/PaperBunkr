using Microsoft.EntityFrameworkCore;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddGuidedViewSettings</c> migration (docs/superpowers/specs/2026-09-25-comic-reader-panels-and-zoom-design.md): <c>GuidedViewOnOpen</c> (off) and <c>SmartDoubleClickZoom</c> (on).
/// <c>Down</c> is a deliberate no-op, so only the migrate-to-HEAD defaults and the round trip are asserted.
/// </summary>
public class AddGuidedViewSettingsMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_guided_view_migration_test_{Guid.NewGuid():N}.db");

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
    public void Migration_AddsColumns_WithGuidedOffAndSmartZoomOn()
    {
        using var context = CreateContext();
        context.Database.Migrate();
        var settings = context.GetOrCreateAppSettings();

        Assert.False(settings.GuidedViewOnOpen);
        Assert.True(settings.SmartDoubleClickZoom);
    }

    [Fact]
    public void Columns_RoundTrip()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var settings = context.GetOrCreateAppSettings();
            settings.GuidedViewOnOpen = true;
            settings.SmartDoubleClickZoom = false;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            Assert.True(settings.GuidedViewOnOpen);
            Assert.False(settings.SmartDoubleClickZoom);
        }
    }
}
