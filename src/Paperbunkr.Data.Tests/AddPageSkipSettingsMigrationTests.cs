using Microsoft.EntityFrameworkCore;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddPageSkipSettings</c> migration (docs/superpowers/specs/2026-09-21-comic-reader-page-
/// intelligence-design.md §2): <c>AppSettings.SkipDeletedPages</c> (default on, CE parity) and
/// <c>SkipAdvertisementPages</c> (default off). <c>Down</c> is a deliberate no-op, so - per the up-down-up
/// antipattern note - only the migrate-to-HEAD defaults and round-trip are asserted.
/// </summary>
public class AddPageSkipSettingsMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public AddPageSkipSettingsMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_pageskip_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsColumns_WithSpecDefaults_ThatRoundTrip()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var settings = context.GetOrCreateAppSettings();

            Assert.True(settings.SkipDeletedPages);
            Assert.False(settings.SkipAdvertisementPages);

            settings.SkipDeletedPages = false;
            settings.SkipAdvertisementPages = true;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            Assert.False(settings.SkipDeletedPages);
            Assert.True(settings.SkipAdvertisementPages);
        }
    }
}
