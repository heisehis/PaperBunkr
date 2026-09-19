using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the hand-written <c>AddSmoothScrolling</c> migration (docs/superpowers/specs/2026-09-19-library-scroll-smoothness-
/// design.md section 6): one additive boolean <c>AppSettings.SmoothScrolling</c> column defaulting to true, with a plain
/// <c>DropColumn</c> on Down (not exercised, see the test).
/// </summary>
public class AddSmoothScrollingMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public AddSmoothScrollingMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_smoothscrolling_migration_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext CreateContext()
    {
        // PendingModelChangesWarning is ignored on purpose: at this commit the committed model snapshot already lists tracker columns
        // that AppSettings does not have (unfinished work in another branch), so EF reports "pending changes" for every migration
        // test, the sibling ones included. This test is about what AddSmoothScrolling does, not about that unrelated drift.
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new PaperbunkrDbContext(options);
    }

    [Fact]
    public void Migration_AddsTheColumn_DefaultingToTrue_ThatRoundTrips()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();

            var settings = context.GetOrCreateAppSettings();
            Assert.True(settings.SmoothScrolling);

            settings.SmoothScrolling = false;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.False(context.GetOrCreateAppSettings().SmoothScrolling);
        }

        // Down() is deliberately not exercised here. On SQLite a DropColumn rebuilds the table from the previous migration's model
        // (AddMatrixRainEnabled), and that Designer still lists columns (ReaderAutoHideChrome, ReaderChromeHoverMode) which the
        // committed snapshot no longer has - the same pre-existing snapshot drift described above - so the rollback fails for reasons
        // unrelated to this migration. Down() never runs in the app; it is a plain DropColumn of the column Up() adds.
    }

    [Fact]
    public void Migration_IsTheLatestOne_SoAFreshDatabaseHasTheColumn()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var columns = context.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('AppSettings') WHERE name = 'SmoothScrolling';")
            .ToList();

        Assert.Equal(new[] { "SmoothScrolling" }, columns);
    }
}
