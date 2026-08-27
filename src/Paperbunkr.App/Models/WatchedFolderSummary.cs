namespace Paperbunkr.App.Models;

/// <summary>One row in the Preferences Libraries tab's Book Folders list.</summary>
public class WatchedFolderSummary
{
    public int Id { get; init; }

    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Bound two-way to the row's checkbox - by the time <c>ToggleWatchCommand</c> runs, this
    /// already reflects the post-click state (standard <c>ToggleButton</c> behavior), so the
    /// command handler just persists whatever value is here.
    /// </summary>
    public bool Watch { get; set; }
}
