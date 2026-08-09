namespace Paperbunkr.App.Models;

/// <summary>One row in the Reading List screen's "Add Issue" search results.</summary>
public class IssueSearchResult
{
    public int IssueId { get; init; }

    public string DisplayLabel { get; init; } = string.Empty;
}
