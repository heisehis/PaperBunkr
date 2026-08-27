using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddBooksBrowseState</c> migration (docs/superpowers/specs/2026-08-27-books-
/// screen-chrome-and-home-strip-design.md) - five plain column adds (2 on Books, 3 on AppSettings),
/// no data fix. Same shape as <see cref="LibraryDetailsColumnsMigrationTests"/>.
/// </summary>
public class AddBooksBrowseStateMigrationTests : IDisposable
{
    private const string PriorMigration = "20260827093006_LibraryDetailsColumns";
    private readonly string _dbPath;

    public AddBooksBrowseStateMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_booksbrowse_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsColumnsWithDefaults_PreservingExistingRows_AndIsReversible()
    {
        // Up to HEAD, then seed via the ORM (a bare AppSettings INSERT can't satisfy every
        // pre-existing NOT NULL column - same workaround as LibraryDetailsColumnsMigrationTests).
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            context.GetOrCreateAppSettings();
            context.Books.Add(new Book
            {
                Title = "Legacy Book", Format = BookFormat.Epub, FilePath = @"C:\x.epub",
                AddedTime = new DateTime(2024, 1, 1),
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            Assert.Equal(BooksSortField.Title, settings.BooksSortField);
            Assert.Equal(SortDirection.Ascending, settings.BooksSortDirection);
            Assert.Equal(BooksGroupField.None, settings.BooksGroupField);

            var book = context.Books.Single();
            Assert.False(book.Finished);
            Assert.Equal(0, book.ChapterCount);
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);

            var cols = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Books') WHERE name IN ('Finished', 'ChapterCount');")
                .ToList();
            Assert.Empty(cols);

            var bookCount = context.Database
                .SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM Books")
                .Single();
            Assert.Equal(1, bookCount);
        }
    }
}
