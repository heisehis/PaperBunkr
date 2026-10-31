using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>The Books "Shelves" menu (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §4).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookShelfServiceTests : IDisposable
{
    private readonly string? _originalOverride;
    private readonly string _dbPath;

    public BookShelfServiceTests()
    {
        _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_shelves_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private static int Seed(string title, Action<Book>? configure = null)
    {
        var book = new Book { Title = title, Format = BookFormat.Epub, FilePath = $@"C:\b\{title}.epub", AddedTime = DateTime.UtcNow };
        configure?.Invoke(book);
        using var context = PaperbunkrDb.CreateContext();
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private static BookShelf Built(BookShelfKind kind) => new(kind.ToString(), kind.ToString(), kind);

    [Fact]
    public void BuiltIns_FollowTheirRules()
    {
        int loved = Seed("Loved", b => b.Rating = 4f);
        int meh = Seed("Meh", b => b.Rating = 2.5f);
        int none = Seed("None");
        int reading = Seed("Reading", b => b.Status = BookReadingStatus.Reading);
        int finished = Seed("Finished", b => { b.Status = BookReadingStatus.Finished; b.LastChapterIndex = 9; });
        int dropped = Seed("Dropped", b => { b.Status = BookReadingStatus.Abandoned; b.LastChapterIndex = 3; });
        int midway = Seed("Midway", b => { b.Status = BookReadingStatus.Unread; b.LastChapterIndex = 2; });
        var service = new BookShelfService();

        Assert.Equal(new[] { loved }, service.Resolve(Built(BookShelfKind.Favorites)).OrderBy(i => i));
        Assert.Equal(new[] { none, reading, finished, dropped, midway }.OrderBy(i => i), service.Resolve(Built(BookShelfKind.NotRated)).OrderBy(i => i));
        Assert.Equal(new[] { reading, midway }.OrderBy(i => i), service.Resolve(Built(BookShelfKind.Unfinished)).OrderBy(i => i));
    }

    [Fact]
    public void List_HasBuiltIns_NovelSmartLists_AndOnlyCollectionsWithBooks()
    {
        int book = Seed("Dune");
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.SmartLists.Add(new SmartList { Name = "Long reads", TargetKind = SmartListTargetKind.Novel, RootGroup = new SmartListConditionGroup { Mode = SmartListGroupMode.And } });
            context.SmartLists.Add(new SmartList { Name = "Comic list", TargetKind = SmartListTargetKind.Issue, RootGroup = new SmartListConditionGroup { Mode = SmartListGroupMode.And } });
            context.SaveChanges();

            var withBook = Paperbunkr.Data.Collections.CollectionService.Create(context, "Beach reads");
            Paperbunkr.Data.Collections.CollectionService.AddItems(context, withBook.Id, bookIds: new[] { book });
            Paperbunkr.Data.Collections.CollectionService.Create(context, "Empty one");
        }

        var shelves = new BookShelfService().List();

        Assert.Equal(new[] { "Favorites", "Unfinished", "Not rated" }, shelves.Where(s => s.IsBuiltIn).Select(s => s.Name));
        Assert.Equal(new[] { "Long reads" }, shelves.Where(s => s.Kind == BookShelfKind.SmartList).Select(s => s.Name));
        Assert.Equal(new[] { "Beach reads" }, shelves.Where(s => s.Kind == BookShelfKind.Collection).Select(s => s.Name));
    }

    [Fact]
    public void SmartListAndCollectionShelves_ResolveToTheirBooks()
    {
        int tor = Seed("Tor book", b => b.Publisher = "Tor");
        int ace = Seed("Ace book", b => b.Publisher = "Ace");
        int listId, collectionId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var list = new SmartList
            {
                Name = "Tor only", TargetKind = SmartListTargetKind.Novel,
                RootGroup = new SmartListConditionGroup
                {
                    Mode = SmartListGroupMode.And,
                    Conditions = { new SmartListCondition { Field = SmartListField.NovelPublisher, Operator = SmartListOperator.Is, Value = "Tor" } },
                },
            };
            context.SmartLists.Add(list);
            context.SaveChanges();
            listId = list.Id;

            var collection = Paperbunkr.Data.Collections.CollectionService.Create(context, "Picks");
            Paperbunkr.Data.Collections.CollectionService.AddItems(context, collection.Id, bookIds: new[] { ace });
            collectionId = collection.Id;
        }

        var service = new BookShelfService();

        Assert.Equal(new[] { tor }, service.Resolve(new BookShelf("s", "Tor only", BookShelfKind.SmartList, listId)));
        Assert.Equal(new[] { ace }, service.Resolve(new BookShelf("c", "Picks", BookShelfKind.Collection, collectionId)));
        Assert.Empty(service.Resolve(new BookShelf("x", "Gone", BookShelfKind.SmartList, 9999)));
    }

    [Fact]
    public void ChoosingAShelf_NarrowsTheGrid_ComposesWithTabsAndSearch_AndClears()
    {
        Seed("Dune", b => { b.Rating = 5f; b.Status = BookReadingStatus.Finished; });
        Seed("Dune Messiah", b => { b.Rating = 4.5f; b.Status = BookReadingStatus.Reading; });
        Seed("Emma", b => b.Rating = 5f);
        Seed("Hobbit");
        var vm = new BooksScreenViewModel(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { });
        vm.LoadFromDatabase();
        vm.ToggleShelvesCommand.Execute(null);
        Assert.True(vm.IsShelvesOpen);
        var favorites = vm.BuiltInShelves.Single(r => r.Name == "Favorites");

        vm.SelectShelfCommand.Execute(favorites);

        Assert.True(vm.HasActiveShelf);
        Assert.Equal("Shelf: Favorites", vm.ShelfButtonLabel);
        Assert.True(favorites.IsActive);
        Assert.Equal(new[] { "Dune", "Dune Messiah", "Emma" }, vm.Books.Select(b => b.Title).OrderBy(t => t));

        vm.SetStatusTabCommand.Execute(BooksStatusTab.Finished);
        Assert.Equal(new[] { "Dune" }, vm.Books.Select(b => b.Title));

        vm.SetStatusTabCommand.Execute(BooksStatusTab.All);
        vm.SearchQuery = "emma";
        Assert.Equal(new[] { "Emma" }, vm.Books.Select(b => b.Title));

        vm.SearchQuery = string.Empty;
        vm.ClearShelfCommand.Execute(null);
        Assert.False(vm.HasActiveShelf);
        Assert.Equal("Shelves", vm.ShelfButtonLabel);
        Assert.Equal(4, vm.Books.Count);
    }
}
