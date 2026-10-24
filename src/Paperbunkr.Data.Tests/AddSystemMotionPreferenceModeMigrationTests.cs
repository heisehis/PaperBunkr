using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

public sealed class AddSystemMotionPreferenceModeMigrationTests : IDisposable
{
    private const string PriorMigration = "20261016140844_AddStoryEventTypes";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_motion_preference_{Guid.NewGuid():N}.db");

    private PaperbunkrDbContext CreateContext() => new(new DbContextOptionsBuilder<PaperbunkrDbContext>()
        .UseSqlite($"Data Source={_dbPath}")
        .Options);

    [Theory]
    [InlineData(true, MotionPreferenceMode.Reduced)]
    [InlineData(false, MotionPreferenceMode.System)]
    public void Migration_MapsLegacyReducedMotion(bool legacyValue, MotionPreferenceMode expected)
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var settings = context.GetOrCreateAppSettings();
            settings.ReducedMotion = legacyValue;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
            context.Database.Migrate();
            Assert.Equal(expected, context.GetOrCreateAppSettings().MotionPreference);
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
