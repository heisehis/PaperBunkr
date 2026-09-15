using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the hand-edited <c>ConsolidateLibraryViewModes</c> migration's raw-SQL remap
/// (docs/superpowers/specs/2026-09-14-library-visual-redesign-design.md §2) against a real SQLite
/// database carrying a pre-migration <c>AppSettings</c> row with a legacy <c>LibraryViewMode</c>
/// string - same shape as <see cref="LibraryPosterGridMigrationTests"/>. Note this migration
/// deliberately does NOT rename the <c>PosterGrid</c> enum member's stored string (see
/// LibraryViewMode.cs's doc comment) - only PanoramaGrid/Tiles collapse into it, and Details renames
/// to DetailsTable.
/// </summary>
public class ConsolidateLibraryViewModesMigrationTests : IDisposable
{
    // The migration immediately before this one - see AddConfirmBeforeClose in the migrations
    // folder listing at the time this migration was authored.
    private const string PriorMigration = "20260914121101_AddConfirmBeforeClose";
    private readonly string _dbPath;

    public ConsolidateLibraryViewModesMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_consolidateviewmode_migration_test_{Guid.NewGuid():N}.db");
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

    private (string ViewMode, string GridCoverFit) MigrateWithSeededViewMode(string legacyValue)
    {
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            // The singleton settings row isn't seeded by migrations - insert one with the legacy
            // view mode. Unlike LibraryPosterGridMigrationTests' much-earlier PriorMigration point,
            // by this point in the chain 4 bool columns (MinimizeToTray/MinimizeToTrayNoticeShown/
            // NavRailPinned/ReducedMotion) have no DB-level default at all - confirmed via
            // `PRAGMA table_info(AppSettings)` against a real migrated scratch DB. Not a real app
            // bug (GetOrCreateAppSettings always inserts explicit values for every property via EF,
            // never relying on the SQLite column default), but a raw partial INSERT like this one
            // needs them spelled out or SQLite's NOT NULL constraint rejects the row.
            context.Database.ExecuteSqlRaw(
                $"INSERT INTO AppSettings (Id, LibraryViewMode, MinimizeToTray, MinimizeToTrayNoticeShown, NavRailPinned, ReducedMotion) VALUES (1, '{legacyValue}', 0, 0, 0, 0);");

            // Pinned to this migration specifically, not latest - same lesson learned fixing
            // LibraryPosterGridMigrationTests/RemoveComicListViewModeMigrationTests above: a future
            // migration could remap LibraryViewMode again, and this test should keep verifying only
            // this migration's own remap.
            migrator.Migrate("20260914220638_ConsolidateLibraryViewModes");
        }

        using (var context = CreateContext())
        {
            var viewMode = context.Database
                .SqlQueryRaw<string>("SELECT LibraryViewMode AS Value FROM AppSettings WHERE Id = 1")
                .Single();
            var coverFit = context.Database
                .SqlQueryRaw<string>("SELECT LibraryGridCoverFit AS Value FROM AppSettings WHERE Id = 1")
                .Single();
            return (viewMode, coverFit);
        }
    }

    [Fact]
    public void Migration_PosterGrid_IsUnchanged_WithPosterCoverFit()
    {
        // PosterGrid's own stored string is deliberately never touched by this migration - it's
        // still the default member, just semantically wider now (the Grid family).
        var (viewMode, coverFit) = MigrateWithSeededViewMode("PosterGrid");

        Assert.Equal("PosterGrid", viewMode);
        Assert.Equal("Poster", coverFit);
    }

    [Fact]
    public void Migration_PanoramaGrid_BecomesPosterGrid_WithPanoramaCoverFit()
    {
        var (viewMode, coverFit) = MigrateWithSeededViewMode("PanoramaGrid");

        Assert.Equal("PosterGrid", viewMode);
        Assert.Equal("Panorama", coverFit);
    }

    [Fact]
    public void Migration_Tiles_BecomesPosterGrid_WithTilesCoverFit()
    {
        var (viewMode, coverFit) = MigrateWithSeededViewMode("Tiles");

        Assert.Equal("PosterGrid", viewMode);
        Assert.Equal("Tiles", coverFit);
    }

    [Fact]
    public void Migration_List_IsUnchanged()
    {
        // List was always a structurally different container (a real ListBox, not a wrap-grid) -
        // it never had a Poster/Panorama/Tiles-style sub-mode to consolidate, so it just survives.
        var (viewMode, coverFit) = MigrateWithSeededViewMode("List");

        Assert.Equal("List", viewMode);
        Assert.Equal("Poster", coverFit); // untouched column default - meaningless for List, never read
    }

    [Fact]
    public void Migration_Details_BecomesDetailsTable()
    {
        var (viewMode, _) = MigrateWithSeededViewMode("Details");

        Assert.Equal("DetailsTable", viewMode);
    }

    [Fact]
    public void Migration_AddsNewColumns_WithCorrectDefaults()
    {
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);
            context.Database.ExecuteSqlRaw(
                "INSERT INTO AppSettings (Id, LibraryViewMode, MinimizeToTray, MinimizeToTrayNoticeShown, NavRailPinned, ReducedMotion) VALUES (1, 'List', 0, 0, 0, 0);");
            migrator.Migrate("20260914220638_ConsolidateLibraryViewModes");
        }

        using (var context = CreateContext())
        {
            var isPanelVisible = context.Database
                .SqlQueryRaw<long>("SELECT IsLibraryPreviewPanelVisible AS Value FROM AppSettings WHERE Id = 1")
                .Single();
            var panelWidth = context.Database
                .SqlQueryRaw<double>("SELECT LibraryPreviewPanelWidth AS Value FROM AppSettings WHERE Id = 1")
                .Single();

            // Regression guard for the exact bug caught while authoring this migration: a bool/double
            // property initializer alone isn't reflected into migration metadata, only an explicit
            // HasDefaultValue(...) in OnModelCreating is - the first scaffold of this migration
            // generated defaultValue: 0.0 for LibraryPreviewPanelWidth before that was added.
            Assert.Equal(1, isPanelVisible);
            Assert.Equal(320.0, panelWidth);
        }
    }

    [Fact]
    public void Migration_PreservesUnmappedOrphanColumns()
    {
        // Regression guard for project_paperbunkr_migration_rollback_orphan_column_bug: an earlier
        // draft of this migration used an AlterColumn to rename PosterGrid's stored default, which
        // forced a full-table rebuild that silently dropped LibraryGroupField/LibrarySortField/
        // LibrarySortDirection (unmapped since UnifyLibrarySortGroupFields, still physically present).
        // The final migration has no AlterColumn at all, specifically to avoid this.
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);
            context.Database.ExecuteSqlRaw(
                "INSERT INTO AppSettings (Id, LibraryViewMode, LibraryGroupField, LibrarySortField, LibrarySortDirection, MinimizeToTray, MinimizeToTrayNoticeShown, NavRailPinned, ReducedMotion) VALUES (1, 'PanoramaGrid', 'Publisher', 'Name', 'Ascending', 0, 0, 0, 0);");
            migrator.Migrate("20260914220638_ConsolidateLibraryViewModes");
        }

        using (var context = CreateContext())
        {
            var groupField = context.Database
                .SqlQueryRaw<string>("SELECT LibraryGroupField AS Value FROM AppSettings WHERE Id = 1")
                .Single();
            var sortField = context.Database
                .SqlQueryRaw<string>("SELECT LibrarySortField AS Value FROM AppSettings WHERE Id = 1")
                .Single();
            var sortDirection = context.Database
                .SqlQueryRaw<string>("SELECT LibrarySortDirection AS Value FROM AppSettings WHERE Id = 1")
                .Single();

            Assert.Equal("Publisher", groupField);
            Assert.Equal("Name", sortField);
            Assert.Equal("Ascending", sortDirection);
        }
    }
}
