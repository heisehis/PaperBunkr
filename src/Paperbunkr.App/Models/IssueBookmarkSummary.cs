namespace Paperbunkr.App.Models;

/// <summary>One row in the comic Reader's Bookmarks flyout (docs/superpowers/specs/2026-08-18-metadata-model-ui-gaps-status-and-bookmarks-design.md).</summary>
public sealed class IssueBookmarkSummary
{
    public int Id { get; init; }

    public int PageNumber { get; init; }

    public string Label { get; init; } = string.Empty;
}
