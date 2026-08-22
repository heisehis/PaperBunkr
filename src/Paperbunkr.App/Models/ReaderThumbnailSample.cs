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

    /// <summary>Drives the corner ribbon indicator (docs/superpowers/specs/2026-08-18-metadata-model-ui-gaps-status-and-bookmarks-design.md) - CE draws the same kind of marker on its own thumbnail rail.</summary>
    public bool IsBookmarked { get; init; }
}
