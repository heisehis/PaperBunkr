using System.Collections.Generic;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Builds the MangaDetail chapter row's right-click menu as plain <see cref="ContextMenuEntry"/> data
/// (docs/superpowers/specs/2026-08-31-keyboard-operability-design.md) - ports the menu that used to
/// live in a dead <c>Button.ContextMenu</c>/<c>ContextMenu</c> element (confirmed via
/// <c>ContextMenuHost</c>'s own doc comment: a plain <c>ContextMenu</c> popup does not render at all
/// in this Avalonia 12 + FluentAvalonia build) - same content, same commands, now actually reachable
/// by mouse and keyboard both. Mirrors <see cref="LibraryContextMenuBuilder"/>'s shape.
/// </summary>
public sealed class MangaDetailContextMenuBuilder
{
    private readonly MangaDetailScreenViewModel _vm;

    public MangaDetailContextMenuBuilder(MangaDetailScreenViewModel vm) => _vm = vm;

    public IReadOnlyList<ContextMenuEntry>? Build(object? target) => target switch
    {
        ChapterRowSample row => BuildChapterMenu(row),
        _ => null,
    };

    private IReadOnlyList<ContextMenuEntry> BuildChapterMenu(ChapterRowSample row) => new[]
    {
        ContextMenuEntry.Item("Edit Properties", _vm.EditChapterPropertiesCommand, row),
        ContextMenuEntry.Item("Show in Explorer", _vm.RevealChapterCommand, row),
        ContextMenuEntry.Separator,
        ContextMenuEntry.Item("Mark as Read", _vm.MarkChapterReadCommand, row),
        ContextMenuEntry.Item("Mark as Unread", _vm.MarkChapterUnreadCommand, row),
        ContextMenuEntry.Separator,
        ContextMenuEntry.Item("Set Cover…", _vm.ChangeChapterCoverCommand, row),
        ContextMenuEntry.Item("Reset Cover", _vm.ResetChapterCoverCommand, row),
    };
}
