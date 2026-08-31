using System.Collections.Generic;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Builds BookDetail's two right-click menus as plain <see cref="ContextMenuEntry"/> data
/// (docs/superpowers/specs/2026-08-31-keyboard-operability-design.md) - ports both menus that used
/// to live in dead <c>Button.ContextMenu</c>/<c>ContextMenu</c> elements (confirmed via
/// <c>ContextMenuHost</c>'s own doc comment: a plain <c>ContextMenu</c> popup does not render at all
/// in this Avalonia 12 + FluentAvalonia build). Mirrors <see cref="LibraryContextMenuBuilder"/>'s shape.
/// </summary>
public sealed class BookDetailContextMenuBuilder
{
    private readonly BookDetailScreenViewModel _vm;

    public BookDetailContextMenuBuilder(BookDetailScreenViewModel vm) => _vm = vm;

    public IReadOnlyList<ContextMenuEntry>? Build(object? target) => target switch
    {
        BookBookmarkSummary bookmark => new[]
        {
            ContextMenuEntry.Item("Delete Bookmark", _vm.DeleteBookmarkCommand, bookmark),
        },
        BookCardSample card => new[]
        {
            ContextMenuEntry.Item("Edit…", _vm.EditBookInSeriesCommand, card),
        },
        _ => null,
    };
}
