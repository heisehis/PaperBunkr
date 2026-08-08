using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Models;

/// <summary>Sample data for a page thumbnail in the Reader screen's left rail.</summary>
public sealed class ReaderThumbnailSample
{
    public bool IsSelected { get; init; }
    public required IBrush CoverBrush { get; init; }

    /// <summary>Real decoded page thumbnail, null until the background generation pass reaches this page.</summary>
    public Bitmap? CoverImage { get; init; }
}
