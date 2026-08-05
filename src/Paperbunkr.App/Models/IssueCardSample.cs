using Avalonia.Media;

namespace Paperbunkr.App.Models;

/// <summary>Sample data for an issue cover tile (Detail screen's Issues tab).</summary>
public sealed class IssueCardSample
{
    public required string Title { get; init; }
    public bool IsUnread { get; init; }
    public required IBrush CoverBrush { get; init; }
}
