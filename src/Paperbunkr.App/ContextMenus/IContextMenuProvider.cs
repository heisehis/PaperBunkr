using System.Collections.Generic;

namespace Paperbunkr.App.ContextMenus;

/// <summary>
/// A view model that can supply a right-click menu for its items. Implemented per screen;
/// <see cref="Controls.ContextMenuHost"/> discovers it from the visual tree and renders whatever it
/// returns.
/// </summary>
public interface IContextMenuProvider
{
    /// <summary>
    /// Build the menu for a right-click whose nearest data context is <paramref name="target"/>
    /// (an item view model such as <c>IssueListRow</c>, or <see langword="null"/> for empty space).
    /// Return <see langword="null"/> to show no menu - the host then keeps walking up the tree.
    /// </summary>
    IReadOnlyList<ContextMenuEntry>? BuildContextMenu(object? target);
}
