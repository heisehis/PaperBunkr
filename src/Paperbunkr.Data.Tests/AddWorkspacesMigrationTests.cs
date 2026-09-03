using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddWorkspaces</c> migration (docs/superpowers/specs/2026-09-03-library-saved-
/// workspaces-design.md) - a new <c>Workspaces</c> table plus two nullable <c>AppSettings</c>
/// columns. Guards against the scaffolder emitting anything unexpected and confirms a clean
/// round-trip / reversal.
/// </summary>
public class AddWorkspacesMigrationTests : IDisposable
{
    // The migration immediately before AddWorkspaces on a clean history. (Deliberately not the
    // literal previous file on disk in a working tree that also carries the uncommitted
    // ReworkBookHighlightAnchor migration - stepping back this far still fully reverses AddWorkspaces.)
    private const string PriorMigration = "20260902142325_AddLastContentTypeSweepUtc";
    private readonly string _dbPath;

    public AddWorkspacesMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_workspaces_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_CreatesWorkspacesTable_AndActiveWorkspaceColumns_ThatRoundTrip()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();

            var settings = context.GetOrCreateAppSettings();
            Assert.Null(settings.LibraryActiveWorkspaceId);
            Assert.Null(settings.BooksActiveWorkspaceId);

            context.Workspaces.Add(new Workspace
            {
                Screen = WorkspaceScreen.Library,
                Name = "Manga",
                SortOrder = 3,
                IsBuiltIn = true,
                StateJson = "{\"ViewMode\":\"PosterGrid\"}",
            });
            context.Workspaces.Add(new Workspace
            {
                Screen = WorkspaceScreen.Books,
                Name = "By series",
                SortOrder = 2,
                IsBuiltIn = true,
                StateJson = "{\"GroupField\":\"Series\"}",
            });
            settings.LibraryActiveWorkspaceId = 1;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var library = context.Workspaces.Single(w => w.Screen == WorkspaceScreen.Library);
            Assert.Equal("Manga", library.Name);
            Assert.Equal(3, library.SortOrder);
            Assert.True(library.IsBuiltIn);
            Assert.Contains("PosterGrid", library.StateJson);

            Assert.Equal(WorkspaceScreen.Books, context.Workspaces.Single(w => w.Name == "By series").Screen);
            Assert.Equal(1, context.GetOrCreateAppSettings().LibraryActiveWorkspaceId);
        }
    }

    [Fact]
    public void Migration_IsReversible_DroppingTableAndColumns_WithoutLosingSettingsRow()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            context.GetOrCreateAppSettings();
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);

            var workspacesTable = context.Database
                .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='table' AND name='Workspaces';")
                .ToList();
            Assert.Empty(workspacesTable);

            var columns = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('AppSettings') WHERE name IN ('LibraryActiveWorkspaceId', 'BooksActiveWorkspaceId');")
                .ToList();
            Assert.Empty(columns);

            var rowCount = context.Database
                .SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM AppSettings WHERE Id = 1")
                .Single();
            Assert.Equal(1, rowCount);
        }
    }
}
