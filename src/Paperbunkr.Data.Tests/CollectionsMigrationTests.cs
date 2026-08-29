using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddCollections</c> migration (docs/superpowers/specs/2026-08-27-collections-
/// design.md) copies every row of the outgoing <c>Categories</c>/<c>CategorySeries</c> tables into
/// the new <c>Collections</c>/<c>CollectionItems</c> tables (preserving <c>Category.Id</c> as the
/// new <c>Collection.Id</c>), and renames <c>AppSettings.LibraryActiveCategoryId</c> to
/// <c>LibraryActiveCollectionId</c>.
/// </summary>
public class CollectionsMigrationTests : IDisposable
{
    private const string PriorMigration = "20260829093238_AddPluginSettingState";
    private readonly string _dbPath;

    public CollectionsMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_collections_migration_{Guid.NewGuid():N}.db");
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
    public void Migration_CopiesCategoryRowsAndMemberships_PreservingId_AndRenamesSettingsColumn()
    {
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            context.Database.ExecuteSqlRaw("INSERT INTO Series (Id, Name, ContentType, ReadingMode, Status) VALUES (1, 'Alpha', 'Unknown', 'LeftToRight', 'Unknown');");
            context.Database.ExecuteSqlRaw("INSERT INTO Series (Id, Name, ContentType, ReadingMode, Status) VALUES (2, 'Bravo', 'Unknown', 'LeftToRight', 'Unknown');");

            context.Database.ExecuteSqlRaw("INSERT INTO Categories (Id, Name, SortOrder) VALUES (10, 'Favorites', 0);");
            context.Database.ExecuteSqlRaw("INSERT INTO Categories (Id, Name, SortOrder) VALUES (20, 'To Reread', 1);");

            context.Database.ExecuteSqlRaw("INSERT INTO CategorySeries (CategoriesId, SeriesId) VALUES (10, 1);");
            context.Database.ExecuteSqlRaw("INSERT INTO CategorySeries (CategoriesId, SeriesId) VALUES (10, 2);");

            context.Database.ExecuteSqlRaw(
                "INSERT INTO AppSettings (Id, LibraryActiveCategoryId, MinimizeToTray, MinimizeToTrayNoticeShown, NavRailPinned, ReducedMotion) " +
                "VALUES (1, 10, 0, 0, 0, 0);");

            migrator.Migrate();
        }

        using (var context = CreateContext())
        {
            var collections = context.Collections.OrderBy(c => c.Id).ToList();
            Assert.Equal(2, collections.Count);
            Assert.Equal(10, collections[0].Id);
            Assert.Equal("Favorites", collections[0].Name);
            Assert.True(collections[0].IsAutoCover);
            Assert.Equal(20, collections[1].Id);
            Assert.Equal("To Reread", collections[1].Name);

            var items = context.CollectionItems.Where(i => i.CollectionId == 10).OrderBy(i => i.SeriesId).ToList();
            Assert.Equal(2, items.Count);
            Assert.All(items, i => Assert.Null(i.IssueId));
            Assert.All(items, i => Assert.Null(i.BookId));
            Assert.Equal(new int?[] { 1, 2 }, items.Select(i => i.SeriesId));

            Assert.Empty(context.CollectionItems.Where(i => i.CollectionId == 20));

            var settings = context.GetOrCreateAppSettings();
            Assert.Equal(10, settings.LibraryActiveCollectionId);
        }
    }
}
