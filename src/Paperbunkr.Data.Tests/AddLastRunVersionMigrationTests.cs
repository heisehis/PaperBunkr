using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises the <c>AddLastRunVersion</c> migration (docs/superpowers/specs/2026-09-09-startup-
/// onboarding-whats-new-design.md) - same shape as
/// <see cref="AddNavRailHoverExpandEnabledMigrationTests"/>. <c>LastRunVersion</c> is a nullable
/// string with no default (null is the "fresh install" sentinel); this confirms the column exists,
/// defaults to null on an existing row, and round-trips a written value.
/// </summary>
public class AddLastRunVersionMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public AddLastRunVersionMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_lastrunversion_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsNullableColumn_DefaultingNull_ThatRoundTrips()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var settings = context.GetOrCreateAppSettings();

            Assert.Null(settings.LastRunVersion);

            settings.LastRunVersion = "0.3.0.0";
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            Assert.Equal("0.3.0.0", settings.LastRunVersion);
        }
    }
}
