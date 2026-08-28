using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="BookSeriesMaintenance.PruneIfEmpty"/> - silent removal of a <see cref="BookSeries"/>
/// row once its last book leaves (docs/superpowers/specs/2026-08-27-books-bulk-series-editing-
/// design.md, component d).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookSeriesMaintenanceTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public BookSeriesMaintenanceTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_seriesprune_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    [Fact]
    public void PrunesSeriesWithNoBooks()
    {
        int seriesId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var s = new BookSeries { Name = "Empty" };
            context.BookSeries.Add(s);
            context.SaveChanges();
            seriesId = s.Id;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            BookSeriesMaintenance.PruneIfEmpty(context, seriesId);
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Empty(context.BookSeries.Where(s => s.Id == seriesId));
        }
    }

    [Fact]
    public void LeavesSeriesThatStillHasBooks()
    {
        int seriesId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var s = new BookSeries { Name = "Populated" };
            context.BookSeries.Add(s);
            context.SaveChanges();
            seriesId = s.Id;
            context.Books.Add(new Book { Title = "Held", BookSeriesId = seriesId, Format = BookFormat.Epub, FilePath = @"C:\b\held.epub" });
            context.SaveChanges();
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            BookSeriesMaintenance.PruneIfEmpty(context, seriesId);
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Single(context.BookSeries.Where(s => s.Id == seriesId));
        }
    }

    [Fact]
    public void NullId_And_MissingRow_AreNoOps()
    {
        using var context = PaperbunkrDb.CreateContext();
        BookSeriesMaintenance.PruneIfEmpty(context, null);
        BookSeriesMaintenance.PruneIfEmpty(context, 999999);
        // no throw
    }
}
