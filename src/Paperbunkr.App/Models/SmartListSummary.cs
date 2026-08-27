using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Models;

/// <summary>Sidebar row for one <c>SmartList</c> — name, live match count, and whether it's the currently open list.</summary>
public class SmartListSummary
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int MatchCount { get; init; }

    public bool IsActive { get; init; }

    /// <summary>
    /// Deletes the whole list (docs/superpowers/specs/2026-08-22-delete-functionality-design.md) -
    /// null for a built-in/maintenance list, which CE-parity read-only rules already forbid editing
    /// let alone deleting (see <c>SmartScreenViewModel.LoadSmartList</c>'s own <c>IsReadOnly</c>).
    /// </summary>
    public TwoStepConfirm? DeleteConfirm { get; init; }
}
