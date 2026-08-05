using Avalonia.Media;

namespace Paperbunkr.App.Models;

/// <summary>Sample data for a page thumbnail in the Reader screen's left rail.</summary>
public sealed class ReaderThumbnailSample
{
    public bool IsSelected { get; init; }
    public required IBrush CoverBrush { get; init; }
}
