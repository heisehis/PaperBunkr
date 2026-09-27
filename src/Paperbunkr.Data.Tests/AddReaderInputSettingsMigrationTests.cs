using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddReaderInputSettings</c> migration (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md): tap zone layouts and inversion for paged and
/// continuous mode, plus the mouse, side-button and gamepad toggles. <c>Down</c> is a deliberate no-op, so only the migrate-to-HEAD defaults and the round trip are asserted.
/// </summary>
public class AddReaderInputSettingsMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public AddReaderInputSettingsMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_input_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsColumns_WithTodaysBehaviourAsDefaults()
    {
        using var context = CreateContext();
        context.Database.Migrate();
        var settings = context.GetOrCreateAppSettings();

        Assert.Equal(TapZoneLayout.Default, settings.PagedTapZoneLayout);
        Assert.Equal(TapZoneInvert.None, settings.PagedTapZoneInvert);
        Assert.Equal(TapZoneLayout.Disabled, settings.ContinuousTapZoneLayout);
        Assert.Equal(TapZoneInvert.None, settings.ContinuousTapZoneInvert);
        Assert.True(settings.TapZonesForMouse);
        Assert.True(settings.ExtraMouseButtonsTurnPages);
        Assert.True(settings.GamepadEnabled);
    }

    [Fact]
    public void Columns_RoundTrip()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var settings = context.GetOrCreateAppSettings();
            settings.PagedTapZoneLayout = TapZoneLayout.Kindlish;
            settings.PagedTapZoneInvert = TapZoneInvert.Both;
            settings.ContinuousTapZoneLayout = TapZoneLayout.LShaped;
            settings.ContinuousTapZoneInvert = TapZoneInvert.Horizontal;
            settings.TapZonesForMouse = false;
            settings.ExtraMouseButtonsTurnPages = false;
            settings.GamepadEnabled = false;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            Assert.Equal(TapZoneLayout.Kindlish, settings.PagedTapZoneLayout);
            Assert.Equal(TapZoneInvert.Both, settings.PagedTapZoneInvert);
            Assert.Equal(TapZoneLayout.LShaped, settings.ContinuousTapZoneLayout);
            Assert.Equal(TapZoneInvert.Horizontal, settings.ContinuousTapZoneInvert);
            Assert.False(settings.TapZonesForMouse);
            Assert.False(settings.ExtraMouseButtonsTurnPages);
            Assert.False(settings.GamepadEnabled);
        }
    }
}
