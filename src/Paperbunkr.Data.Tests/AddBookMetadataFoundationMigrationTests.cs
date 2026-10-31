using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddBookMetadataFoundation</c> migration (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §3):
/// new Book columns, <c>BookTag</c>/<c>BookFieldSource</c> tables, the <c>Status</c> data step, and that the legacy
/// <c>Books.Finished</c> column survives (the entity property is [NotMapped] now).
/// </summary>
public class AddBookMetadataFoundationMigrationTests : IDisposable
{
    private const string PriorMigration = "20261026124041_AddCollectionContent";
    private const string ThisMigration = "20261026203325_AddBookMetadataFoundation";
    private readonly string _dbPath;

    public AddBookMetadataFoundationMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookmeta_migration_test_{Guid.NewGuid():N}.db");
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

    private static void InsertLegacyBook(PaperbunkrDbContext context, string title, bool finished, int chapter, string? blockId, double? fraction)
    {
        context.Database.ExecuteSqlRaw(
            "INSERT INTO Books (Title, Format, FilePath, AddedTime, Finished, LastChapterIndex, LastBlockId, LastProgressionFraction, ChapterCount) " +
            "VALUES ({0}, 'Epub', {1}, '2024-01-01 00:00:00', {2}, {3}, {4}, {5}, 0);",
            title, @"C:\" + title + ".epub", finished ? 1 : 0, chapter, blockId!, fraction!);
    }

    [Fact]
    public void StatusDataStep_MapsFinishedReadingAndUnread()
    {
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
            InsertLegacyBook(context, "done", finished: true, chapter: 5, blockId: null, fraction: null);
            InsertLegacyBook(context, "block", finished: false, chapter: 0, blockId: "pb-p4", fraction: null);
            InsertLegacyBook(context, "fraction", finished: false, chapter: 0, blockId: null, fraction: 0.3);
            InsertLegacyBook(context, "chapter", finished: false, chapter: 2, blockId: null, fraction: null);
            InsertLegacyBook(context, "fresh", finished: false, chapter: 0, blockId: null, fraction: null);
        }

        using (var context = CreateContext())
        {
            context.Database.Migrate();

            var byTitle = context.Books.AsNoTracking().ToDictionary(b => b.Title, b => b.Status);
            Assert.Equal(BookReadingStatus.Finished, byTitle["done"]);
            Assert.Equal(BookReadingStatus.Reading, byTitle["block"]);
            Assert.Equal(BookReadingStatus.Reading, byTitle["fraction"]);
            Assert.Equal(BookReadingStatus.Reading, byTitle["chapter"]);
            Assert.Equal(BookReadingStatus.Unread, byTitle["fresh"]);

            // The legacy column is still there, unused.
            var legacy = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Books') WHERE name = 'Finished';")
                .ToList();
            Assert.Single(legacy);
        }
    }

    [Fact]
    public void NewBooksCanBeInsertedWithoutTheLegacyColumn_AndFinishedViewsStatus()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var book = new Book { Title = "New", Format = BookFormat.Epub, FilePath = @"C:\n.epub", AddedTime = DateTime.UtcNow };
        Assert.Equal(BookReadingStatus.Unread, book.Status);
        book.Finished = true;
        Assert.Equal(BookReadingStatus.Finished, book.Status);
        book.Finished = false;
        Assert.Equal(BookReadingStatus.Reading, book.Status);

        book.Rating = 3.5f;
        book.Publisher = "Tor";
        context.Books.Add(book);
        context.SaveChanges();

        var loaded = context.Books.AsNoTracking().Single();
        Assert.Equal(3.5f, loaded.Rating);
        Assert.Equal("Tor", loaded.Publisher);
    }

    [Fact]
    public void Tags_AreUniquePerBookFieldAndValue_CaseInsensitively_AndCascadeWithTheBook()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var book = new Book { Title = "T", Format = BookFormat.Epub, FilePath = @"C:\t.epub", AddedTime = DateTime.UtcNow };
        book.Tags.Add(new BookTag { Field = BookTagField.Genre, Value = "Fantasy" });
        book.FieldSources.Add(new BookFieldSource { Field = "Publisher", Source = BookFieldSourceKind.File, FetchedUtc = DateTime.UtcNow });
        context.Books.Add(book);
        context.SaveChanges();

        context.BookTags.Add(new BookTag { BookId = book.Id, Field = BookTagField.Genre, Value = "fantasy" });
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();

        // Same value under the other field is fine.
        context.BookTags.Add(new BookTag { BookId = book.Id, Field = BookTagField.Tags, Value = "Fantasy" });
        context.SaveChanges();

        context.Books.Remove(context.Books.Single());
        context.SaveChanges();
        Assert.Empty(context.BookTags);
        Assert.Empty(context.BookFieldSources);
    }

    [Fact]
    public void Migration_IsReversible_AndSettingsDefaultToProvidersOn()
    {
        using (var context = CreateContext())
        {
            // Exercise this migration's Down/Up pair, not later migrations whose deliberate orphan
            // columns make rolling the entire current schema backwards unsupported. The current EF
            // settings model also contains later columns, so inspect this schema's defaults directly.
            context.GetService<IMigrator>().Migrate(ThisMigration);
            string ColumnDefault(string name) => context.Database.SqlQueryRaw<string>(
                "SELECT dflt_value AS Value FROM pragma_table_info('AppSettings') WHERE name = {0}", name).Single();
            Assert.Equal("1", ColumnDefault("BookMetadataOpenLibraryEnabled"));
            Assert.Equal("1", ColumnDefault("BookMetadataGoogleBooksEnabled"));
            Assert.Equal("0", ColumnDefault("BookMetadataBackfillDone"));
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
            var tables = context.Database
                .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='table' AND name IN ('BookTags','BookFieldSources');")
                .ToList();
            Assert.Empty(tables);
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(ThisMigration);
            Assert.Equal(0, context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM BookTags").Single());
        }
    }
}
