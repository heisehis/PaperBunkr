using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Covers <see cref="BooksContextMenuBuilder"/> - the Books screen right-click menu as plain data
/// (docs/superpowers/specs/2026-08-29-context-menu-rebuild-design.md's own follow-up list named
/// this screen). Same harness as <see cref="BooksScreenViewModelTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BooksContextMenuBuilderTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public BooksContextMenuBuilderTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_books_ctxmenu_test_{Guid.NewGuid():N}.db");
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

    private static BooksScreenViewModel NewVm() =>
        new(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { });

    private static int AddBook(string title)
    {
        using var context = PaperbunkrDb.CreateContext();
        var book = new Book { Title = title, Format = BookFormat.Epub, FilePath = $@"C:\books\{title}.epub" };
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private static IReadOnlyList<ContextMenuEntry> Menu(BooksScreenViewModel vm, object? target) =>
        ((IContextMenuProvider)vm).BuildContextMenu(target) ?? Array.Empty<ContextMenuEntry>();

    private static IEnumerable<ContextMenuEntry> Flatten(IEnumerable<ContextMenuEntry> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
            if (entry.Children is { } children)
            {
                foreach (var descendant in Flatten(children))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static ContextMenuEntry Find(IEnumerable<ContextMenuEntry> entries, string header) =>
        Flatten(entries).First(e => e.Header == header);

    [Fact]
    public void BookMenu_HasCoreEntries_InOrder()
    {
        AddBook("Alpha Novel");
        var vm = NewVm();
        var card = Assert.Single(vm.Books);

        var headers = Menu(vm, card).Where(e => !e.IsSeparator).Select(e => e.Header).ToList();

        Assert.Equal(new[] { "Edit…", "Add to Collection", "Delete Book…" }, headers);
    }

    [Fact]
    public void BookMenu_EveryActionLeaf_HasACommand()
    {
        AddBook("Alpha Novel");
        var vm = NewVm();
        var card = Assert.Single(vm.Books);

        foreach (var leaf in Flatten(Menu(vm, card)).Where(e => !e.IsSeparator && e.Children is null))
        {
            Assert.True(leaf.Command is not null, $"'{leaf.Header}' has no command");
        }
    }

    [Fact]
    public void AddToCollection_ListsExistingCollections_PlusNewCollection()
    {
        AddBook("Alpha Novel");
        var vm = NewVm();
        using (var context = PaperbunkrDb.CreateContext())
        {
            CollectionService.Create(context, "Favorites");
        }
        vm.LoadFromDatabase();
        var card = Assert.Single(vm.Books);

        var addToCollection = Find(Menu(vm, card), "Add to Collection");
        var childHeaders = addToCollection.Children!.Select(c => c.Header).ToList();

        Assert.Equal(new[] { "Favorites", null, "New collection…" }, childHeaders);
    }

    [Fact]
    public void AddBookToCollection_AddsBookAsMember()
    {
        int bookId = AddBook("Alpha Novel");
        int collectionId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            collectionId = CollectionService.Create(context, "Favorites").Id;
        }
        var vm = NewVm();
        vm.LoadFromDatabase();

        vm.AddBookToCollectionCommand.Execute((bookId, collectionId));

        using var verify = PaperbunkrDb.CreateContext();
        var members = CollectionResolver.GetMembers(verify, collectionId);
        var member = Assert.Single(members);
        Assert.Equal(CollectionMemberKind.Book, member.Kind);
        Assert.Equal(bookId, member.TargetId);
    }

    [Fact]
    public void CreateCollectionAndAddBook_CreatesCollection_AndAddsBook()
    {
        int bookId = AddBook("Alpha Novel");
        var vm = NewVm();

        vm.CreateCollectionAndAddBookCommand.Execute(bookId);

        var collection = Assert.Single(vm.Collections);
        Assert.Equal(1, collection.Count);
    }

    [Fact]
    public void SeriesGroupMenu_HasEditSeriesEntry()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            var series = new BookSeries { Name = "A Trilogy" };
            context.BookSeries.Add(series);
            context.SaveChanges();
            context.Books.Add(new Book { Title = "Book One", Format = BookFormat.Epub, FilePath = @"C:\books\one.epub", BookSeriesId = series.Id });
            context.SaveChanges();
        }
        var vm = NewVm();
        vm.SetGroupFieldCommand.Execute(BooksGroupField.Series);
        var group = vm.Groups.Single(g => g.BookSeriesId is not null);

        var headers = Menu(vm, group).Select(e => e.Header).ToList();

        Assert.Equal(new[] { "Edit series…" }, headers);
    }

    [Fact]
    public void EmptySpace_YieldsNoMenu()
    {
        AddBook("Alpha Novel");
        var vm = NewVm();

        Assert.Null(((IContextMenuProvider)vm).BuildContextMenu(null));
    }
}
