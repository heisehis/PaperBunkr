using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddFb2MobiBookFormat</c> migration (docs/superpowers/specs/2026-09-01-books-
/// format-ingestion-fb2-mobi-design.md) - an intentionally empty migration (see its own doc comment):
/// <c>Book.Format</c> has no CHECK constraint, so the new <see cref="BookFormat.Fb2"/>/
/// <see cref="BookFormat.Mobi"/> enum members are just new strings an existing column already
/// accepts. This is mostly a smoke test that the empty migration doesn't corrupt anything and that
/// both new format values round-trip.
/// </summary>
public class AddFb2MobiBookFormatMigrationTests : IDisposable
{
    private const string PriorMigration = "20260901195401_AddBookReaderErgonomicsAndAnnotations";
    private readonly string _dbPath;

    public AddFb2MobiBookFormatMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_fb2mobiformat_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_LeavesSchemaUnchanged_AndNewFormatValuesRoundTrip()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            context.Books.Add(new Book { Title = "An FB2 Book", Format = BookFormat.Fb2, FilePath = @"C:\b\one.fb2", AddedTime = new DateTime(2024, 1, 1) });
            context.Books.Add(new Book { Title = "A MOBI Book", Format = BookFormat.Mobi, FilePath = @"C:\b\two.mobi", AddedTime = new DateTime(2024, 1, 1) });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var fb2Book = context.Books.Single(b => b.Title == "An FB2 Book");
            var mobiBook = context.Books.Single(b => b.Title == "A MOBI Book");
            Assert.Equal(BookFormat.Fb2, fb2Book.Format);
            Assert.Equal(BookFormat.Mobi, mobiBook.Format);
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);

            // No column was ever added, so rolling back is a schema no-op - the existing rows
            // (written with string values the prior schema doesn't know as enum members, but the
            // column itself never validated that) survive untouched.
            var bookCount = context.Database
                .SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM Books")
                .Single();
            Assert.Equal(2, bookCount);
        }
    }
}
