using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Builds a Reading List member row's right-click menu as plain <see cref="ContextMenuEntry"/> data
/// (docs/superpowers/specs/2026-08-31-keyboard-operability-design.md) - a new menu, this screen had
/// none before. Entirely built from <see cref="ReadingListItemRowViewModel"/>'s own existing per-row
/// commands - <c>OpenCommand</c>, a single <c>ToggleReadCommand</c> (not separate Mark Read/Unread -
/// the row only has one toggle, corrected from the design doc's initial guess), <c>SetRoleCommand</c>
/// (one child per <see cref="EventMembershipRoleOption"/>, radio-checked against
/// <see cref="ReadingListItemRowViewModel.SelectedRole"/>), and <c>RemoveCommand</c>.
/// </summary>
public sealed class ReadingListMemberContextMenuBuilder
{
    public IReadOnlyList<ContextMenuEntry>? Build(object? target) => target switch
    {
        ReadingListItemRowViewModel row => BuildRowMenu(row),
        _ => null,
    };

    private static IReadOnlyList<ContextMenuEntry> BuildRowMenu(ReadingListItemRowViewModel row) => new[]
    {
        ContextMenuEntry.Item("Open", row.OpenCommand),
        ContextMenuEntry.Item(row.IsRead ? "Mark as Unread" : "Mark as Read", row.ToggleReadCommand),
        // RoleOptions.All is never empty, so SubMenu here never actually returns null - the ! isn't
        // hiding a real edge case, just satisfying the same nullable-return shape ContextMenuEntry.SubMenu
        // always has (same tolerated pattern already in BooksContextMenuBuilder).
        ContextMenuEntry.SubMenu("Set Role", RoleChildren(row))!,
        ContextMenuEntry.Separator,
        ContextMenuEntry.Item("Remove from List", row.RemoveCommand, isDanger: true),
    };

    private static IEnumerable<ContextMenuEntry?> RoleChildren(ReadingListItemRowViewModel row) =>
        ReadingListItemRowViewModel.RoleOptions.Select(option =>
            ContextMenuEntry.Item(option.Label, row.SetRoleCommand, option, isChecked: row.SelectedRole == option.Role));
}
