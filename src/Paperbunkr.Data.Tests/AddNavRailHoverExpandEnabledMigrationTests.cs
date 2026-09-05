using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises the <c>AddNavRailHoverExpandEnabled</c> migration (docs/superpowers/specs/2026-09-05-
/// nav-rail-hover-toggle-and-undo-redo-removal-design.md) - same shape as
/// <see cref="AddBehaviorSettingsBatch2MigrationTests"/>'s first test, confirming the column's
/// <c>HasDefaultValue(true)</c> (set in <c>PaperbunkrDbContext.OnModelCreating</c>) actually reaches
/// the generated SQL, not just the CLR default - an existing row's column must backfill to
/// <see langword="true"/> (preserving today's hover-expand behavior for existing installs), not
/// SQLite's bare 0/false.
/// </summary>
public class AddNavRailHoverExpandEnabledMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public AddNavRailHoverExpandEnabledMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_navrailhover_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsColumn_DefaultingTrue_ThatRoundTrips()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var settings = context.GetOrCreateAppSettings();

            Assert.True(settings.NavRailHoverExpandEnabled);

            settings.NavRailHoverExpandEnabled = false;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            Assert.False(settings.NavRailHoverExpandEnabled);
        }
    }
}
