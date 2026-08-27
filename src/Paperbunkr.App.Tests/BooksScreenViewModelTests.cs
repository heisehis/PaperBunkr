using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="BooksScreenViewModel"/> after folder management moved to Preferences →
/// Libraries (docs/superpowers/specs/2026-08-27-books-section-restyle-and-folders-to-preferences-
/// plan.md) - the screen is now just the cover grid + reader routing + delete. Redirects
/// <see cref="PaperbunkrDbContext.DatabasePathOverride"/> to a temp SQLite file, same pattern as
/// <see cref="LibraryScreenViewModelTests"/>. Runs under <see cref="AvaloniaTestCollection"/> since
/// <see cref="BookCardSample"/> builds cover brushes.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BooksScreenViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public BooksScreenViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_books_vm_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static BooksScreenViewModel CreateViewModel(Action<int, BookFormat>? goReader = null, Action? goLibrarySettings = null) =>
        new(goReader ?? ((_, _) => { }), goLibrarySettings ?? (() => { }));

    private static int AddBook(string title, BookFormat format = BookFormat.Epub, string? filePath = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var book = new Book { Title = title, Format = format, FilePath = filePath ?? $@"C:\books\{title}.epub" };
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    [Fact]
    public void LoadFromDatabase_LoadsBooks_IgnoresBookFolders()
    {
        AddBook("Dune");
        AddBook("Hyperion");
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.BookFolders.Add(new BookFolder { Path = @"D:\Novels" });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadFromDatabase();

        Assert.True(vm.HasBooks);
        Assert.Equal(2, vm.Books.Count);
    }

    [Fact]
    public void SelectBook_InvokesReaderCallback_WithIdAndFormat()
    {
        (int Id, BookFormat Format)? captured = null;
        int id = AddBook("Neuromancer", BookFormat.Pdf);
        var vm = CreateViewModel(goReader: (bookId, format) => captured = (bookId, format));
        vm.LoadFromDatabase();

        vm.SelectBookCommand.Execute(vm.Books.Single(b => b.BookId == id));

        Assert.Equal((id, BookFormat.Pdf), captured);
    }

    [Fact]
    public void OpenLibrarySettings_InvokesCallback()
    {
        bool called = false;
        var vm = CreateViewModel(goLibrarySettings: () => called = true);

        vm.OpenLibrarySettingsCommand.Execute(null);

        Assert.True(called);
    }

    [Fact]
    public void DeleteBook_RemovesRowAndReloads()
    {
        int id = AddBook("Snow Crash");
        var vm = CreateViewModel();
        vm.LoadFromDatabase();

        vm.DeleteBookCommand.Execute(id);

        Assert.False(vm.HasBooks);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.Books);
    }
}
