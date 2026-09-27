using Microsoft.EntityFrameworkCore;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddReaderComfortSettings</c> migration (docs/superpowers/specs/2026-09-25-comic-reader-comfort-design.md): the stats chip, break nudge and warm shift settings.
/// <c>Down</c> is a deliberate no-op, so only the migrate-to-HEAD defaults and the round trip are asserted.
/// </summary>
public class AddReaderComfortSettingsMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_comfort_migration_test_{Guid.NewGuid():N}.db");

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
    public void Migration_AddsColumns_WithEverythingOffAndTheDocumentedDefaults()
    {
        using var context = CreateContext();
        context.Database.Migrate();
        var settings = context.GetOrCreateAppSettings();

        Assert.False(settings.ShowSessionHud);
        Assert.False(settings.BreakNudgesEnabled);
        Assert.Equal(20, settings.BreakNudgeIntervalMinutes);
        Assert.False(settings.WarmShiftEnabled);
        Assert.Equal(1260, settings.WarmShiftStartMinutes);
        Assert.Equal(420, settings.WarmShiftEndMinutes);
        Assert.Equal(40, settings.WarmShiftStrength);
    }

    [Fact]
    public void Columns_RoundTrip()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var settings = context.GetOrCreateAppSettings();
            settings.ShowSessionHud = true;
            settings.BreakNudgesEnabled = true;
            settings.BreakNudgeIntervalMinutes = 45;
            settings.WarmShiftEnabled = true;
            settings.WarmShiftStartMinutes = 1200;
            settings.WarmShiftEndMinutes = 360;
            settings.WarmShiftStrength = 90;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            Assert.True(settings.ShowSessionHud);
            Assert.True(settings.BreakNudgesEnabled);
            Assert.Equal(45, settings.BreakNudgeIntervalMinutes);
            Assert.True(settings.WarmShiftEnabled);
            Assert.Equal(1200, settings.WarmShiftStartMinutes);
            Assert.Equal(360, settings.WarmShiftEndMinutes);
            Assert.Equal(90, settings.WarmShiftStrength);
        }
    }
}
