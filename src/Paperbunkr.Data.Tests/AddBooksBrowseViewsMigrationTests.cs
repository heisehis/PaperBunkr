using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>Books slice 2a settings columns (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §10).</summary>
public class AddBooksBrowseViewsMigrationTests : IDisposable
{
    private const string PriorMigration = "20261026203325_AddBookMetadataFoundation";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_booksviews_migration_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    [Fact]
    public void Defaults_RoundTrip_AndReversible()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var settings = context.GetOrCreateAppSettings();
            Assert.Equal(BooksStatusTab.All, settings.BooksActiveStatusTab);
            Assert.Equal(BooksViewMode.Grid, settings.BooksViewMode);

            settings.BooksActiveStatusTab = BooksStatusTab.Finished;
            settings.BooksViewMode = BooksViewMode.Bookshelf;
            settings.BooksSortField = BooksSortField.SeriesOrder;
            settings.BooksGroupField = BooksGroupField.Genre;
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            Assert.Equal(BooksStatusTab.Finished, settings.BooksActiveStatusTab);
            Assert.Equal(BooksViewMode.Bookshelf, settings.BooksViewMode);
            Assert.Equal(BooksSortField.SeriesOrder, settings.BooksSortField);
            Assert.Equal(BooksGroupField.Genre, settings.BooksGroupField);
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
            var cols = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('AppSettings') WHERE name IN ('BooksActiveStatusTab','BooksViewMode');")
                .ToList();
            Assert.Empty(cols);
        }
    }
}
