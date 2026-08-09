namespace Paperbunkr.App.Models;

/// <summary>Sidebar row for one <c>ReadingList</c> — name, item count, and whether it's the currently open list.</summary>
public class ReadingListSummary
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int TotalCount { get; init; }

    public bool IsActive { get; init; }
}
