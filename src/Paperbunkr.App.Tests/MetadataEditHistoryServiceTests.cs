using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Covers <see cref="MetadataEditHistoryService"/>'s book path added for the Book Properties editor
/// (docs/superpowers/specs/2026-08-27-book-properties-editor-design.md) - <c>RecordBookEdit</c> +
/// the <see cref="MetadataEditTarget.Book"/> branch in <c>Apply</c>, plus a mixed book/issue stack.
/// Temp SQLite via <see cref="PaperbunkrDbContext.DatabasePathOverride"/> - shares
/// <see cref="AvaloniaTestCollection"/> so it doesn't race other tests over that process-global.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class MetadataEditHistoryServiceTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public MetadataEditHistoryServiceTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_history_test_{Guid.NewGuid():N}.db");
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

    private int AddBook(string title, string? author = null, int? seriesId = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var book = new Book { Title = title, Author = author, BookSeriesId = seriesId, Format = BookFormat.Epub, FilePath = $@"C:\b\{title}.epub" };
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    [Fact]
    public void RecordBookEdit_ThenUndo_RestoresEveryBookField_ThenRedoReapplies()
    {
        int seriesId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var s = new BookSeries { Name = "S" };
            context.BookSeries.Add(s);
            context.SaveChanges();
            seriesId = s.Id;
        }
        int bookId = AddBook("Before", author: "A1");

        var history = new MetadataEditHistoryService();
        Dictionary<string, string?> before, after;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var book = context.Books.Single(b => b.Id == bookId);
            before = BookMetadataSnapshot.Capture(book);
            book.Title = "After";
            book.Author = "A2";
            book.BookSeriesId = seriesId;
            context.SaveChanges();
            after = BookMetadataSnapshot.Capture(book);
        }
        history.RecordBookEdit("Edited", bookId, before, after);

        Assert.Equal("Edited", history.Undo(PaperbunkrDb.CreateContext));
        using (var context = PaperbunkrDb.CreateContext())
        {
            var book = context.Books.Single(b => b.Id == bookId);
            Assert.Equal("Before", book.Title);
            Assert.Equal("A1", book.Author);
            Assert.Null(book.BookSeriesId);
        }

        Assert.Equal("Edited", history.Redo(PaperbunkrDb.CreateContext));
        using (var context = PaperbunkrDb.CreateContext())
        {
            var book = context.Books.Single(b => b.Id == bookId);
            Assert.Equal("After", book.Title);
            Assert.Equal(seriesId, book.BookSeriesId);
        }
    }

    [Fact]
    public void RecordBookEdits_MultiBook_UndoAndRedoRestoreEveryBook()
    {
        int a = AddBook("A-before", author: "aa");
        int b = AddBook("B-before", author: "bb");

        var history = new MetadataEditHistoryService();
        var before = new Dictionary<int, Dictionary<string, string?>>();
        var after = new Dictionary<int, Dictionary<string, string?>>();
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (var book in context.Books.Where(x => x.Id == a || x.Id == b).ToList())
            {
                before[book.Id] = BookMetadataSnapshot.Capture(book);
                book.Title += "!";
                book.Author = "changed";
            }
            context.SaveChanges();
            foreach (var book in context.Books.Where(x => x.Id == a || x.Id == b).ToList())
            {
                after[book.Id] = BookMetadataSnapshot.Capture(book);
            }
        }
        history.RecordBookEdits("bulk", before, after);

        history.Undo(PaperbunkrDb.CreateContext);
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal("A-before", context.Books.Single(x => x.Id == a).Title);
            Assert.Equal("bb", context.Books.Single(x => x.Id == b).Author);
        }

        history.Redo(PaperbunkrDb.CreateContext);
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal("A-before!", context.Books.Single(x => x.Id == a).Title);
            Assert.Equal("changed", context.Books.Single(x => x.Id == b).Author);
        }
    }

    [Fact]
    public void RecordBookEdit_ClearsRedoStack()
    {
        int bookId = AddBook("B");
        var history = new MetadataEditHistoryService();
        var snap = BookMetadataSnapshot.Capture(new Book { Title = "B" });

        history.RecordBookEdit("first", bookId, snap, snap);
        history.Undo(PaperbunkrDb.CreateContext);
        Assert.True(history.CanRedo);

        history.RecordBookEdit("second", bookId, snap, snap);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void MixedBookAndIssueEdits_UndoInLifoOrder_EachHittingItsOwnTable()
    {
        int issueId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var series = new Series { Name = "Comic" };
            var issue = new Issue { Series = series, Number = "1", Title = "IssueBefore" };
            context.Series.Add(series);
            context.Issues.Add(issue);
            context.SaveChanges();
            issueId = issue.Id;
        }
        int bookId = AddBook("BookBefore");

        var history = new MetadataEditHistoryService();

        // Issue edit recorded first.
        using (var context = PaperbunkrDb.CreateContext())
        {
            var issue = context.Issues.Include(i => i.Series).Include(i => i.Tags).Single(i => i.Id == issueId);
            var b = MetadataEditHistoryService.CaptureSnapshot(issue);
            issue.Title = "IssueAfter";
            context.SaveChanges();
            history.Record("issue", new() { [issueId] = b }, new() { [issueId] = MetadataEditHistoryService.CaptureSnapshot(issue) });
        }

        // Book edit recorded second.
        using (var context = PaperbunkrDb.CreateContext())
        {
            var book = context.Books.Single(x => x.Id == bookId);
            var b = BookMetadataSnapshot.Capture(book);
            book.Title = "BookAfter";
            context.SaveChanges();
            history.RecordBookEdit("book", bookId, b, BookMetadataSnapshot.Capture(book));
        }

        // LIFO: book undoes first.
        Assert.Equal("book", history.Undo(PaperbunkrDb.CreateContext));
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal("BookBefore", context.Books.Single(x => x.Id == bookId).Title);
            Assert.Equal("IssueAfter", context.Issues.Single(i => i.Id == issueId).Title);
        }

        Assert.Equal("issue", history.Undo(PaperbunkrDb.CreateContext));
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal("IssueBefore", context.Issues.Single(i => i.Id == issueId).Title);
        }
    }
}
