using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>AddPersonalization (docs/superpowers/specs/2026-11-06-personalization-design.md), run for real against SQLite.</summary>
public sealed class AddPersonalizationMigrationTests
{
    [Fact]
    public void Migrate_AddsTheSettingsWithTheirDefaults_AndTheModelHasNoPendingChanges()
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_personalization_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={path}").Options;
            using var context = new PaperbunkrDbContext(options);
            context.Database.Migrate();

            var settings = context.GetOrCreateAppSettings();
            Assert.Equal(100, settings.FontScale);
            Assert.Null(settings.HeadingFontFamily);
            Assert.Equal(1, settings.CardElevation);
            Assert.Equal(SidebarStyle.Standard, settings.SidebarStyle);
            Assert.Equal(1, settings.FocusRingThickness);
            Assert.False(context.Database.HasPendingModelChanges());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
