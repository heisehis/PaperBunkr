using System;
using System.Collections.Generic;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels.LibraryActions;

/// <summary>Where a Library action is being offered: a right-click menu (it has a clicked tile) or the selection bar / keyboard
/// (it acts on the current selection).</summary>
public enum LibraryActionSurface
{
    Menu,
    Bar,
}

/// <summary>
/// The items one Library action runs on, resolved once when the menu/bar is built (docs/superpowers/specs/2026-09-29-library-bulk-actions-
/// design.md §1): issue ids, or series ids when <see cref="IsSeries"/>. Passed as the <c>CommandParameter</c> of every target-based command,
/// so the label's count and the command's work always come from the same set.
/// </summary>
public sealed record LibraryTarget(bool IsSeries, IReadOnlyList<int> Ids)
{
    public int Count => Ids.Count;

    public static LibraryTarget Issues(IReadOnlyList<int> ids) => new(false, ids);

    public static LibraryTarget Series(IReadOnlyList<int> ids) => new(true, ids);

    public static readonly LibraryTarget None = new(false, Array.Empty<int>());
}

/// <summary>
/// Everything an action needs to decide whether it shows, what its label says and what it passes to its command. Built by
/// <see cref="LibraryActionCatalog.ForMenu"/> (a clicked tile: the selection ∪ that tile, the same set the union-based commands act on) or
/// <see cref="LibraryActionCatalog.ForSelection"/> (the bar and the keyboard: the current selection).
/// </summary>
public sealed class LibraryActionContext
{
    public required LibraryActionSurface Surface { get; init; }

    public required LibraryTarget Target { get; init; }

    /// <summary>The right-clicked issue row, if any.</summary>
    public IssueListRow? Row { get; init; }

    /// <summary>The right-clicked series card, if any.</summary>
    public SeriesCardSample? Card { get; init; }

    /// <summary>An id inside <see cref="Target"/> for the older union-based commands (<c>MarkIssueRead(int)</c> …): the clicked id for a
    /// menu, the first selected id for the bar - <c>UnionForAction</c> of a selected id is exactly the selection.</summary>
    public required int AnchorId { get; init; }

    /// <summary>The clicked tile is from a remote library - its menu offers only the read-only actions.</summary>
    public bool IsRemote { get; init; }

    /// <summary>At least one targeted item has a file on disk.</summary>
    public bool HasFile { get; init; }

    /// <summary>Distinct series of the targeted items (the series themselves for a series target), for the Series ▸ setters.</summary>
    public required IReadOnlyList<int> SeriesIds { get; init; }

    public bool IsSeries => Target.IsSeries;

    public int Count => Target.Count;

    public bool IsMulti => Target.Count > 1;

    public bool IsMenu => Surface == LibraryActionSurface.Menu;
}
