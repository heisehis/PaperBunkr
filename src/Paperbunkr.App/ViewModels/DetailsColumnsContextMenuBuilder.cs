using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Library's Details-table header column picker (docs/superpowers/specs/2026-08-31-keyboard-
/// operability-design.md) - ports a menu that used to live in a dead
/// <c>ItemsControl.ContextMenu</c>/<c>ContextMenu</c> element (found during a broader sweep, beyond
/// the originally-scoped four dead menus).
///
/// Unlike every other builder in this batch, this one ignores its <c>target</c> argument entirely
/// and is attached via a *second*, more narrowly-scoped <c>ContextMenuHost.Provider</c> directly on
/// the Details header <c>Grid</c> (not the whole Library screen root, which already has its own
/// provider for card/row right-clicks) - the column picker isn't "about" whatever's under the
/// cursor, it always shows the full column list regardless of exactly where in the header row was
/// clicked. Implements <see cref="IContextMenuProvider"/> directly (rather than exposing a plain
/// <c>Build</c> method some other class delegates to) since it's never invoked any other way.
/// </summary>
public sealed class DetailsColumnsContextMenuBuilder : IContextMenuProvider
{
    private readonly IReadOnlyList<DetailsColumn> _columns;

    public DetailsColumnsContextMenuBuilder(IReadOnlyList<DetailsColumn> columns) => _columns = columns;

    public IReadOnlyList<ContextMenuEntry>? BuildContextMenu(object? target) =>
        _columns.Select(column => ContextMenuEntry.Item(column.DisplayName, column.ToggleVisibleCommand, isChecked: column.IsVisible)).ToList();
}
