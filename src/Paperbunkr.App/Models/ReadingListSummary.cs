using System;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Models;

/// <summary>Sidebar row for one <c>ReadingList</c> — name, item count, and whether it's the currently open list.</summary>
public class ReadingListSummary
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int TotalCount { get; init; }

    public bool IsActive { get; init; }

    /// <summary>Deletes the whole list (docs/superpowers/specs/2026-08-22-delete-functionality-design.md) - not to be confused with removing one item from within an open list.</summary>
    public required TwoStepConfirm DeleteConfirm { get; init; }

    /// <summary>Whether this list carries the given tag value (docs/superpowers/specs/2026-08-23-reading-list-tags-design.md) - drives the sidebar's tag-click filter. A closure over the loaded ReadingList.Tags rather than a stored collection, since the sidebar never needs to display which tags a list has, only test membership.</summary>
    public required Func<string, bool> HasTag { get; init; }
}
