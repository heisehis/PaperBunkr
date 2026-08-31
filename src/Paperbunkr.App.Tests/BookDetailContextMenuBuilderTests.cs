using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="BookDetailContextMenuBuilder"/> (docs/superpowers/specs/2026-08-31-
/// keyboard-operability-design.md) - both ported menus, formerly dead (plain <c>ContextMenu</c>
/// elements that never render in this Avalonia build). No DB fixture needed - both target models
/// (<see cref="BookBookmarkSummary"/>, <see cref="BookCardSample"/>) are plain init-only records the
/// builder never queries the database for. Still joins <see cref="AvaloniaTestCollection"/> -
/// real bug found running this alongside the other new context-menu test classes: without it, this
/// class (which does construct a real <see cref="BookDetailScreenViewModel"/>, an Avalonia-touching
/// type) ran in true parallel with the Avalonia-collection tests and corrupted their shared state,
/// cascading into unrelated failures across the whole batch.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookDetailContextMenuBuilderTests
{
    private static BookDetailScreenViewModel CreateViewModel() =>
        new(() => { }, (_, _, _) => { }, null, null, null);

    [Fact]
    public void Build_Bookmark_ReturnsDeleteBookmarkEntry()
    {
        var vm = CreateViewModel();
        var builder = new BookDetailContextMenuBuilder(vm);
        var bookmark = new BookBookmarkSummary { Id = 1, ChapterTitle = "Chapter 1" };

        var entries = builder.Build(bookmark);

        var entry = Assert.Single(entries!);
        Assert.Equal("Delete Bookmark", entry.Header);
        Assert.Same(vm.DeleteBookmarkCommand, entry.Command);
        Assert.Same(bookmark, entry.CommandParameter);
    }

    [Fact]
    public void Build_SeriesModeBookCard_ReturnsEditEntry()
    {
        var vm = CreateViewModel();
        var builder = new BookDetailContextMenuBuilder(vm);
        var card = new BookCardSample { BookId = 7, Title = "Book Seven" };

        var entries = builder.Build(card);

        var entry = Assert.Single(entries!);
        Assert.Equal("Edit…", entry.Header);
        Assert.Same(vm.EditBookInSeriesCommand, entry.Command);
        Assert.Same(card, entry.CommandParameter);
    }

    [Fact]
    public void Build_UnrecognizedTarget_ReturnsNull()
    {
        var builder = new BookDetailContextMenuBuilder(CreateViewModel());

        Assert.Null(builder.Build(new object()));
        Assert.Null(builder.Build(null));
    }
}
