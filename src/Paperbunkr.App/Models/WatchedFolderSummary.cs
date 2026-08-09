namespace Paperbunkr.App.Models;

/// <summary>One row in the Preferences Libraries tab's Book Folders list.</summary>
public class WatchedFolderSummary
{
    public int Id { get; init; }

    public string Path { get; init; } = string.Empty;
}
