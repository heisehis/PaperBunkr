using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Library Health's "Duplicate books" section (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §8).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookDuplicatesViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public BookDuplicatesViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookdups_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "db.sqlite");
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private int AddBook(string title, string author, BookFormat format, int bytes = 1024, bool createFile = true, string? series = null)
    {
        string path = Path.Combine(_root, $"{Guid.NewGuid():N}.{format.ToString().ToLowerInvariant()}");
        if (createFile)
        {
            File.WriteAllBytes(path, new byte[bytes]);
        }

        using var context = new PaperbunkrDbContext(_options);
        var book = new Book { Title = title, Author = author, Format = format, FilePath = path, AddedTime = DateTime.UtcNow };
        if (series is not null)
        {
            book.BookSeries = new BookSeries { Name = series };
        }

        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private BookDuplicatesViewModel Create(List<string>? revealed = null, List<string>? recycled = null) =>
        new(() => new PaperbunkrDbContext(_options), action => action(), revealed is null ? null : revealed.Add, recycled is null ? null : recycled.Add);

    [Fact]
    public void Scan_ListsGroups_WithSizes_AndSaysCleanWhenThereAreNone()
    {
        var vm = Create();
        vm.EnsureScanned();
        Assert.True(vm.IsClean);
        Assert.Equal("· No duplicates", vm.HeaderDetail);

        AddBook("Dune", "Frank Herbert", BookFormat.Epub, bytes: 2048);
        AddBook("Dune", "Frank Herbert", BookFormat.Pdf, createFile: false);
        vm.ScanCommand.Execute(null);

        var group = Assert.Single(vm.Rows);
        Assert.Contains("different formats", group.Reason);
        Assert.Equal("· 1 book", vm.HeaderDetail);
        Assert.Equal(new[] { "2 KB", "file missing" }, group.Entries.Select(e => e.SizeLabel).OrderBy(s => s, StringComparer.Ordinal));
        Assert.False(vm.IsClean);
    }

    [Fact]
    public void Reveal_PassesThePath()
    {
        AddBook("Dune", "F", BookFormat.Epub);
        AddBook("Dune", "F", BookFormat.Pdf);
        var revealed = new List<string>();
        var vm = Create(revealed);
        vm.EnsureScanned();
        var entry = vm.Rows[0].Entries[0];

        vm.RevealCommand.Execute(entry);

        Assert.Equal(new[] { entry.Path }, revealed);
    }

    [Fact]
    public void Delete_NeedsAConfirmation_ThenRecyclesTheFile_RemovesTheBook_AndRescans()
    {
        int keep = AddBook("Dune", "F", BookFormat.Epub);
        int drop = AddBook("Dune", "F", BookFormat.Pdf);
        var recycled = new List<string>();
        var vm = Create(recycled: recycled);
        vm.EnsureScanned();
        var target = vm.Rows[0].Entries.Single(e => e.BookId == drop);
        var other = vm.Rows[0].Entries.Single(e => e.BookId == keep);

        vm.AskDeleteCommand.Execute(target);
        Assert.True(target.IsConfirmingDelete);
        Assert.Empty(recycled);

        vm.AskDeleteCommand.Execute(other); // only one question open at a time
        Assert.False(target.IsConfirmingDelete);
        Assert.True(other.IsConfirmingDelete);

        vm.CancelDeleteCommand.Execute(other);
        Assert.False(other.IsConfirmingDelete);

        vm.ConfirmDeleteCommand.Execute(target);

        Assert.Equal(new[] { target.Path }, recycled);
        using var context = new PaperbunkrDbContext(_options);
        Assert.Equal(new[] { keep }, context.Books.Select(b => b.Id).ToArray());
        Assert.True(vm.IsClean); // one copy left: no longer a duplicate
    }

    [Fact]
    public void Delete_PrunesAnEmptiedSeries()
    {
        AddBook("Dune", "F", BookFormat.Epub);
        int drop = AddBook("Dune", "F", BookFormat.Pdf, series: "Lonely series");
        var vm = Create(recycled: new List<string>());
        vm.EnsureScanned();

        vm.ConfirmDeleteCommand.Execute(vm.Rows[0].Entries.Single(e => e.BookId == drop));

        using var context = new PaperbunkrDbContext(_options);
        Assert.Empty(context.BookSeries);
    }
}
