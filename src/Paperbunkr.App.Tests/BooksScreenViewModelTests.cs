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

    private static int SeedBook(string title, string? author = null, string? series = null,
        DateTime? added = null, DateTime? lastOpened = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        int? seriesId = null;
        if (series is not null)
        {
            var bs = context.BookSeries.FirstOrDefault(s => s.Name == series) ?? new BookSeries { Name = series };
            if (bs.Id == 0) { context.BookSeries.Add(bs); context.SaveChanges(); }
            seriesId = bs.Id;
        }

        var book = new Book
        {
            Title = title,
            Author = author,
            BookSeriesId = seriesId,
            Format = BookFormat.Epub,
            FilePath = $@"C:\books\{title}.epub",
            AddedTime = added ?? DateTime.UtcNow,
            LastOpenedTime = lastOpened,
        };
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private static void SeedAppSettings(Action<Paperbunkr.Data.Entities.AppSettings> configure)
    {
        using var context = PaperbunkrDb.CreateContext();
        configure(context.GetOrCreateAppSettings());
        context.SaveChanges();
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

    // --- chrome: search / sort / group / persistence
    // (docs/superpowers/specs/2026-08-27-books-screen-chrome-and-home-strip-design.md) ---

    [Fact]
    public void Search_FiltersByTitleAuthorOrSeries()
    {
        SeedBook("Dune", author: "Frank Herbert", series: "Dune Chronicles");
        SeedBook("Neuromancer", author: "William Gibson");
        SeedBook("Hyperion", author: "Dan Simmons", series: "Hyperion Cantos");

        var vm = CreateViewModel();
        vm.LoadFromDatabase();

        vm.SearchQuery = "gibson";
        Assert.Equal(new[] { "Neuromancer" }, vm.Books.Select(b => b.Title));

        vm.SearchQuery = "cantos";
        Assert.Equal(new[] { "Hyperion" }, vm.Books.Select(b => b.Title));

        vm.SearchQuery = "";
        Assert.Equal(3, vm.Books.Count);
    }

    [Fact]
    public void Sort_ByField_AndDirection_OrdersBooks()
    {
        SeedBook("Banana", author: "Zed", added: new DateTime(2024, 1, 1), lastOpened: new DateTime(2024, 6, 1));
        SeedBook("Apple", author: "Amy", added: new DateTime(2024, 3, 1), lastOpened: new DateTime(2024, 5, 1));

        var vm = CreateViewModel();
        vm.LoadFromDatabase();

        vm.SortField = BooksSortField.Title;
        vm.SortDirection = SortDirection.Ascending;
        Assert.Equal(new[] { "Apple", "Banana" }, vm.Books.Select(b => b.Title));

        vm.SortDirection = SortDirection.Descending;
        Assert.Equal(new[] { "Banana", "Apple" }, vm.Books.Select(b => b.Title));

        vm.SortField = BooksSortField.Author;
        vm.SortDirection = SortDirection.Ascending;
        Assert.Equal(new[] { "Apple", "Banana" }, vm.Books.Select(b => b.Title)); // Amy, Zed

        vm.SortField = BooksSortField.RecentlyAdded;
        vm.SortDirection = SortDirection.Descending;
        Assert.Equal(new[] { "Apple", "Banana" }, vm.Books.Select(b => b.Title)); // Apple added later

        vm.SortField = BooksSortField.LastOpened;
        vm.SortDirection = SortDirection.Descending;
        Assert.Equal(new[] { "Banana", "Apple" }, vm.Books.Select(b => b.Title)); // Banana opened later
    }

    [Fact]
    public void GroupBySeries_PutsNoSeriesBooksInStandalone_SortedLast()
    {
        SeedBook("Loner");
        SeedBook("Hyperion", series: "Hyperion Cantos");

        var vm = CreateViewModel();
        vm.LoadFromDatabase();
        vm.GroupField = BooksGroupField.Series;

        Assert.Equal(new[] { "Hyperion Cantos", "Standalone" }, vm.Groups.Select(g => g.Header));
        Assert.Equal("Loner", vm.Groups.Single(g => g.Header == "Standalone").Items.Single().Title);
    }

    [Fact]
    public void GroupByAuthor_PutsBlankAuthorInUnknownAuthor_SortedLast()
    {
        SeedBook("Anonymous Work");
        SeedBook("Known Work", author: "Ada");

        var vm = CreateViewModel();
        vm.LoadFromDatabase();
        vm.GroupField = BooksGroupField.Author;

        Assert.Equal(new[] { "Ada", "Unknown author" }, vm.Groups.Select(g => g.Header));
    }

    [Fact]
    public void SortGroupDirection_PersistToAppSettings_SearchDoesNot()
    {
        var vm = CreateViewModel();
        vm.SortField = BooksSortField.Author;
        vm.SortDirection = SortDirection.Descending;
        vm.GroupField = BooksGroupField.Series;
        vm.SearchQuery = "temp";

        var settings = ReadAppSettings();
        Assert.Equal(BooksSortField.Author, settings.BooksSortField);
        Assert.Equal(SortDirection.Descending, settings.BooksSortDirection);
        Assert.Equal(BooksGroupField.Series, settings.BooksGroupField);

        var reloaded = CreateViewModel();
        Assert.Equal(BooksSortField.Author, reloaded.SortField);
        Assert.Equal(BooksGroupField.Series, reloaded.GroupField);
        Assert.Equal("", reloaded.SearchQuery);
    }

    [Fact]
    public void SeededAppSettings_AreReflectedOnConstruction()
    {
        SeedAppSettings(s =>
        {
            s.BooksSortField = BooksSortField.LastOpened;
            s.BooksGroupField = BooksGroupField.Author;
        });

        var vm = CreateViewModel();

        Assert.Equal(BooksSortField.LastOpened, vm.SortField);
        Assert.Equal(BooksGroupField.Author, vm.GroupField);
        Assert.True(vm.IsGrouped);
    }

    [Fact]
    public void EmptyStates_ShowEmptyLibrary_VersusShowNoMatches()
    {
        var vm = CreateViewModel();
        Assert.True(vm.ShowEmptyLibrary);
        Assert.False(vm.ShowNoMatches);

        SeedBook("Dune");
        vm.LoadFromDatabase();
        Assert.False(vm.ShowEmptyLibrary);

        vm.SearchQuery = "nothing matches this";
        Assert.False(vm.ShowEmptyLibrary);
        Assert.True(vm.ShowNoMatches);
        Assert.False(vm.HasBooks);
    }

    private static Paperbunkr.Data.Entities.AppSettings ReadAppSettings()
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.GetOrCreateAppSettings();
    }
}
