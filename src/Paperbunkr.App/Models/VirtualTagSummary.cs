namespace Paperbunkr.App.Models;

/// <summary>One row in the Preferences Libraries tab's Virtual Tags list.</summary>
public class VirtualTagSummary
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool IsEnabled { get; init; }

    /// <summary>Row-highlight state (docs/superpowers/specs/2026-09-07-virtual-tags-editor-
    /// redesign-design.md) - computed fresh by <c>RefreshVirtualTags</c> on every rebuild rather
    /// than mutated in place, same "always re-query" convention the rest of this ViewModel's list
    /// refreshes already use.</summary>
    public bool IsSelected { get; init; }
}
