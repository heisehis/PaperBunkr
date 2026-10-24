using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Motion v2 §4.1: the stored strings from the old System/On/Off choice map onto System/Full/Reduced/Off. Seeds the old strings
/// directly (not round-tripped through the new enum), so the "old Off means Full" collision is tested against real old data.
/// </summary>
public sealed class MotionPreferenceFourModesMigrationTests : IDisposable
{
    private const string PriorMigration = "20261018093444_AddReaderPageFrameStyle";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_motion_four_{Guid.NewGuid():N}.db");

    private PaperbunkrDbContext CreateContext() => new(new DbContextOptionsBuilder<PaperbunkrDbContext>()
        .UseSqlite($"Data Source={_dbPath}")
        .Options);

    [Theory]
    [InlineData("On", MotionPreferenceMode.Reduced)]
    [InlineData("Off", MotionPreferenceMode.Full)]
    [InlineData("System", MotionPreferenceMode.System)]
    public void Up_MapsOldStrings(string stored, MotionPreferenceMode expected)
    {
        SeedAtPriorMigration(stored);

        using var context = CreateContext();
        context.Database.Migrate();
        Assert.Equal(expected, context.GetOrCreateAppSettings().MotionPreference);
    }

    [Theory]
    [InlineData(MotionPreferenceMode.Reduced, "On")]
    [InlineData(MotionPreferenceMode.Off, "On")]
    [InlineData(MotionPreferenceMode.Full, "Off")]
    [InlineData(MotionPreferenceMode.System, "System")]
    public void Down_MapsBackToOldStrings(MotionPreferenceMode current, string expected)
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            context.GetOrCreateAppSettings().MotionPreference = current;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
            Assert.Equal(expected, ReadStored(context));
        }
    }

    private void SeedAtPriorMigration(string stored)
    {
        // The settings row has many NOT NULL columns, so create it through EF at the latest schema, then step back to the prior
        // migration and overwrite the column with the old string.
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            context.GetOrCreateAppSettings();
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
            context.Database.ExecuteSqlRaw("UPDATE AppSettings SET MotionPreference = {0}", stored);
        }
    }

    private static string ReadStored(PaperbunkrDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MotionPreference FROM AppSettings LIMIT 1";
        return (string)command.ExecuteScalar()!;
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
