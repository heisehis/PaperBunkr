using System.Collections.Generic;
using System.Linq;
using FluentIcons.Common;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels.LibraryActions;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Builds the Library screen's right-click menus as plain <see cref="ContextMenuEntry"/> data.
/// Split out of <see cref="LibraryScreenViewModel"/> (already ~1900 lines) and reads only that
/// view model's public command surface + selection controllers, so the whole menu for any target
/// is assertable in a plain unit test.
///
/// Right-click semantics come for free from the commands themselves: each already routes through
/// <c>Selection.UnionForAction(id)</c>, so a menu on an unselected tile acts on just that tile and
/// a menu on a tile within a selection acts on the whole selection. This builder only mirrors that
/// into the <em>labels</em> ("Delete 4 comics").
/// </summary>
public sealed class LibraryContextMenuBuilder
{
    private readonly LibraryScreenViewModel _vm;

    public LibraryContextMenuBuilder(LibraryScreenViewModel vm) => _vm = vm;

    /// <summary>
    /// Issue and series menus come from <see cref="LibraryActionCatalog"/> (docs/superpowers/specs/2026-09-29-library-bulk-actions-design.md
    /// §1) - the same definitions the selection bar and the keyboard use. The empty-space and preview-panel menus stay here.
    /// </summary>
    public IReadOnlyList<ContextMenuEntry>? Build(object? target)
    {
        if (target is null)
        {
            return BuildEmptyMenu();
        }

        var catalog = new LibraryActionCatalog(_vm);
        return catalog.ForMenu(target) is { } context ? catalog.BuildMenu(context) : null;
    }

    private IReadOnlyList<ContextMenuEntry>? BuildEmptyMenu()
    {
        var entry = _vm.IsSeriesGranularity
            ? ContextMenuEntry.Item("Select All", _vm.SelectAllVisibleSeriesCommand, icon: Symbol.SelectAllOn)
            : ContextMenuEntry.Item("Select All", _vm.SelectAllVisibleIssuesCommand, icon: Symbol.SelectAllOn);
        return new[] { entry };
    }

    /// <summary>
    /// The preview panel's "⋯" overflow (docs/superpowers/specs/2026-09-26-library-preview-panel-v2-design.md §2/§3): the
    /// rarely used actions, so the pinned bar stays one row wide at 280 px. Remote books get no collection / reveal entries,
    /// for the same reason <see cref="BuildRemoteIssueMenu"/> omits them.
    /// </summary>
    public IReadOnlyList<ContextMenuEntry> BuildPreviewOverflow(object? target)
    {
        var entries = new List<ContextMenuEntry?>();
        switch (target)
        {
            case IssueListRow row:
                entries.Add(ContextMenuEntry.Item("Go to Series", _vm.GoToSeriesCommand, row.SeriesId, Symbol.ArrowForward));
                if (!row.IsRemote)
                {
                    entries.Add(ContextMenuEntry.SubMenu("Add to Collection", CollectionChildren(row.Id, _vm.AddIssueToCollectionCommand, _vm.CreateCollectionAndAddIssueCommand), Symbol.CollectionsAdd));
                    if (row.HasFile)
                    {
                        entries.Add(ContextMenuEntry.Item("Reveal in Explorer", _vm.RevealIssueCommand, row.Id, Symbol.FolderOpen));
                    }
                }

                break;
            case SeriesCardSample card:
                entries.Add(ContextMenuEntry.Item("Open series page", _vm.GoToSeriesCommand, card.SeriesId, Symbol.ArrowForward));
                if (!card.IsRemote)
                {
                    entries.Add(ContextMenuEntry.SubMenu("Add to Collection", CollectionChildren(card.SeriesId, _vm.AddSeriesToCollectionCommand, _vm.CreateCollectionAndAddSeriesCommand), Symbol.CollectionsAdd));
                    if (card.HasFile)
                    {
                        entries.Add(ContextMenuEntry.Item("Reveal in Explorer", _vm.RevealSeriesCommand, card, Symbol.FolderOpen));
                    }
                }

                break;
        }

        return ContextMenuEntry.Compact(entries);
    }

    /// <summary>The preview panel's own "Add to Collection" button: just the collection list (existing collections + "New collection…").</summary>
    public IReadOnlyList<ContextMenuEntry> BuildPreviewCollectionMenu(object? target) => target switch
    {
        IssueListRow row => ContextMenuEntry.Compact(CollectionChildren(row.Id, _vm.AddIssueToCollectionCommand, _vm.CreateCollectionAndAddIssueCommand).ToList()),
        SeriesCardSample card => ContextMenuEntry.Compact(CollectionChildren(card.SeriesId, _vm.AddSeriesToCollectionCommand, _vm.CreateCollectionAndAddSeriesCommand).ToList()),
        _ => System.Array.Empty<ContextMenuEntry>(),
    };

    /// <summary>Shared "Add to Collection ▸" child list for an issue or series target - one item per
    /// existing collection (command/parameter shape differs per target type, so the caller passes
    /// its own commands) plus a trailing "New collection…".</summary>
    private IEnumerable<ContextMenuEntry?> CollectionChildren(int targetId, System.Windows.Input.ICommand addCommand, System.Windows.Input.ICommand createCommand)
    {
        foreach (var collection in _vm.Collections)
        {
            yield return ContextMenuEntry.Item(collection.Name, addCommand, (targetId, collection.Id));
        }

        if (_vm.Collections.Count > 0)
        {
            yield return ContextMenuEntry.Separator;
        }

        yield return ContextMenuEntry.Item("New collection…", createCommand, targetId);
    }
}
