using System.Collections.Generic;
using System.Linq;
using FluentIcons.Common;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Builds the Books screen's right-click menus as plain <see cref="ContextMenuEntry"/> data - same
/// mechanism/shape as <see cref="LibraryContextMenuBuilder"/> (docs/superpowers/specs/2026-08-29-
/// context-menu-rebuild-design.md), just for the two targets this screen's grid has: a book tile
/// and a series-group header. No multi-select awareness here (unlike Library's builder) - this
/// screen's Edit…/Delete Book tile actions were already single-book before the old broken
/// `ContextMenu` markup, and widening their scope to the current selection wasn't asked for.
/// </summary>
public sealed class BooksContextMenuBuilder
{
    private readonly BooksScreenViewModel _vm;

    public BooksContextMenuBuilder(BooksScreenViewModel vm) => _vm = vm;

    public IReadOnlyList<ContextMenuEntry>? Build(object? target) => target switch
    {
        BookCardSample card => BuildBookMenu(card),
        BookCardGroup group when group.BookSeriesId is int seriesId => BuildSeriesGroupMenu(seriesId),
        _ => null,
    };

    private IReadOnlyList<ContextMenuEntry> BuildBookMenu(BookCardSample card) => new[]
    {
        ContextMenuEntry.Item("Edit…", _vm.EditBookCommand, card.BookId, Symbol.Edit),
        ContextMenuEntry.SubMenu("Add to Collection", CollectionChildren(card.BookId), Symbol.CollectionsAdd),
        ContextMenuEntry.Separator,
        ContextMenuEntry.SubMenu(
            "Delete Book…",
            new[] { ContextMenuEntry.Item("Yes, delete this book", _vm.DeleteBookCommand, card.BookId) },
            Symbol.Delete,
            isDanger: true),
    };

    private IReadOnlyList<ContextMenuEntry> BuildSeriesGroupMenu(int bookSeriesId) => new[]
    {
        ContextMenuEntry.Item("Edit series…", _vm.EditSeriesCommand, bookSeriesId, Symbol.Edit),
    };

    private IEnumerable<ContextMenuEntry?> CollectionChildren(int bookId)
    {
        foreach (var collection in _vm.Collections)
        {
            yield return ContextMenuEntry.Item(collection.Name, _vm.AddBookToCollectionCommand, (bookId, collection.Id));
        }

        if (_vm.Collections.Count > 0)
        {
            yield return ContextMenuEntry.Separator;
        }

        yield return ContextMenuEntry.Item("New collection…", _vm.CreateCollectionAndAddBookCommand, bookId);
    }
}
