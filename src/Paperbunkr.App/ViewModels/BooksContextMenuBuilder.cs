using System.Collections.Generic;
using System.Linq;
using FluentIcons.Common;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;

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

    private IReadOnlyList<ContextMenuEntry> BuildBookMenu(BookCardSample card)
    {
        var entries = new List<ContextMenuEntry>
        {
            ContextMenuEntry.Item("Edit…", _vm.EditBookCommand, card.BookId, Symbol.Edit),
            ContextMenuEntry.Item("Match metadata…", _vm.MatchBookCommand, card.BookId, Symbol.Search),
        };
        AddIfPresent(entries, ContextMenuEntry.SubMenu("Set status", StatusChildren(card.BookId), Symbol.CheckmarkCircle));
        AddIfPresent(entries, ContextMenuEntry.SubMenu("Rate", RatingChildren(card.BookId), Symbol.Star));
        AddIfPresent(entries, ContextMenuEntry.SubMenu("Add to Collection", CollectionChildren(card.BookId), Symbol.CollectionsAdd));

        // Plugin API v2 Books hook (docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-hooks-
        // plan.md §3/§4) - one entry per enabled command, same "no anchor if nothing's registered"
        // gating Library's own plugin entry uses (_vm.HasPluginHost).
        var pluginCommands = _vm.HasPluginHost
            ? _vm.NovelBooksPluginCommands.Where(c => c.ShowsInMenu(Command.MenuNames.Books)).ToList()
            : new List<Command>();
        if (pluginCommands.Count > 0)
        {
            AddIfPresent(entries, ContextMenuEntry.SubMenu(
                "Plugins",
                pluginCommands.Select(cmd => ContextMenuEntry.Item(cmd.Name, _vm.RunNovelBooksPluginCommand, (card.BookId, cmd))),
                Symbol.Apps));
        }

        entries.Add(ContextMenuEntry.Separator);
        AddIfPresent(entries, ContextMenuEntry.SubMenu(
            "Delete Book…",
            new[] { ContextMenuEntry.Item("Yes, delete this book", _vm.DeleteBookCommand, card.BookId, Symbol.Delete, isDanger: true) },
            Symbol.Delete,
            isDanger: true));

        return entries;
    }

    private IReadOnlyList<ContextMenuEntry> BuildSeriesGroupMenu(int bookSeriesId) => new[]
    {
        ContextMenuEntry.Item("Edit series…", _vm.EditSeriesCommand, bookSeriesId, Symbol.Edit),
    };

    private static void AddIfPresent(List<ContextMenuEntry> entries, ContextMenuEntry? entry)
    {
        if (entry is not null)
        {
            entries.Add(entry);
        }
    }

    private IEnumerable<ContextMenuEntry?> StatusChildren(int bookId)
    {
        foreach (var status in System.Enum.GetValues<BookReadingStatus>())
        {
            yield return ContextMenuEntry.Item(BookStatusLabels.ToLabel(status), _vm.SetBookStatusCommand, (bookId, status));
        }
    }

    private IEnumerable<ContextMenuEntry?> RatingChildren(int bookId)
    {
        for (int half = 10; half >= 1; half--)
        {
            float stars = half / 2f;
            yield return ContextMenuEntry.Item(
                stars == 1f ? "1 star" : $"{stars:0.#} stars", _vm.RateBookCommand, (bookId, (float?)stars));
        }

        yield return ContextMenuEntry.Separator;
        yield return ContextMenuEntry.Item("Clear rating", _vm.RateBookCommand, (bookId, (float?)null));
    }

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
