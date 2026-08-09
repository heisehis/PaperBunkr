namespace Paperbunkr.App.Models;

/// <summary>Sidebar row for one <c>SmartList</c> — name, live match count, and whether it's the currently open list.</summary>
public class SmartListSummary
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int MatchCount { get; init; }

    public bool IsActive { get; init; }
}
