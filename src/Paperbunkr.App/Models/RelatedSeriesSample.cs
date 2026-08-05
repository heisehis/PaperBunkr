using Avalonia.Media;

namespace Paperbunkr.App.Models;

/// <summary>Sample data for the Detail screen's Related tab carousel.</summary>
public sealed class RelatedSeriesSample
{
    public required string Title { get; init; }
    public required string Name { get; init; }
    public required string Note { get; init; }
    public required IBrush CoverBrush { get; init; }
}
