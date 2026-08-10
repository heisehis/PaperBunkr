namespace Paperbunkr.App.Models;

/// <summary>One row in the Books screen's Book Folders list - mirrors <see cref="WatchedFolderSummary"/> for the comic library.</summary>
public class BookFolderSummary
{
    public int Id { get; init; }

    public string Path { get; init; } = string.Empty;
}
